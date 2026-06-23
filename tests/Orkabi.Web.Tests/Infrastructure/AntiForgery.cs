using System.Text.RegularExpressions;

namespace Orkabi.Web.Tests.Infrastructure;

public static class AntiForgery
{
    // Match the hidden input tag that carries the token (attribute order agnostic),
    // then pull its value — so `value` appearing before or after `name` both work.
    private static readonly Regex TokenInput = new(
        @"<input\b[^>]*\bname=""__RequestVerificationToken""[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ValueAttr = new(
        @"\bvalue=""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Extract(string html)
    {
        var input = TokenInput.Match(html);
        if (input.Success)
        {
            var value = ValueAttr.Match(input.Value);
            if (value.Success) return value.Groups[1].Value;
        }
        throw new InvalidOperationException(
            "Could not find __RequestVerificationToken in the response HTML.");
    }
}
