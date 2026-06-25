using System.Text.Json;

namespace Orkabi.Web.Shared;

/// <summary>
/// Builds HTMX <c>HX-Trigger</c> response-header values.
///
/// IMPORTANT: HTTP response-header values are serialized by Kestrel as Latin-1 (ISO-8859-1)
/// by default, so any non-ASCII character (e.g. Hebrew) placed raw into a header value is
/// replaced with '?' on the wire. We therefore JSON-serialize the payload with
/// <see cref="JsonSerializer"/>, whose default encoder escapes every non-ASCII rune to a
/// <c>\uXXXX</c> sequence — pure ASCII that survives header transport and is decoded back to
/// the original text by HTMX's <c>JSON.parse</c> in the browser.
/// </summary>
public static class HxTrigger
{
    /// <summary>
    /// Returns an ASCII-safe <c>HX-Trigger</c> value that fires a client-side
    /// <c>showToast</c> event carrying <paramref name="message"/> as <c>detail.msg</c>.
    /// </summary>
    public static string ShowToast(string message) =>
        JsonSerializer.Serialize(new { showToast = new { msg = message } });
}
