using System.Net;
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

    [Fact]
    public async Task Google_challenge_redirects_to_accounts_google_com()
    {
        // Use a temp SQLite file so the DbContext can start up (challenge won't actually hit DB).
        var dbPath = Path.Combine(Path.GetTempPath(), $"orkabi_google_{Guid.NewGuid():N}.db");
        var cs = $"Data Source={dbPath}";
        try
        {
            using var factory = new OrkabiAppFactory { ConnectionString = cs }
                .WithConfig("Authentication:Google:ClientId", "test-id")
                .WithConfig("Authentication:Google:ClientSecret", "test-secret");

            // Prepared() creates the schema so the app can start fully (audit interceptor, etc.)
            factory.Prepared();

            var client = factory.CreateClient(new() { AllowAutoRedirect = false });
            var response = await client.GetAsync("/Account/ExternalLogin?provider=Google");

            // The handler must build a Google OAuth redirect (302) — proves AddGoogle() wired
            // StateDataFormat + Backchannel correctly. Fake creds are fine; the challenge only
            // builds the URL, it does NOT call Google.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location?.ToString() ?? "";
            Assert.StartsWith("https://accounts.google.com/", location);
        }
        finally
        {
            // Release pooled SQLite handles before deleting the file, or Windows throws
            // "file in use" (the factory's DbContext connection is pooled, not closed).
            // Mirrors SqliteFixture.Dispose(); fixes the intermittent file-lock flake.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
