using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class GoogleSchemeTests
{
    [Fact]
    public async Task Google_scheme_is_registered_when_client_id_is_configured()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = "Host=invalid" }
            .WithConfig("Authentication:Google:ClientId", "test-id")
            .WithConfig("Authentication:Google:ClientSecret", "test-secret");
        using var scope = factory.Services.CreateScope();
        var schemes = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync("Google"));

        // the Identity app cookie remains the default challenge scheme, NOT Google (B1)
        var def = await schemes.GetDefaultChallengeSchemeAsync();
        Assert.Equal(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, def?.Name);
    }
}
