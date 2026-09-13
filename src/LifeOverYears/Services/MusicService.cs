using System.Text.Json;
using System.Text.RegularExpressions;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// One bed per platform family, chosen for a run, muxed once, credited in the
// description. Ported from YoutubePublisher's MusicLibrary + LoopRenderer
// with the pieces this project does not need left behind (the loop
// crossfade; the state file), and one change of principle: music is laid
// down at publish time, never in the run. The run's timeline.mp4 is the
// silent master; each family gets its own derivative beside it.
//
// Two libraries because two licences: data/music/youtube/ is what YouTube
// may carry, data/music/meta/ what Instagram and Facebook may. The families
// are fixed here rather than configured — a platform's family is a fact
// about the platform, not a per-installation choice.
public sealed class MusicService : IMusicService
{
    public const string YouTubeFamily = "youtube";
    public const string MetaFamily    = "meta";

    private static readonly string[] Extensions = { ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg" };

    private readonly IFfmpegProvider _ffmpeg;
    private readonly string _musicDir;
    private readonly string? _runsDir;
    private readonly bool _required;
    private readonly ILogger<MusicService> _logger;

    // runsDir is where publish.json records live; scanned for the tracks
    // already used so every track is heard once before any repeats. Null
    // means no ledger — pick by hash alone.
    public MusicService(
        IFfmpegProvider ffmpeg, string musicDir, string? runsDir, bool required, ILogger<MusicService> logger)
    {
        _ffmpeg   = ffmpeg;
        _musicDir = musicDir;
        _runsDir  = runsDir;
        _required = required;
        _logger   = logger;
    }

    public string FamilyOf(string platform) =>
        platform.Equals(YouTubeFamily, StringComparison.OrdinalIgnoreCase) ? YouTubeFamily : MetaFamily;

    public string LibraryDir(string family) => Path.Combine(_musicDir, family);

    public IReadOnlyList<string> Files(string family)
    {
        var dir = LibraryDir(family);
        return !Directory.Exists(dir)
            ? Array.Empty<string>()
            : Directory.EnumerateFiles(dir)
                .Where(p => Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
    }

    public async Task<(PublishRequest Request, string TrackFile)> WithMusicAsync(
        string family, PublishRequest request, CancellationToken ct = default)
    {
        var files = Files(family);
        if (files.Count == 0)
        {
            if (_required)
                throw new InvalidOperationException(
                    $"No music in {LibraryDir(family)} — the {family} library is empty, and a silent video is not published. " +
                    "Drop tracks in, or set Publish:Music:Required to false.");
            _logger.LogWarning("No music in {Dir}; publishing {Family} silent", LibraryDir(family), family);
            return (request, "");
        }

        var track  = Pick(files, request.Video.Id, UsedTracks(family));
        var credit = await CreditAsync(track);

        var videoPath = request.Video.FilePath;
        var muxed     = Path.Combine(
            Path.GetDirectoryName(videoPath)!,
            $"{Path.GetFileNameWithoutExtension(videoPath)}.{family}.mp4");

        // Reused, not rebuilt: two Meta targets in one publish share one mux,
        // and a retried publish of the same run does not re-encode.
        if (!File.Exists(muxed))
        {
            var duration    = await _ffmpeg.ProbeDurationAsync(videoPath);
            var trackLength = await _ffmpeg.ProbeDurationAsync(track);
            var start       = StartOffsetFor(request.Video.Id, trackLength, duration);
            await _ffmpeg.MuxMusicAsync(videoPath, track, start, muxed);
        }

        var description = request.Caption.Description.TrimEnd() + "\n\n" + credit;
        var withMusic = request with
        {
            Video   = request.Video with { FilePath = muxed },
            Caption = request.Caption with { Description = description },
        };
        return (withMusic, Path.GetFileName(track));
    }

    // Chosen by hash of the run id, but only from tracks this family has
    // not used yet — a bare hash over the whole library spreads badly (the
    // reference project measured 17 of 50 tracks silent and one under four
    // videos). Draining the unused set first means every track is heard once
    // before any repeats; when everything has been heard, the next lap
    // starts over the full set. Deterministic for a given run id and ledger.
    public static string Pick(IReadOnlyList<string> files, string runId, ISet<string> used)
    {
        var pool = files.Where(f => !used.Contains(Path.GetFileName(f))).ToList();
        if (pool.Count == 0) pool = files.ToList();
        return pool[(int)(Fnv1a(runId) % (uint)pool.Count)];
    }

    // Where in the track the bed starts. A four-minute piece under a
    // sixteen-second clip would otherwise open on the same bar every time;
    // derived from the run id so it varies per video and repeats on a rerun.
    // Stays clear of the last stretch so the fade-out has material.
    public static double StartOffsetFor(string runId, double trackDuration, double clipDuration)
    {
        var usable = trackDuration - clipDuration - 1.0;
        if (usable <= 0) return 0;
        return Fnv1a("offset:" + runId) % (uint)Math.Floor(usable);
    }

    // The ledger: every publish.json under the runs folder that recorded a
    // track for this family. Cheap — a few hundred small files at most.
    public HashSet<string> UsedTracks(string family)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_runsDir is null || !Directory.Exists(_runsDir)) return used;

        foreach (var path in Directory.EnumerateFiles(_runsDir, RunPublishSource.PublishFileName, SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("Music", out var music)
                    && music.ValueKind == JsonValueKind.Object
                    && music.TryGetProperty(family, out var track)
                    && track.GetString() is { Length: > 0 } name)
                    used.Add(name);
            }
            catch (JsonException) { /* a corrupt record is not a reason to stop picking */ }
        }
        return used;
    }

    // The line that travels with a CC-BY track. Built from the file's own
    // tags so it cannot drift from the audio actually mixed in; if the tags
    // are thin, the file name still beats a silent omission.
    public async Task<string> CreditAsync(string trackPath)
    {
        var tags    = await _ffmpeg.ProbeTagsAsync(trackPath);
        var title   = CleanTitle(tags.GetValueOrDefault("title", ""));
        var artist  = tags.GetValueOrDefault("artist", "");
        var license = NormaliseLicense(tags.GetValueOrDefault("copyright", ""));

        var name = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(trackPath) : title;
        var line = string.IsNullOrWhiteSpace(artist) ? $"Music: {name}" : $"Music: {name} by {artist}";
        return license is null ? line : $"{line} ({license})";
    }

    // Composers who release permissively often bake the licence into the
    // title — "Reawakening (CC-BY)". The credit adds it from the copyright
    // tag anyway, so leaving it here prints it twice.
    private static string CleanTitle(string title) =>
        Regex.Replace(title, @"\s*[\(\[]\s*(cc[\s\-]?by(?:[\s\-]?(?:sa|nc|nd|\d(?:\.\d)?))*|cc0)\s*[\)\]]\s*$",
            "", RegexOptions.IgnoreCase).Trim();

    private static string? NormaliseLicense(string raw)
    {
        var v = raw.Trim();
        if (v.Length == 0) return null;
        return v.ToLowerInvariant() switch
        {
            "cc-by" or "ccby" or "cc by" => "CC BY",
            "cc0"                        => "CC0",
            _                            => v,
        };
    }

    public static uint Fnv1a(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
