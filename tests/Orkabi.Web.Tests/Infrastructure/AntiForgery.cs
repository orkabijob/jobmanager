using System.Text.RegularExpressions;

namespace Orkabi.Web.Tests.Infrastructure;

public static class AntiForgery
{
    private static readonly Regex TokenPattern = new(
        @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Extract(string html)
    {
        var match = TokenPattern.Match(html);
        if (!match.Success)
            throw new InvalidOperationException(
                "Could not find __RequestVerificationToken in the response HTML.");
        return match.Groups[1].Value;
    }
}
