using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// TD1 — Identity errors that reach the UI render in Hebrew.
public class IdentityLocalizationTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public IdentityLocalizationTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task PasswordTooShort_error_is_hebrew()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var email = $"short{Guid.NewGuid():N}@t.test";
        var result = await um.CreateAsync(new AppUser { UserName = email, Email = email }, "short");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "PasswordTooShort");
        Assert.Contains(result.Errors, e => e.Description.Contains("תווים"));   // Hebrew "characters"
        Assert.DoesNotContain(result.Errors, e => e.Description.Contains("characters"));
    }

    [Fact]
    public async Task DuplicateEmail_error_is_hebrew()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var email = $"dup{Guid.NewGuid():N}@t.test";
        var first = await um.CreateAsync(new AppUser { UserName = email, Email = email }, "Passw0rd!");
        Assert.True(first.Succeeded);

        var second = await um.CreateAsync(new AppUser { UserName = $"other{Guid.NewGuid():N}@t.test", Email = email }, "Passw0rd!");

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Description.Contains("בשימוש"));   // Hebrew "in use"
    }
}
