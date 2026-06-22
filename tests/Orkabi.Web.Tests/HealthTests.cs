using System.Net;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class HealthTests : IClassFixture<OrkabiAppFactory>
{
    private readonly OrkabiAppFactory _factory;
    public HealthTests(OrkabiAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_200_ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }
}
