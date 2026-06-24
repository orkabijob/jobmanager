using Orkabi.Web.Tests.Infrastructure;
namespace Orkabi.Web.Tests;

public class RtlLayoutTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public RtlLayoutTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Login_page_renders_rtl_hebrew_shell()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var html = await factory.CreateClient().GetStringAsync("/Account/Login");
        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("lang=\"he\"", html);
        Assert.Contains("tokens.css", html);
        Assert.Contains("base.css", html);
    }

    [Fact]
    public async Task Base_css_contains_people_surface_classes()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var css = await factory.CreateClient().GetStringAsync("/css/base.css");
        // Verify People surface marker classes are present (Task 5 — Slice 1 CSS additions)
        Assert.Contains(".roster-pane", css);
        Assert.Contains(".subnav", css);
    }

    [Fact]
    public async Task Layout_includes_htmx_script_and_csrf_meta()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var html = await factory.CreateClient().GetStringAsync("/Account/Login");
        // htmx script include — allow for asp-append-version querystring
        Assert.Contains("htmx.min.js", html);
        // antiforgery meta tag for HTMX POST wiring
        Assert.Contains("name=\"htmx-csrf\"", html);
    }
}
