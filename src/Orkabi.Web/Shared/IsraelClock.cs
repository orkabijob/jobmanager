namespace Orkabi.Web.Shared;

/// <summary>
/// Single source of truth for Israel time. Store all instants as UTC; convert to IsraelTz
/// ONLY at the presentation edge and in the (Slice 4) job scheduler — DST-correct.
/// .NET 8 resolves the IANA id "Asia/Jerusalem" cross-platform (Windows + Linux).
/// </summary>
public static class IsraelClock
{
    public static readonly TimeZoneInfo IsraelTz =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
}
