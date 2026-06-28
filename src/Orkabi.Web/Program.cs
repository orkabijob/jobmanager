using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
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
builder.Services.AddScoped<Orkabi.Web.Modules.People.AcademicYearService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.SchoolService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.ClassService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.ClientService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.EnrollmentService>();
builder.Services.AddScoped<Orkabi.Web.Modules.Scheduling.IShiftInstanceGenerator, Orkabi.Web.Modules.Scheduling.ShiftInstanceGenerator>();
builder.Services.AddScoped<Orkabi.Web.Modules.Scheduling.SchedulingService>();
builder.Services.AddScoped<Orkabi.Web.Modules.Curriculum.CurriculumService>();
builder.Services.AddScoped<Orkabi.Web.Modules.ActionHub.ActionItemService>();
builder.Services.AddScoped<Orkabi.Web.Modules.Dashboard.DashboardMetricsService>();
builder.Services.AddScoped<Orkabi.Web.Modules.Operations.OperationsService>();
builder.Services.AddScoped<Orkabi.Web.Modules.Logistics.SupplyPacingService>();
builder.Services.AddScoped<IOutboxDrainer, OutboxDrainer>();
builder.Services.AddScoped<Orkabi.Web.Jobs.IDailyJobRunner, Orkabi.Web.Jobs.DailyJobService>();
builder.Services.AddHostedService<Orkabi.Web.Jobs.DailyJobScheduler>();

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
        // Internal staff tool — keep it simple: 8+ chars, no complexity requirements.
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireDigit = false;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddErrorDescriber<Orkabi.Web.Modules.Identity.HebrewIdentityErrorDescriber>()
    .AddDefaultTokenProviders();

// Admin user & role management (list / create / assign-roles / enable-disable / reset-password).
builder.Services.AddScoped<Orkabi.Web.Modules.Identity.UserAdminService>();

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
        // Security (final-review I-1): surface the email_verified flag so the callback can gate provisioning.
        o.ClaimActions.MapJsonKey("email_verified", "email_verified");
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

// ── Opportunistic outbox drain ────────────────────────────────────────────────
// After each AUTHENTICATED request completes, fire-and-forget a drain on a FRESH DI scope
// (the request scope — and its AppDbContext — is disposed once the response finishes, so the
// drainer cannot borrow it). Errors are logged inside the drainer; the dedup-key index is the
// backstop against a concurrent double-drain. The dedicated BackgroundService is Slice 4.
// Skipped under Testing: tests call IOutboxDrainer.DrainAsync() directly; the detached Task.Run
// would otherwise race SqliteFixture.Dispose() file-deletion causing intermittent IOException.
if (!app.Environment.IsEnvironment("Testing"))
{
    var drainScopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
    app.Use(async (ctx, next) =>
    {
        await next(ctx);
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = drainScopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IOutboxDrainer>().DrainAsync();
                }
                catch { /* already logged inside the drainer */ }
            });
        }
    });
}

app.MapRazorPages();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.MapGet("/api/ping", () => Results.Json(new { pong = true })).RequireAuthorization();

// ── POST /api/attendance ─────────────────────────────────────────────────────
// Optimistic attendance submit. Cookie-authed (the /api/* → 401 seam handles anonymous).
// Re-checks the date-scope guard server-side (Admin bypasses), validates antiforgery from the
// RequestVerificationToken header, then submits with a client-supplied idempotency key.
// Idempotent: a repeat with the same key returns 409 "already saved" — never a duplicate write.
app.MapPost("/api/attendance", async (
    AttendanceRequest body,
    HttpContext http,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
    Orkabi.Web.Modules.Scheduling.SchedulingService scheduling) =>
{
    // Antiforgery — the HTMX/JS slice sends the token in the RequestVerificationToken header.
    try { await antiforgery.ValidateRequestAsync(http); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
    {
        return Results.Json(new { error = "antiforgery" }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (body is null || body.marks is null || body.marks.Count == 0)
        return Results.Json(new { error = "marks חסרים" }, statusCode: StatusCodes.Status400BadRequest);
    if (string.IsNullOrWhiteSpace(body.idempotencyKey))
        return Results.Json(new { error = "idempotencyKey חסר" }, statusCode: StatusCodes.Status400BadRequest);

    var userId = int.Parse(http.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
    var isAdmin = http.User.IsInRole(AppRoles.Admin);

    // Server-side date-scope guard (mirrors the page). Admin bypasses.
    if (!isAdmin && !await scheduling.CanAccessShiftAsync(body.shiftInstanceId, userId))
        return Results.Json(new { error = "אין הרשאה למשמרת זו" }, statusCode: StatusCodes.Status403Forbidden);

    // Resolve the LessonLog (get-or-create with the class's current model).
    var (lessonLogId, _, _) = await scheduling.ResolveLessonLogForAttendanceAsync(body.shiftInstanceId);
    if (lessonLogId is null)
        return Results.Json(new { error = "טרם שובץ דגם לכיתה" }, statusCode: StatusCodes.Status409Conflict);

    var marks = new List<(int, Orkabi.Web.Modules.Scheduling.AttendanceStatus)>(body.marks.Count);
    foreach (var m in body.marks)
    {
        if (!Enum.TryParse<Orkabi.Web.Modules.Scheduling.AttendanceStatus>(m.status, ignoreCase: true, out var status))
            return Results.Json(new { error = $"status לא חוקי: {m.status}" }, statusCode: StatusCodes.Status400BadRequest);
        marks.Add((m.clientId, status));
    }

    // Was this batch key already used? Decide the friendly response BEFORE submitting — but still
    // call Submit (idempotent per-row) so a partially-saved first attempt is completed on retry.
    var alreadyUsed = await scheduling.WasIdempotencyKeyUsedAsync(body.idempotencyKey);
    var saved = await scheduling.SubmitAttendanceAsync(lessonLogId.Value, marks, body.idempotencyKey);

    return alreadyUsed
        ? Results.Json(new { saved = true, count = saved.Count, message = "הנוכחות כבר נשמרה" },
            statusCode: StatusCodes.Status409Conflict)
        : Results.Json(new { saved = true, count = saved.Count, message = "הנוכחות נשמרה" });
}).RequireAuthorization();

if (!app.Environment.IsEnvironment("Testing") && dbProvider != "Sqlite")
{
    var migrateCs = app.Configuration.GetConnectionString("Migrations")
                    ?? app.Configuration.GetConnectionString("Default");
    var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(migrateCs).Options;
    await using (var migrateDb = new AppDbContext(opts))
        await migrateDb.Database.MigrateAsync();

    using var scope = app.Services.CreateScope();
    await DataSeeder.SeedRolesAsync(scope.ServiceProvider);
    await DataSeeder.SeedAdminAsync(scope.ServiceProvider, app.Configuration);
    await DataSeeder.SeedAcademicYearAsync(scope.ServiceProvider);
}

app.Run();

// Request DTOs for POST /api/attendance.
public sealed record AttendanceMarkInput(int clientId, string status);
public sealed record AttendanceRequest(int shiftInstanceId, List<AttendanceMarkInput> marks, string idempotencyKey);

public partial class Program { }
