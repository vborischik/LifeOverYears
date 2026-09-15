using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IFfmpegProvider
{
    // Returns null when ffmpeg is unavailable and video assembly is skipped.
    Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath);

    // loopTail=false: the video ends on its last image instead of wiping
    // back to the first — n clips, n-1 transitions.
    Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath, bool loopTail);

    // Seconds, from the container. 0 when the file cannot be read.
    Task<double> ProbeDurationAsync(string path);

    // The container's format tags (title, artist, copyright …), keys lowered.
    Task<IReadOnlyDictionary<string, string>> ProbeTagsAsync(string path);

    // Lays a music bed under a silent video: the track from `startSeconds`,
    // trimmed to the video's length, faded at both ends, loudness-normalised,
    // video stream copied untouched. Writes outputPath.
    Task MuxMusicAsync(string videoPath, string trackPath, double startSeconds, string outputPath);
}
