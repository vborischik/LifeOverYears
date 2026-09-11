namespace LifeOverYears.Services;

// Stops the day's YouTube batch before Google does. Ported from
// YoutubePublisher.
//
// The accounting is purely local, and has to be: the youtube.upload scope
// cannot read the quota endpoint, and the only feedback the API gives is a
// 403 quotaExceeded after the fact. The caller keeps the ledger of uploads
// per day and hands today's count in; this class is the arithmetic.
//
// The numbers were checked against Google's current documentation rather
// than the widely repeated ones: videos.insert no longer draws 1600 units
// from the shared 10,000/day pool. It bills to its own bucket at 1 unit per
// call, capped at 100 calls a day. The ceiling below is therefore an
// operating pace we chose, not the API's limit.
public sealed class QuotaGovernor
{
    public const int UploadCostUnits  = 1;
    public const int ApiDailyUploadCap = 100;

    // Our pace, not Google's. Kept low on purpose — a channel that receives
    // a hundred uploads in a day looks like exactly what an abuse heuristic
    // is built to catch.
    public const int DefaultDailyCeiling = 6;

    private readonly int _usedToday;
    private readonly int _ceiling;

    // An override only lowers. A flag that could raise the ceiling would
    // make the safe default meaningless the first time someone typed a big
    // number.
    public QuotaGovernor(int usedToday, int configuredCeiling, int? overrideCeiling = null)
    {
        _usedToday = Math.Max(0, usedToday);

        var ceiling = configuredCeiling > 0 ? configuredCeiling : DefaultDailyCeiling;
        if (overrideCeiling is { } n && n < ceiling)
            ceiling = n;

        _ceiling = Math.Max(0, Math.Min(ceiling, ApiDailyUploadCap));
    }

    public int Ceiling   => _ceiling;
    public int UsedToday => _usedToday;
    public int Remaining => Math.Max(0, _ceiling - _usedToday);
    public bool CanUpload => Remaining > 0;
    public int UnitsSpentToday => _usedToday * UploadCostUnits;
}
