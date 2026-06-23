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
    }
}
