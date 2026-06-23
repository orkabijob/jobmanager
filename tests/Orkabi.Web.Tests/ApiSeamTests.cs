using System.Net;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ApiSeamTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ApiSeamTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Anonymous_api_call_returns_401_not_a_login_redirect()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/api/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode); // 401, NOT 302 to /Account/Login
    }
}
