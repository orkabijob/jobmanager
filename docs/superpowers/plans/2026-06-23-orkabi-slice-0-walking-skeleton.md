# Orkabi — Slice 0: Walking Skeleton — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a deployed ASP.NET Core 8 app on Render + Neon with working email/password **and** Google login, 4 roles, role-routed dashboards, the archival global-query-filter base, the Hebrew-RTL Apple-glass base layout, and a `/health` endpoint — proving every integration seam before any domain code exists.

**Architecture:** A single ASP.NET Core 8 Razor Pages app (modular monolith, modules added in later slices). EF Core 9 + Npgsql against Neon Postgres, migrations applied on boot. ASP.NET Core Identity (int keys) for users/hashing + cookie auth + Google OAuth. Cross-cutting bases (audit interceptor, archival query filter, Israel-TZ constant, RTL design system) are established here so every later slice inherits them.

**Tech Stack:** .NET 8 (LTS), ASP.NET Core Razor Pages, EF Core 9, Npgsql (prod), `Microsoft.EntityFrameworkCore.Sqlite` (inner-loop tests), ASP.NET Core Identity, xUnit, `Microsoft.AspNetCore.Mvc.Testing`, Docker, Render (host), Neon (Postgres). **No Docker is required for the test inner loop** — Postgres fidelity is delegated to the real-Neon deploy gate (Task 11).

## Global Constraints

_Copied verbatim from the spec. Every task implicitly includes these._

- **Language:** 100% Hebrew, **RTL-native**. Culture fixed to `he-IL`; `<html dir="rtl" lang="he">`. No bilingual/culture-switching machinery.
- **CSS:** **logical-property-native** (`margin-inline-*`, `padding-inline-*`, `inset-inline-*`, `text-align: start/end`). Do **not** create a `[dir="rtl"]` physical-property override file.
- **Runtime:** .NET 8 (LTS). ASP.NET Core Razor Pages + thin REST API (`/api/*`).
- **Data:** EF Core 9 + Npgsql, **PostgreSQL (Neon free tier)**. Enums are **int-backed** (`.HasConversion<int>()`). Store instants as `timestamptz` UTC; convert to `Asia/Jerusalem` only at the presentation/scheduler edge. Pure dates use `DateOnly`.
- **Every entity** has base audit fields: `CreatedAt`, `CreatedByUserId`, `UpdatedAt`, `UpdatedByUserId` (set by a SaveChanges interceptor).
- **Roles (exactly 4, fixed):** `Admin`, `CustomerService`, `Logistics`, `Instructor`. Authorize via role policies; **no** grant/ABAC engine. Role string representation is standardized everywhere (the `enum.ToString()` value == the Identity role name == any future `Action_Item.AssignedToRole`).
- **Auth:** ASP.NET Core Identity + cookie (`HttpOnly`, `SameSite=Lax`, `Secure`) + Google OAuth. Cookie's `OnRedirectToLogin` returns **401 JSON for `/api/*`** instead of a 302. Anti-forgery enforced on cookie-authed API endpoints.
- **Brand:** primary color **Blue Jay `#2B547E`**. Fonts: self-hosted **Assistant** (UI) + **Heebo** (tabular numerals), both OFL, subset to Hebrew+Latin.
- **Migrations** applied at boot via `MigrateAsync()` (never `EnsureCreated`).

---

## File Structure

```
Orkabi.sln
src/Orkabi.Web/
├─ Program.cs                         # composition root: DI, auth, middleware, migrate-on-boot
├─ Orkabi.Web.csproj
├─ appsettings.json                   # config keys (no secrets)
├─ Dockerfile
├─ Data/
│  ├─ AppDbContext.cs                 # IdentityDbContext<AppUser,AppRole,int> + global filters
│  ├─ AppDbContextFactory.cs          # design-time factory for `dotnet ef`
│  ├─ AuditSaveChangesInterceptor.cs  # stamps audit fields
│  └─ DataSeeder.cs                   # seeds the 4 roles
├─ Shared/
│  ├─ BaseEntity.cs                   # Id + audit fields
│  ├─ IArchivable.cs + EntityStatus.cs# archival contract
│  ├─ ICurrentUser.cs + CurrentUser.cs# resolves logged-in user id
│  └─ IsraelClock.cs                  # Asia/Jerusalem TZ helpers
├─ Modules/Identity/
│  ├─ AppUser.cs, AppRole.cs, AppRoles.cs (constants)
│  └─ Pages/Account/ (Login, Logout, Register, AccessDenied)
├─ Pages/
│  ├─ Shared/_Layout.cshtml           # RTL Apple-glass shell
│  ├─ Index.cshtml                    # post-login role router
│  ├─ Dashboard/{Admin,Cs,Logistics,Instructor}.cshtml
│  └─ Health.cshtml? -> use minimal API endpoint instead
└─ wwwroot/
   ├─ css/tokens.css, base.css        # design tokens + base (logical props)
   └─ fonts/ (Assistant, Heebo woff2)
tests/Orkabi.Web.Tests/
├─ Orkabi.Web.Tests.csproj
├─ Infrastructure/SqliteFixture.cs    # file-per-fixture SQLite (no Docker)
├─ Infrastructure/OrkabiAppFactory.cs # WebApplicationFactory w/ test DB
├─ HealthTests.cs, AuthTests.cs, ArchivalFilterTests.cs, AuditInterceptorTests.cs, RtlLayoutTests.cs
```

---

### Task 1: Repository & solution scaffolding

**Files:**
- Create: `Orkabi.sln`, `src/Orkabi.Web/Orkabi.Web.csproj`, `tests/Orkabi.Web.Tests/Orkabi.Web.Tests.csproj`, `.gitignore`, `Directory.Build.props`, `global.json`

**Interfaces:**
- Consumes: nothing.
- Produces: a building solution with one web project + one xUnit test project referencing it.

- [ ] **Step 1: Initialize git and pin the SDK**

```bash
cd /c/Users/katzi/downloads/orkabis
git init
dotnet new globaljson --sdk-version 8.0.400 --roll-forward latestfeature
```

- [ ] **Step 2: Create solution and projects**

```bash
dotnet new sln -n Orkabi
dotnet new webapp -n Orkabi.Web -o src/Orkabi.Web --framework net8.0
dotnet new xunit -n Orkabi.Web.Tests -o tests/Orkabi.Web.Tests --framework net8.0
dotnet sln add src/Orkabi.Web/Orkabi.Web.csproj tests/Orkabi.Web.Tests/Orkabi.Web.Tests.csproj
dotnet add tests/Orkabi.Web.Tests/Orkabi.Web.Tests.csproj reference src/Orkabi.Web/Orkabi.Web.csproj
```

- [ ] **Step 3: Add `Directory.Build.props`** (repo root) — enforce nullable + warnings-as-errors

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Add `.gitignore`**

```bash
dotnet new gitignore
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold Orkabi solution (web + tests)"
```

---

### Task 2: Health endpoint + integration test harness

**Files:**
- Modify: `src/Orkabi.Web/Program.cs`
- Create: `tests/Orkabi.Web.Tests/Infrastructure/OrkabiAppFactory.cs`, `tests/Orkabi.Web.Tests/HealthTests.cs`
- Modify: `tests/Orkabi.Web.Tests/Orkabi.Web.Tests.csproj`

**Interfaces:**
- Consumes: the web app from Task 1.
- Produces: `OrkabiAppFactory : WebApplicationFactory<Program>` (test clients); a `GET /health` endpoint returning `200 {"status":"ok"}`. Requires `Program` to be partial-class accessible (add `public partial class Program {}` at the end of Program.cs).

- [ ] **Step 1: Add the test packages**

```bash
dotnet add tests/Orkabi.Web.Tests reference src/Orkabi.Web/Orkabi.Web.csproj
dotnet add tests/Orkabi.Web.Tests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: Write the failing test** — `HealthTests.cs`

```csharp
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
```

- [ ] **Step 3: Create `OrkabiAppFactory`** (minimal for now; DB wired in Task 3)

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace Orkabi.Web.Tests.Infrastructure;

public class OrkabiAppFactory : WebApplicationFactory<Program>
{
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test --filter Health_returns_200_ok`
Expected: FAIL — 404 Not Found (no `/health` endpoint yet).

- [ ] **Step 5: Add the endpoint + expose `Program`** — in `Program.cs`, before `app.Run();`

```csharp
app.MapGet("/health", () => Results.Json(new { status = "ok" }));
```

At the very end of `Program.cs`:

```csharp
public partial class Program { }
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test --filter Health_returns_200_ok`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add /health endpoint and integration test harness"
```

---

### Task 3: EF Core + AppDbContext + dual-provider (Npgsql prod / SQLite tests) + migrate-on-boot

**Files:**
- Create: `src/Orkabi.Web/Data/AppDbContext.cs`, `src/Orkabi.Web/Data/AppDbContextFactory.cs`
- Modify: `src/Orkabi.Web/Program.cs`, `src/Orkabi.Web/appsettings.json`, `src/Orkabi.Web/Orkabi.Web.csproj`
- Create: `tests/Orkabi.Web.Tests/Infrastructure/SqliteFixture.cs`; modify `OrkabiAppFactory.cs`; create `tests/Orkabi.Web.Tests/DbConnectivityTests.cs`

**Interfaces:**
- Consumes: web app + factory.
- Produces: `AppDbContext` (initially plain `DbContext`; becomes `IdentityDbContext` in Task 6), a runtime provider chosen by config key `Database:Provider` (`Sqlite` → `UseSqlite`, else `UseNpgsql`), the Npgsql migration set applied on boot (skipped under `Testing`), and a `SqliteFixture` that gives each test class a fresh file-backed SQLite DB. `OrkabiAppFactory` accepts a connection string, forces the SQLite provider + `Testing` env, and exposes `Prepared()` which builds the schema with **`EnsureCreated()`** (no migration files in the inner loop).

> **No Docker required.** The inner loop runs on SQLite (a single managed dependency). The Npgsql migration files run ONLY at the real-Neon deploy (Task 11), which is the sole Postgres-fidelity gate — the SQLite inner loop deliberately does not exercise PG-specific SQL, the Neon pooler, or `timestamptz`/`DateOnly` semantics.

- [ ] **Step 1: Add packages**

```bash
dotnet add src/Orkabi.Web package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Orkabi.Web package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/Orkabi.Web package Microsoft.EntityFrameworkCore.Design
# tests reference Microsoft.Data.Sqlite transitively via the Web project's Sqlite provider;
# add it explicitly to the test project only if the fixture needs SqliteConnection directly:
dotnet add tests/Orkabi.Web.Tests package Microsoft.Data.Sqlite
```

- [ ] **Step 2: Create `AppDbContext`** (Identity base added in Task 6)

```csharp
using Microsoft.EntityFrameworkCore;

namespace Orkabi.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
```

- [ ] **Step 3: Create design-time factory** (so `dotnet ef` works without running the app)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Orkabi.Web.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Design-time uses the DIRECT (non-pooled) Migrations endpoint. `migrations add`
        // needs no live DB; `database update` would hit this — never point it at the pooler.
        var cs = Environment.GetEnvironmentVariable("ORKABI_MIGRATIONS_CONNSTRING")
                 ?? "Host=localhost;Database=orkabi_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs).Options;
        return new AppDbContext(options);
    }
}
```

- [ ] **Step 4: Register DbContext (pooled runtime) + migrate-on-boot (DIRECT endpoint)** in `Program.cs`.

**Two connection strings** (the A3 blocker fix): `Default` = Neon **pooled** endpoint for runtime queries, which is PgBouncer in transaction mode and therefore **requires `Max Auto Prepare=0`**; `Migrations` = Neon **direct** (non-pooled) endpoint used ONLY for boot migration and `dotnet ef`.

Register the runtime context (after `builder` is created):

```csharp
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
```

Migrate on boot from a SEPARATE context built on the direct `Migrations` string, and SKIP it under the `Testing` environment (the test harness owns migration — Step 7):

```csharp
if (!app.Environment.IsEnvironment("Testing"))
{
    var migrateCs = app.Configuration.GetConnectionString("Migrations")
                    ?? app.Configuration.GetConnectionString("Default");
    var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(migrateCs).Options;
    await using var migrateDb = new AppDbContext(opts);
    await migrateDb.Database.MigrateAsync();
}
```

Add to `appsettings.json` (values come from Render env vars — never committed):

```json
"ConnectionStrings": { "Default": "", "Migrations": "" }
```

Real values (set as `ConnectionStrings__Default` / `ConnectionStrings__Migrations` env vars):
- `Default` (pooled): `Host=<proj>-pooler.<region>.aws.neon.tech;Database=orkabi;Username=...;Password=...;SSL Mode=Require;Pooling=true;Maximum Pool Size=20;Max Auto Prepare=0`
- `Migrations` (direct): `Host=<proj>.<region>.aws.neon.tech;Database=orkabi;Username=...;Password=...;SSL Mode=Require`

- [ ] **Step 5: Create the initial migration**

```bash
dotnet ef migrations add InitialCreate --project src/Orkabi.Web --startup-project src/Orkabi.Web
```

Expected: a `Migrations/` folder with `InitialCreate` (empty model for now).

- [ ] **Step 6: Create `PostgresFixture`** (Testcontainers, shared per test collection)

```csharp
using Testcontainers.PostgreSql;

namespace Orkabi.Web.Tests.Infrastructure;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```

- [ ] **Step 7: Make `OrkabiAppFactory` use the test container**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkabi.Web.Data;

namespace Orkabi.Web.Tests.Infrastructure;

public class OrkabiAppFactory : WebApplicationFactory<Program>
{
    public string ConnectionString { get; set; } = "";
    private readonly Dictionary<string, string?> _config = new();

    public OrkabiAppFactory WithConfig(string key, string? value)
    { _config[key] = value; return this; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Testing env => Program skips its boot-migrate; the factory owns migration (A1 fix).
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(_config));
        builder.ConfigureServices(services =>
        {
            var d = services.Single(s => s.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(d);
            // NOTE (Task 4): once the audit interceptor exists, this swap re-adds it.
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(ConnectionString));
        });
    }

    /// <summary>Apply migrations to the test container. Idempotent; call once at the start of a DB test.</summary>
    public void Migrate()
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }
}
```

- [ ] **Step 8: Write the connectivity test** — `DbConnectivityTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class DbConnectivityTests
{
    private readonly PostgresFixture _pg;
    public DbConnectivityTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task App_starts_and_applies_migrations_against_real_postgres()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        factory.Migrate();   // the factory owns migration under the Testing environment
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Database.CanConnectAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());  // all applied
    }
}
```

- [ ] **Step 9: Run the test**

Run: `dotnet test --filter App_starts_and_applies_migrations_against_real_postgres`
Expected: PASS (container boots, app applies the migration, connects).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: wire EF Core + Neon, migrate-on-boot, Testcontainers harness"
```

---

### Task 4: BaseEntity + audit SaveChanges interceptor

**Files:**
- Create: `src/Orkabi.Web/Shared/BaseEntity.cs`, `src/Orkabi.Web/Shared/ICurrentUser.cs`, `src/Orkabi.Web/Shared/CurrentUser.cs`, `src/Orkabi.Web/Data/AuditSaveChangesInterceptor.cs`
- Modify: `Program.cs`, `AppDbContext.cs`
- Create: `tests/Orkabi.Web.Tests/AuditInterceptorTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`.
- Produces: `BaseEntity { int Id; DateTime CreatedAt; int? CreatedByUserId; DateTime UpdatedAt; int? UpdatedByUserId; }`; `ICurrentUser { int? UserId { get; } }`; `AuditSaveChangesInterceptor` registered on the context. A throwaway `Probe : BaseEntity` test entity verifies stamping.

- [ ] **Step 1: Create `BaseEntity`**

```csharp
namespace Orkabi.Web.Shared;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
}
```

- [ ] **Step 2: Create `ICurrentUser` + HTTP-context implementation**

```csharp
// ICurrentUser.cs
namespace Orkabi.Web.Shared;
public interface ICurrentUser { int? UserId { get; } }

// CurrentUser.cs
using System.Security.Claims;
namespace Orkabi.Web.Shared;
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;
    public int? UserId
    {
        get
        {
            var v = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(v, out var id) ? id : null;
        }
    }
}
```

- [ ] **Step 3: Create the interceptor**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Data;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _user;
    public AuditSaveChangesInterceptor(ICurrentUser user) => _user = user;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        var ctx = eventData.Context;
        if (ctx is not null) Stamp(ctx);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void Stamp(DbContext ctx)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ctx.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedByUserId = _user.UserId;
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedByUserId = _user.UserId;
            }
        }
    }
}
```

- [ ] **Step 4: Register interceptor + accessor** in `Program.cs`

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
```

Change the DbContext registration to add the interceptor:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
     .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
```

Also update `OrkabiAppFactory`'s DbContext swap (from Task 3) to include the interceptor — otherwise audit stamping won't run through the test host:

```csharp
services.AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(ConnectionString)
    .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
```

- [ ] **Step 5: Add a test-only `Probe` entity** to `AppDbContext` guarded for tests — add to `AppDbContext`:

```csharp
public DbSet<Probe> Probes => Set<Probe>();
// ...
public class Probe : Orkabi.Web.Shared.BaseEntity { public string Name { get; set; } = ""; }
```

Create migration:

```bash
dotnet ef migrations add AddProbe --project src/Orkabi.Web --startup-project src/Orkabi.Web
```

- [ ] **Step 6: Write the failing test** — `AuditInterceptorTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class AuditInterceptorTests
{
    private readonly PostgresFixture _pg;
    public AuditInterceptorTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Adding_entity_stamps_created_and_updated_timestamps()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        factory.Migrate();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // unique name keeps this row distinct from other tests sharing the container (A2)
        var probe = new Probe { Name = $"audit-{Guid.NewGuid():N}" };
        db.Probes.Add(probe);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, probe.CreatedAt);
        Assert.Equal(probe.CreatedAt, probe.UpdatedAt);
    }
}
```

- [ ] **Step 7: Run — verify pass**

Run: `dotnet test --filter Adding_entity_stamps_created_and_updated_timestamps`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: BaseEntity + audit SaveChanges interceptor"
```

---

### Task 5: Archival — IArchivable + EntityStatus + global query filter

**Files:**
- Create: `src/Orkabi.Web/Shared/EntityStatus.cs`, `src/Orkabi.Web/Shared/IArchivable.cs`
- Modify: `AppDbContext.cs` (apply global filter), make `Probe` implement `IArchivable`
- Create: `tests/Orkabi.Web.Tests/ArchivalFilterTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `Probe`.
- Produces: `enum EntityStatus { Active = 0, Archived = 1 }`; `interface IArchivable { EntityStatus Status { get; } }`; a global query filter `WHERE Status = Active` applied **per IArchivable entity** in `OnModelCreating`, with `IgnoreQueryFilters()` as the documented escape hatch. This establishes the pattern every later aggregate root opts into.

- [ ] **Step 1: Create the contract**

```csharp
// EntityStatus.cs
namespace Orkabi.Web.Shared;
public enum EntityStatus { Active = 0, Archived = 1 }

// IArchivable.cs
namespace Orkabi.Web.Shared;

/// INVARIANT: `Archived` is set ONLY by the academic-year batch job (Slice 4+).
/// An entity's own `IsActive = false` (e.g. a dropped-out student) means inactive
/// WITHIN the current year and must NOT be Archived — it stays visible in current-year
/// views. Status (Active/Archived) and IsActive are orthogonal; never conflate them.
public interface IArchivable { EntityStatus Status { get; } }
```

- [ ] **Step 2: Apply the filter in `AppDbContext.OnModelCreating`** — make `Probe : BaseEntity, IArchivable` with `public EntityStatus Status { get; set; }`, then:

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    base.OnModelCreating(b);

    // ARCHIVAL: applied ONLY to aggregate roots that implement IArchivable.
    // Use IgnoreQueryFilters() for cross-year admin reports. Do NOT apply
    // to referenced lookup entities (would cause silent null navigations).
    b.Entity<Probe>().HasQueryFilter(p => p.Status == EntityStatus.Active);

    // Int-backed enum convention example (later entities follow this):
    b.Entity<Probe>().Property(p => p.Status).HasConversion<int>();
}
```

Create migration:

```bash
dotnet ef migrations add AddProbeStatus --project src/Orkabi.Web --startup-project src/Orkabi.Web
```

- [ ] **Step 3: Write the failing test** — `ArchivalFilterTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class ArchivalFilterTests
{
    private readonly PostgresFixture _pg;
    public ArchivalFilterTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Archived_rows_are_hidden_by_default_and_visible_with_IgnoreQueryFilters()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        factory.Migrate();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tag = $"arch-{Guid.NewGuid():N}";   // isolate this test's rows in the shared container (A2)
        db.Probes.Add(new Probe { Name = tag, Status = EntityStatus.Active });
        db.Probes.Add(new Probe { Name = tag, Status = EntityStatus.Archived });
        await db.SaveChangesAsync();

        // relative counts scoped to this test's tag — never table totals
        Assert.Equal(1, await db.Probes.Where(p => p.Name == tag).CountAsync());                      // filtered
        Assert.Equal(2, await db.Probes.IgnoreQueryFilters().Where(p => p.Name == tag).CountAsync()); // escape hatch
    }
}
```

- [ ] **Step 4: Run — verify pass**

Run: `dotnet test --filter Archived_rows_are_hidden_by_default_and_visible_with_IgnoreQueryFilters`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: archival contract + global query filter pattern"
```

---

### Task 6: ASP.NET Core Identity (int keys) + 4 roles + cookie config

**Files:**
- Create: `src/Orkabi.Web/Modules/Identity/AppUser.cs`, `AppRole.cs`, `AppRoles.cs`
- Modify: `AppDbContext.cs` (extend `IdentityDbContext<AppUser, AppRole, int>`), `Program.cs`
- Create: `src/Orkabi.Web/Data/DataSeeder.cs`
- Create: `tests/Orkabi.Web.Tests/AuthTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`.
- Produces: `AppUser : IdentityUser<int>`, `AppRole : IdentityRole<int>`, `static class AppRoles { const string Admin="Admin", CustomerService="CustomerService", Logistics="Logistics", Instructor="Instructor"; string[] All; }`; Identity registered with cookie auth; the 4 roles seeded on boot; cookie returns **401 for `/api/*`**.

- [ ] **Step 1: Add Identity packages**

```bash
dotnet add src/Orkabi.Web package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/Orkabi.Web package Microsoft.AspNetCore.Identity.UI
```

- [ ] **Step 2: Create the role constants + entities**

```csharp
// AppRoles.cs
namespace Orkabi.Web.Modules.Identity;
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string CustomerService = "CustomerService";
    public const string Logistics = "Logistics";
    public const string Instructor = "Instructor";
    public static readonly string[] All = { Admin, CustomerService, Logistics, Instructor };
}

// AppUser.cs
using Microsoft.AspNetCore.Identity;
namespace Orkabi.Web.Modules.Identity;
public class AppUser : IdentityUser<int> { public string? FullName { get; set; } }

// AppRole.cs
using Microsoft.AspNetCore.Identity;
namespace Orkabi.Web.Modules.Identity;
public class AppRole : IdentityRole<int> { public AppRole() {} public AppRole(string name) : base(name) {} }
```

- [ ] **Step 3: Make `AppDbContext` an Identity context**

```csharp
public class AppDbContext : IdentityDbContext<AppUser, AppRole, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    // ... existing DbSets + OnModelCreating (keep base.OnModelCreating call FIRST)
}
```

Create migration:

```bash
dotnet ef migrations add AddIdentity --project src/Orkabi.Web --startup-project src/Orkabi.Web
```

- [ ] **Step 4: Register Identity + cookie + 401-for-API** in `Program.cs`

```csharp
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
    // Always in Production; SameAsRequest in Dev/Testing so HTTP test/dev login round-trips (B2 fix)
    o.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
        ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask;
    };
});
```

Add `app.UseAuthentication(); app.UseAuthorization();` (in that order, after `UseRouting`).

- [ ] **Step 5: Seed the roles** — `DataSeeder.cs`

```csharp
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Data;

public static class DataSeeder
{
    public static async Task SeedRolesAsync(IServiceProvider sp)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<AppRole>>();
        foreach (var role in AppRoles.All)
            if (!await roleMgr.RoleExistsAsync(role))
                await roleMgr.CreateAsync(new AppRole(role));
    }
}
```

Wire seeding into the boot block. Final boot block in `Program.cs` (migrate via the direct endpoint, then seed via a DI scope; whole block skipped under `Testing`):

```csharp
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
```

Because boot-seeding is skipped under `Testing`, **update `OrkabiAppFactory.Migrate()` (from Task 3) to also seed roles** so role-dependent tests work:

```csharp
public void Migrate()
{
    using var scope = Services.CreateScope();
    var sp = scope.ServiceProvider;
    sp.GetRequiredService<AppDbContext>().Database.Migrate();
    DataSeeder.SeedRolesAsync(sp).GetAwaiter().GetResult();
}
```

- [ ] **Step 6: Write the failing test** — `AuthTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class AuthTests
{
    private readonly PostgresFixture _pg;
    public AuthTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Four_roles_are_seeded_and_user_can_be_created_with_a_role()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        factory.Migrate();   // migrates AND seeds the 4 roles
        using var scope = factory.Services.CreateScope();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        foreach (var r in AppRoles.All)
            Assert.True(await roleMgr.RoleExistsAsync(r));

        var user = new AppUser { UserName = "romi@orkabi.test", Email = "romi@orkabi.test" };
        var created = await userMgr.CreateAsync(user, "Passw0rd!");
        Assert.True(created.Succeeded);
        Assert.True((await userMgr.AddToRoleAsync(user, AppRoles.Admin)).Succeeded);
        Assert.True(await userMgr.IsInRoleAsync(user, AppRoles.Admin));
    }
}
```

> `factory.Migrate()` both migrates and seeds the 4 roles (boot-seeding is skipped under `Testing` — see Step 5).

- [ ] **Step 7: Run — verify pass**

Run: `dotnet test --filter Four_roles_are_seeded_and_user_can_be_created_with_a_role`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: ASP.NET Identity (int keys), 4 roles, cookie + 401-for-api"
```

---

### Task 6A: Prove the `/api/*` 401-JSON seam

**Files:**
- Modify: `src/Orkabi.Web/Program.cs` (add `GET /api/ping` requiring auth)
- Create: `tests/Orkabi.Web.Tests/ApiSeamTests.cs`

**Interfaces:**
- Consumes: cookie auth + the `OnRedirectToLogin` 401-for-`/api` event from Task 6.
- Produces: an authed `GET /api/ping` → `200 {"pong":true}`; anonymous → **401** (not a 302 redirect). Proves the thin-REST seam the whole architecture depends on (configured in Task 6, exercised here).

- [ ] **Step 1: Write the failing test** — `ApiSeamTests.cs`

```csharp
using System.Net;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class ApiSeamTests
{
    private readonly PostgresFixture _pg;
    public ApiSeamTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Anonymous_api_call_returns_401_not_a_login_redirect()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        factory.Migrate();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/api/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode); // 401, NOT 302 to /Account/Login
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter Anonymous_api_call_returns_401_not_a_login_redirect`
Expected: FAIL — 404 (no endpoint yet).

- [ ] **Step 3: Add the endpoint** in `Program.cs` (before `app.Run();`)

```csharp
app.MapGet("/api/ping", () => Results.Json(new { pong = true })).RequireAuthorization();
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter Anonymous_api_call_returns_401_not_a_login_redirect`
Expected: PASS — 401, not a 302.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test: prove /api/* returns 401 (not a redirect) for anonymous"
```

---

### Task 7: Login / Register / Logout pages (email + password)

**Files:**
- Create: `src/Orkabi.Web/Modules/Identity/Pages/Account/Login.cshtml(.cs)`, `Register.cshtml(.cs)`, `Logout.cshtml.cs`, `AccessDenied.cshtml`
- Create: `tests/Orkabi.Web.Tests/LoginFlowTests.cs`

**Interfaces:**
- Consumes: `SignInManager<AppUser>`, `UserManager<AppUser>`.
- Produces: working `/Account/Login` (POST sets the auth cookie), `/Account/Register`, `/Account/Logout`. After login, redirect to `/` (the role router from Task 9).

- [ ] **Step 1: Create the Login page model** (`Login.cshtml.cs`)

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Modules.Identity.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    public LoginModel(SignInManager<AppUser> signIn) => _signIn = signIn;

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await _signIn.PasswordSignInAsync(Email, Password, true, false);
        if (result.Succeeded) return LocalRedirect("/");
        Error = "אימייל או סיסמה שגויים";   // "wrong email or password"
        return Page();
    }
}
```

- [ ] **Step 2: Create `Login.cshtml`** (Hebrew, uses the base layout from Task 10)

```html
@page
@model Orkabi.Web.Modules.Identity.Pages.Account.LoginModel
@{ ViewData["Title"] = "כניסה"; }
<form method="post" class="glass-panel auth-card">
  <h1>כניסה לאורקבי</h1>
  @if (Model.Error is not null) { <p class="error">@Model.Error</p> }
  <label>אימייל<input type="email" asp-for="Email" dir="ltr" required /></label>
  <label>סיסמה<input type="password" asp-for="Password" required /></label>
  <button type="submit" class="btn-primary">כניסה</button>
  <a class="btn-google" href="/Account/ExternalLogin?provider=Google">המשך עם Google</a>
</form>
```

- [ ] **Step 3: Create Register + Logout** (`Register.cshtml.cs` creates an `AppUser`, assigns a default role chosen by an admin later; `Logout.cshtml.cs` calls `_signIn.SignOutAsync()` then redirects to `/Account/Login`). Create `AccessDenied.cshtml` with a Hebrew "אין לך הרשאה" message.

```csharp
// Logout.cshtml.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
namespace Orkabi.Web.Modules.Identity.Pages.Account;
public class LogoutModel : PageModel
{
    private readonly SignInManager<AppUser> _s;
    public LogoutModel(SignInManager<AppUser> s) => _s = s;
    public async Task<IActionResult> OnPostAsync() { await _s.SignOutAsync(); return RedirectToPage("/Account/Login"); }
}
```

- [ ] **Step 4: Write the failing test** — `LoginFlowTests.cs` (drives the real HTTP login, asserts the auth cookie is set)

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class LoginFlowTests
{
    private readonly PostgresFixture _pg;
    public LoginFlowTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Valid_login_sets_auth_cookie()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        factory.Migrate();
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = "a@b.test", Email = "a@b.test" };
            await um.CreateAsync(u, "Passw0rd!");
            await um.AddToRoleAsync(u, AppRoles.Admin);
        }
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // fetch anti-forgery token from the login GET, then POST
        var getResp = await client.GetAsync("/Account/Login");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());
        var form = new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"] = "a@b.test", ["Password"] = "Passw0rd!",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync("/Account/Login", form);

        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains(postResp.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("orkabi.auth"));
    }
}
```

Add a tiny `AntiForgery.Extract(html)` helper in `Infrastructure/AntiForgery.cs` that regex-pulls the `__RequestVerificationToken` hidden input value.

- [ ] **Step 5: Run — verify pass**

Run: `dotnet test --filter Valid_login_sets_auth_cookie`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: email/password login, register, logout pages (Hebrew)"
```

---

### Task 8: Google OAuth login

**Files:**
- Modify: `Program.cs` (add `.AddGoogle`), create `ExternalLogin.cshtml.cs`
- Modify: `appsettings.json` (config keys, no secrets)
- Create: `tests/Orkabi.Web.Tests/GoogleSchemeTests.cs`

**Interfaces:**
- Consumes: Identity + cookie.
- Produces: a registered `Google` auth scheme; `/Account/ExternalLogin?provider=Google` challenges Google; the callback creates/links an `AppUser`. Client id/secret come from env (`Authentication__Google__ClientId/ClientSecret`).

- [ ] **Step 1: Add the package**

```bash
dotnet add src/Orkabi.Web package Microsoft.AspNetCore.Authentication.Google
```

- [ ] **Step 2: Register Google** in `Program.cs` (after Identity):

```csharp
var googleId = builder.Configuration["Authentication:Google:ClientId"];
if (!string.IsNullOrWhiteSpace(googleId))
{
    // AddAuthentication() with NO args here must NOT change Identity's default schemes —
    // the app cookie stays the default authenticate/challenge scheme; Google is opt-in via
    // the /signin-google callback only. (Verified by the test in Step 4.)
    builder.Services.AddAuthentication().AddGoogle(o =>
    {
        o.ClientId = googleId!;
        o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        // CallbackPath defaults to /signin-google — this is the URI registered in Google Cloud
        // (NOT the /Account/ExternalLogin page path). Do not override it.
    });
}
```

- [ ] **Step 3: Create `ExternalLogin.cshtml.cs`** (challenge + callback that signs in / provisions the user)

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Modules.Identity.Pages.Account;

public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _users;
    public ExternalLoginModel(SignInManager<AppUser> s, UserManager<AppUser> u) { _signIn = s; _users = u; }

    public IActionResult OnGet(string provider)
    {
        var props = _signIn.ConfigureExternalAuthenticationProperties(
            provider, "/Account/ExternalLogin?handler=Callback");
        return Challenge(props, provider);
    }

    public async Task<IActionResult> OnGetCallbackAsync()
    {
        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info is null) return RedirectToPage("/Account/Login");

        var signin = await _signIn.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, true);
        if (signin.Succeeded) return LocalRedirect("/");

        var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value;
        var user = await _users.FindByEmailAsync(email)
                   ?? new AppUser { UserName = email, Email = email };
        if (user.Id == 0) await _users.CreateAsync(user);
        await _users.AddLoginAsync(user, info);
        await _signIn.SignInAsync(user, true);
        // NOTE: a freshly-provisioned Google user has NO role yet → the Index router (Task 9)
        // sends them to AccessDenied ("ממתין לשיוך תפקיד" / awaiting role assignment) until an
        // Admin assigns one. This is intended for an internal tool — document so QA doesn't file it as a bug.
        return LocalRedirect("/");
    }
}
```

- [ ] **Step 4: Write the test** — `GoogleSchemeTests.cs` (verifies the scheme registers when configured)

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class GoogleSchemeTests
{
    [Fact]
    public async Task Google_scheme_is_registered_when_client_id_is_configured()
    {
        var factory = new OrkabiAppFactory { ConnectionString = "Host=invalid" }
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
```

> `WithConfig(key, value)` is already defined on `OrkabiAppFactory` (added in Task 3 Step 7).

- [ ] **Step 5: Run — verify pass**

Run: `dotnet test --filter Google_scheme_is_registered_when_client_id_is_configured`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: Google OAuth login (challenge + provisioning callback)"
```

---

### Task 9: Role-routed dashboards

**Files:**
- Create: `src/Orkabi.Web/Pages/Index.cshtml(.cs)` (router), `Pages/Dashboard/Admin.cshtml(.cs)`, `Cs.cshtml`, `Logistics.cshtml`, `Instructor.cshtml`
- Create: `tests/Orkabi.Web.Tests/RoleRoutingTests.cs`

**Interfaces:**
- Consumes: Identity roles, login flow.
- Produces: `/` redirects a logged-in user to `/Dashboard/{role}`; each dashboard page is `[Authorize(Roles = ...)]`; anonymous `/` → login.

- [ ] **Step 1: Create the router** (`Index.cshtml.cs`)

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages;

[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.IsInRole(AppRoles.Admin)) return RedirectToPage("/Dashboard/Admin");
        if (User.IsInRole(AppRoles.CustomerService)) return RedirectToPage("/Dashboard/Cs");
        if (User.IsInRole(AppRoles.Logistics)) return RedirectToPage("/Dashboard/Logistics");
        if (User.IsInRole(AppRoles.Instructor)) return RedirectToPage("/Dashboard/Instructor");
        return RedirectToPage("/Account/AccessDenied");
    }
}
```

- [ ] **Step 2: Create the 4 dashboard pages** — each a page-model with `[Authorize(Roles = AppRoles.X)]` and a Hebrew stub view (`<h1>לוח בקרה — מנהל</h1>` etc.). Example `Admin.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
namespace Orkabi.Web.Pages.Dashboard;
[Authorize(Roles = AppRoles.Admin)]
public class AdminModel : PageModel { public void OnGet() { } }
```

- [ ] **Step 3: Write the failing test** — `RoleRoutingTests.cs` (log in as Instructor, assert `/Dashboard/Admin` is forbidden and `/` routes to the instructor dashboard)

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class RoleRoutingTests
{
    private readonly PostgresFixture _pg;
    public RoleRoutingTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Instructor_is_routed_to_their_dashboard_and_denied_admin()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        factory.Migrate();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = "t@b.test", Email = "t@b.test" };
            await um.CreateAsync(u, "Passw0rd!");
            await um.AddToRoleAsync(u, AppRoles.Instructor);
        }
        var client = await TestLogin.SignInAsync(factory, "t@b.test", "Passw0rd!");

        var root = await client.GetAsync("/");
        Assert.Equal("/Dashboard/Instructor", root.Headers.Location?.ToString());

        var admin = await client.GetAsync("/Dashboard/Admin");
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode); // → AccessDenied
        Assert.Contains("AccessDenied", admin.Headers.Location?.ToString());
    }
}
```

Add `Infrastructure/TestLogin.cs` (`SignInAsync` reuses the anti-forgery+POST flow from Task 7 and returns an authenticated `HttpClient`).

- [ ] **Step 4: Run — verify pass**

Run: `dotnet test --filter Instructor_is_routed_to_their_dashboard_and_denied_admin`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: role-routed dashboards with per-role authorization"
```

---

### Task 10: Hebrew-RTL Apple-glass base layout + design tokens + fonts

**Files:**
- Create: `src/Orkabi.Web/Pages/Shared/_Layout.cshtml`, `wwwroot/css/tokens.css`, `wwwroot/css/base.css`, `wwwroot/fonts/*.woff2`
- Modify: `Program.cs` (fixed `he-IL` culture), `_ViewStart.cshtml`
- Create: `tests/Orkabi.Web.Tests/RtlLayoutTests.cs`

**Interfaces:**
- Consumes: nothing app-specific.
- Produces: every rendered page is `<html dir="rtl" lang="he">` with the Apple-glass token CSS + Assistant/Heebo fonts loaded; logical-property-native base CSS (no `[dir=rtl]` override file).

- [ ] **Step 1: Fix the culture to he-IL** in `Program.cs`

```csharp
var he = new System.Globalization.CultureInfo("he-IL");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = he;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = he;
var locOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("he-IL"),
    SupportedCultures = new[] { he }, SupportedUICultures = new[] { he }
};
app.UseRequestLocalization(locOptions);
```

- [ ] **Step 2: Create `tokens.css`** — the three-tier glass system (Blue Jay, logical-property-friendly)

```css
:root{
  --brand:#2B547E; --brand-rgb:43,84,126; --brand-tint:#3E6FA0;
  --glass-blur-nav:saturate(180%) blur(24px);
  --glass-blur-panel:saturate(160%) blur(16px);
  --glass-fill:rgba(255,255,255,.55);
  --glass-fill-strong:rgba(255,255,255,.72);
  --glass-hairline:1px solid rgba(255,255,255,.55);
  --shadow-glass:0 1px 1px rgba(43,84,126,.04),0 8px 24px -8px rgba(43,84,126,.18),inset 0 1px 0 rgba(255,255,255,.5);
  --radius-card:20px; --radius-panel:28px; --radius-hero:32px;
  --font-ui:'Assistant',system-ui,sans-serif; --font-num:'Heebo',monospace;
}
body{background-color:#EEF3F8;}
body::before{content:"";position:fixed;inset:0;z-index:-1;pointer-events:none;
  background:
    radial-gradient(40% 50% at 18% 22%,rgba(62,111,160,.28),transparent 70%),
    radial-gradient(45% 55% at 85% 18%,rgba(120,160,210,.22),transparent 70%),
    radial-gradient(50% 60% at 75% 88%,rgba(43,84,126,.20),transparent 75%);}
```

- [ ] **Step 3: Create `base.css`** — logical-property base (panels, buttons, auth card). All spacing uses `*-inline-*`/`*-block-*`.

```css
@font-face{font-family:'Assistant';src:url('/fonts/assistant-400.woff2') format('woff2');font-weight:400;font-display:swap;}
@font-face{font-family:'Assistant';src:url('/fonts/assistant-600.woff2') format('woff2');font-weight:600;font-display:swap;}
@font-face{font-family:'Heebo';src:url('/fonts/heebo-500.woff2') format('woff2');font-weight:500;font-display:swap;}
*{box-sizing:border-box;}
body{font-family:var(--font-ui);margin:0;color:#16263a;}
.glass-panel{background:var(--glass-fill-strong);backdrop-filter:var(--glass-blur-panel);
  -webkit-backdrop-filter:var(--glass-blur-panel);border:var(--glass-hairline);
  border-radius:var(--radius-panel);box-shadow:var(--shadow-glass);padding-block:24px;padding-inline:24px;}
.btn-primary{background:var(--brand);color:#fff;border:0;border-radius:9999px;
  padding-block:12px;padding-inline:24px;font-weight:600;}
.auth-card{max-inline-size:380px;margin-inline:auto;margin-block-start:12vh;display:flex;flex-direction:column;gap:16px;}
@supports not (backdrop-filter:blur(1px)){.glass-panel{background:rgba(255,255,255,.92);}}
```

- [ ] **Step 4: Create `_Layout.cshtml`**

```html
<!DOCTYPE html>
<html dir="rtl" lang="he">
<head>
  <meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>@ViewData["Title"] · אורקבי</title>
  <link rel="stylesheet" href="~/css/tokens.css" asp-append-version="true" />
  <link rel="stylesheet" href="~/css/base.css" asp-append-version="true" />
</head>
<body>
  <main>@RenderBody()</main>
</body>
</html>
```

Set `_ViewStart.cshtml` to `@{ Layout = "_Layout"; }`.

- [ ] **Step 4a: Add real Hebrew-subset fonts (HARD requirement — placeholders render tofu boxes)**

Download **Assistant** (400, 600) and **Heebo** (500) from Google Fonts (OFL). Subset each to Hebrew + Latin + punctuation and convert to woff2:

```bash
pip install fonttools brotli
# repeat for assistant-600 and heebo-500
pyftsubset Assistant-Regular.ttf \
  --unicodes=U+0020-007E,U+05D0-05EA,U+05B0-05C7,U+200E,U+200F \
  --flavor=woff2 --output-file=src/Orkabi.Web/wwwroot/fonts/assistant-400.woff2
```

Commit the three real `.woff2` files. Do NOT ship empty/placeholder fonts — Hebrew renders as boxes and the build still passes, hiding the defect.

- [ ] **Step 4b: Create the `IsraelClock` stub** — `src/Orkabi.Web/Shared/IsraelClock.cs`

```csharp
namespace Orkabi.Web.Shared;

/// Single source of truth for Israel time. Store all instants as UTC; convert to IsraelTz
/// ONLY at the presentation edge and in the (Slice 4) job scheduler — DST-correct.
/// .NET 8 resolves the IANA id "Asia/Jerusalem" cross-platform (Windows + Linux).
public static class IsraelClock
{
    public static readonly TimeZoneInfo IsraelTz =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
}
```

(Established now so Slice 2's first time-of-day logic has its home; no consumer in Slice 0.)

- [ ] **Step 5: Write the failing test** — `RtlLayoutTests.cs`

```csharp
using Orkabi.Web.Tests.Infrastructure;
namespace Orkabi.Web.Tests;

[Collection("postgres")]
public class RtlLayoutTests
{
    private readonly PostgresFixture _pg;
    public RtlLayoutTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Login_page_renders_rtl_hebrew_shell()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _pg.ConnectionString };
        var html = await factory.CreateClient().GetStringAsync("/Account/Login");
        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("lang=\"he\"", html);
        Assert.Contains("tokens.css", html);
    }
}
```

- [ ] **Step 6: Run — verify pass**

Run: `dotnet test --filter Login_page_renders_rtl_hebrew_shell`
Expected: PASS.

- [ ] **Step 7: Run the FULL test suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: Hebrew-RTL Apple-glass base layout, tokens, fonts"
```

---

### Task 11: Containerize + deploy to Render + Neon (end-to-end verification)

**Files:**
- Create: `src/Orkabi.Web/Dockerfile`, `.dockerignore`, `render.yaml`
- Create: `docs/DEPLOY.md`

**Interfaces:**
- Consumes: the whole app.
- Produces: a running public URL where `/health` returns 200, `/Account/Login` renders RTL, and a seeded admin can log in. Secrets (`ConnectionStrings__Default`, `Authentication__Google__ClientId/Secret`) set as Render env vars.

- [ ] **Step 1: Create `Dockerfile`** (multi-stage, .NET 8)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Orkabi.Web/Orkabi.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orkabi.Web.dll"]
```

- [ ] **Step 1a: Create `.dockerignore`** (repo root) — small image, no leaked dev secrets

```
**/bin
**/obj
**/.vs
.git
tests
docs
**/appsettings.Development.json
**/*.user
```

- [ ] **Step 2: Create the Neon database + capture BOTH endpoints**

Create a free Neon project named `orkabi`. From the dashboard copy:
- the **pooled** string (`...-pooler.<region>...`) → `ConnectionStrings__Default`, append `;SSL Mode=Require;Pooling=true;Maximum Pool Size=20;Max Auto Prepare=0`
- the **direct** string (no `-pooler`) → `ConnectionStrings__Migrations`, append `;SSL Mode=Require`

Runtime queries use the pooler; boot migration + `dotnet ef` use the direct endpoint (Task 3 Step 4). This split is the A3-blocker fix — do not point both at the pooler.

- [ ] **Step 3: Create `render.yaml`** (free web service from the Dockerfile)

```yaml
services:
  - type: web
    name: orkabi
    runtime: docker
    plan: free
    dockerfilePath: ./src/Orkabi.Web/Dockerfile
    healthCheckPath: /health
    envVars:
      - key: ConnectionStrings__Default      # Neon POOLED endpoint (+ Max Auto Prepare=0)
        sync: false
      - key: ConnectionStrings__Migrations   # Neon DIRECT endpoint (boot migration only)
        sync: false
      - key: Authentication__Google__ClientId
        sync: false
      - key: Authentication__Google__ClientSecret
        sync: false
      - key: SEED_ADMIN_EMAIL                 # first-admin seed (create-if-absent)
        sync: false
      - key: SEED_ADMIN_PASSWORD             # remove after first successful deploy
        sync: false
```

- [ ] **Step 4: Configure the Google OAuth consent + redirect URI**

In Google Cloud Console, add the authorized redirect URI `https://orkabi.onrender.com/signin-google` (and the eventual custom domain). Put the client id/secret into Render env vars.

- [ ] **Step 5: Push and deploy**

```bash
git add -A
git commit -m "chore: Dockerfile + render.yaml for Render+Neon deploy"
# create the GitHub repo, push, connect it in Render (or `render blueprint launch`)
```

- [ ] **Step 6: Seed the first admin (env-var guarded, create-if-absent)** — extend `DataSeeder`:

```csharp
public static async Task SeedAdminAsync(IServiceProvider sp, IConfiguration cfg)
{
    var email = cfg["SEED_ADMIN_EMAIL"]; var pwd = cfg["SEED_ADMIN_PASSWORD"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pwd)) return;
    var users = sp.GetRequiredService<UserManager<AppUser>>();
    if (await users.FindByEmailAsync(email) is not null) return;
    var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
    if ((await users.CreateAsync(admin, pwd)).Succeeded)
        await users.AddToRoleAsync(admin, AppRoles.Admin);
}
```

Call it right after `SeedRolesAsync` in the boot block (also skipped under Testing). Set the two `SEED_ADMIN_*` Render env vars for the first deploy, then remove `SEED_ADMIN_PASSWORD`. Document in `docs/DEPLOY.md`.

- [ ] **Step 7: End-to-end verification (manual)**

> First request after idle hits a ~30-60s cold start (Render free spin-up + Neon resume). Expected — not a failure. Document in `docs/DEPLOY.md`.

- **Acceptance — real-Neon boot migration (the seam Testcontainers structurally cannot catch):** Render boot logs must show migrations applied against Neon with **no `prepared statement "_pN" already exists` / `does not exist` errors**. If they appear, either the runtime string is missing `Max Auto Prepare=0` or migration ran on the pooler instead of the direct endpoint.
- Visit `https://orkabi.onrender.com/health` → `{"status":"ok"}`.
- Visit `/Account/Login` → RTL Hebrew glass card with **real Hebrew letterforms** (not boxes).
- Log in as the seeded admin → lands on `/Dashboard/Admin`.
- Click "המשך עם Google" → completes sign-in → a dashboard (or AccessDenied "awaiting role" if that Google account has no role yet).
- Confirm in Neon's dashboard that the `AspNetUsers` row exists.

- [ ] **Step 8: Commit the deploy docs**

```bash
git add docs/DEPLOY.md
git commit -m "docs: deployment guide (Render + Neon + Google OAuth)"
```

---

## Self-Review (incorporating Architect + FullStack reviews)

**Spec coverage (Slice 0 scope):** auth (Identity + Google + cookie + 401-for-api) ✅ Tasks 6, 6A, 7, 8; 4 roles + policies ✅ Tasks 6, 9; archival global filter + `is_active`-vs-`Archived` invariant ✅ Task 5; audit fields ✅ Task 4; Israel-TZ constant ✅ Task 10 Step 4b (`IsraelClock` stub); RTL + Apple-glass base + real Hebrew fonts ✅ Task 10; Neon (split pooled/direct) + migrate-on-boot ✅ Tasks 3, 11; `/api/*` 401-JSON seam proven ✅ Task 6A; deployed walking skeleton ✅ Task 11; test harness (xUnit + WebApplicationFactory + factory-owned Testcontainers migration) ✅ Tasks 2-3.

**Blockers fixed (FullStack):** A1 — test harness owns migration; `Program` boot-migrate guarded out of `Testing` (Task 3). A2 — archival/audit tests isolated by a unique per-test tag (Tasks 4-5). A3 — migrations on Neon's **direct** endpoint, `Max Auto Prepare=0` on the pooled runtime string (Tasks 3, 11). B2 — cookie `SecurePolicy` env-branched (Task 6). Plus should-fixes: `/api` 401 proven (6A), env-var admin seed + `.dockerignore` + real-Neon acceptance check (11), real fonts (10), Google scheme/redirect/no-role notes (8).

**Placeholder scan:** every code step has real code; commands have expected output. No TBD/TODO.

**Type consistency:** `AppUser`/`AppRole`/`AppRoles.*` consistent across Tasks 6-9; `OrkabiAppFactory.ConnectionString`/`WithConfig`/`Migrate()` consistent across Tasks 3-9; `AppDbContext` evolves DbContext→IdentityDbContext in Task 6 (noted in its interface block).

**Tracked for cleanup (must not ship):** the `Probe` test entity + its migrations are scaffolding — **remove them at the start of Slice 1**, replaced by the first real aggregate root that opts into the archival filter.

**Deferred within this slice (transparency):** anti-forgery's *token requirement on state-changing API calls* is first exercised in **Slice 2** (the attendance POST); the 401-redirect path itself is proven here (Task 6A). `IsraelClock` has no consumer until Slice 2 — the constant is merely established now.
