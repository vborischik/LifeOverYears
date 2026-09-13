using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

// A music bed under the video, chosen per platform family and laid down at
// publish time only. The run's timeline.mp4 stays silent: a platform's
// licence terms decide what plays under it, and YouTube and Meta do not
// accept the same library.
public interface IMusicService
{
    // "youtube" or "meta" — which library the platform draws from.
    string FamilyOf(string platform);

    // Returns the request with Video.FilePath pointing at a muxed copy and the
    // track's credit line appended to the description. The muxed file lands
    // beside the original as video/timeline.{family}.mp4; a second call for
    // the same family reuses it. Throws when the family's library is empty
    // and music is required.
    Task<(PublishRequest Request, string TrackFile)> WithMusicAsync(
        string family, PublishRequest request, CancellationToken ct = default);
}
