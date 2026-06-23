using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

var dbProvider = builder.Configuration["Database:Provider"] ?? "Npgsql";
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (dbProvider == "Sqlite")
        o.UseSqlite(cs);          // inner-loop tests; schema built via EnsureCreated() in the factory
    else
        o.UseNpgsql(cs);          // prod: Neon pooled endpoint
    o.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

builder.Services
    .AddIdentity<AppUser, AppRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Google OAuth: registered only when ClientId is present at startup.
// In prod, the env var is set before the process starts so AddGoogle() runs and wires
// the full handler chain (OAuthPostConfigureOptions, EnsureSignInScheme, etc.).
// In tests, UseSetting injects the value before Build() so the eager read sees it.
// When ClientId is absent (CI without secrets, local dev), Google is simply not registered.
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
// Require BOTH id and secret — registering AddGoogle with one missing throws a confusing
// InvalidOperationException at startup (OAuthPostConfigureOptions validates both).
if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret))
{
    builder.Services.AddAuthentication().AddGoogle(o =>
    {
        o.ClientId = googleId;
        o.ClientSecret = googleSecret;
        // CallbackPath defaults to /signin-google (registered in Google Cloud) — do not override.
    });
}

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "orkabi.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    // Always in Production; SameAsRequest in Dev/Testing so HTTP test/dev login round-trips
    o.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.Headers.Remove("Location");   // a 401 must not carry a redirect Location
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

var app = builder.Build();

// Fix culture to he-IL — all threads and request pipelines speak Hebrew/Israel.
var he = new System.Globalization.CultureInfo("he-IL");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = he;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = he;
var locOptions = new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("he-IL"),
    SupportedCultures = new[] { he },
    SupportedUICultures = new[] { he }
};

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRequestLocalization(locOptions);
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.MapGet("/api/ping", () => Results.Json(new { pong = true })).RequireAuthorization();

if (!app.Environment.IsEnvironment("Testing") && dbProvider != "Sqlite")
{
    var migrateCs = app.Configuration.GetConnectionString("Migrations")
                    ?? app.Configuration.GetConnectionString("Default");
    var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(migrateCs).Options;
    await using (var migrateDb = new AppDbContext(opts))
        await migrateDb.Database.MigrateAsync();

    using var scope = app.Services.CreateScope();
    await DataSeeder.SeedRolesAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program { }
