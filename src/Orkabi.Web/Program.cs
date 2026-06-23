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
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

if (!app.Environment.IsEnvironment("Testing"))
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
