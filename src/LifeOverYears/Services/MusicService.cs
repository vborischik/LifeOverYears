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
// A folder per family, because a licence is per platform. Meta is the one
// group — Instagram and Facebook are one company under one music library —
// and every other platform is its own family under its own name, so a
// platform added later never borrows another's tracks by accident: with no
// folder of its own it is refused, not published from Meta's. Where each
// folder is comes from Publish:Music:{Family}; an entry that is missing or
// empty means data/music/{family}.
public sealed class MusicService : IMusicService
{
    public const string MetaFamily = "meta";

    public static readonly string DefaultRoot = Path.Combine("data", "music");

    private static readonly HashSet<string> MetaPlatforms =
        new(StringComparer.OrdinalIgnoreCase) { "instagram", "facebook" };

    // .mp4 is here for Meta Sound Collection downloads, which are AAC audio
    // in an mp4 container. The mux maps the track's audio stream only, so a
    // file that also carried video would not leak a picture in.
    private static readonly string[] Extensions = { ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".mp4" };

    private readonly IFfmpegProvider _ffmpeg;
    private readonly IReadOnlyDictionary<string, string> _folders;
    private readonly string? _runsDir;
    private readonly bool _required;
    private readonly ILogger<MusicService> _logger;

    // folders is family → folder, straight from Publish:Music. runsDir is
    // where publish.json records live; scanned for the tracks already used so
    // every track is heard once before any repeats. Null means no ledger.
    public MusicService(
        IFfmpegProvider ffmpeg, IReadOnlyDictionary<string, string>? folders, string? runsDir, bool required,
        ILogger<MusicService> logger)
    {
        _ffmpeg   = ffmpeg;
        _folders  = new Dictionary<string, string>(folders ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        _runsDir  = runsDir;
        _required = required;
        _logger   = logger;
    }

    public string FamilyOf(string platform) =>
        MetaPlatforms.Contains(platform) ? MetaFamily : platform.ToLowerInvariant();

    public string LibraryDir(string family) =>
        _folders.TryGetValue(family, out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(DefaultRoot, family.ToLowerInvariant());

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

        // The posted file is always timeline.{family}.mp4 beside the master,
        // whatever fed the mux — the master itself or a family's silent
        // re-cut (timeline.{family}.silent.mp4). One name per family, so the
        // record, the reviewer and the next publish all find the same file.
        var videoPath = request.Video.FilePath;
        var muxed     = Path.Combine(
            Path.GetDirectoryName(videoPath)!,
            $"{Path.GetFileNameWithoutExtension(RunPublishSource.VideoRelativePath)}.{family}.mp4");

        // Reused, not rebuilt: two Meta targets in one publish share one mux,
        // and a retried publish of the same run does not re-encode. Rebuilt
        // when its input is newer — a re-cut made after the last mux must
        // not be posted with the old picture under it.
        var stale = File.Exists(muxed) && File.GetLastWriteTimeUtc(videoPath) > File.GetLastWriteTimeUtc(muxed);
        if (!File.Exists(muxed) || stale)
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

        // Meta Sound Collection files carry the track's numeric id as the
        // title tag; that is not a name a reader can use, the file name is.
        var name = string.IsNullOrWhiteSpace(title) || title.All(char.IsDigit)
            ? Path.GetFileNameWithoutExtension(trackPath)
            : title;
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
