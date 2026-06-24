# Slice 1 — People Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **Design reference (binding for all UI tasks):** `docs/design/slice-1-people-design.md` (exact Hebrew copy, markup class names, paste-ready CSS). **Architecture source:** the architect blueprint folded into this plan. **Spec:** `docs/superpowers/specs/2026-06-23-orkabi-design.md` §4/§7/§8.

**Goal:** Add the People bounded context (AcademicYear, School, Class, Client, Enrollment join) so Customer-Service/Admin users can manage schools, classes, students, and **build class rosters** — all in Hebrew RTL Liquid-Glass, deployed to Neon.

**Architecture:** New `Modules/People/` bounded context: EF entities on `BaseEntity` (audit via the existing interceptor), one archival global query filter (Class only), a thin service layer per aggregate (no cross-module `DbSet` reaching), and Razor Pages under `Pages/People/` gated by a new `AppRoles.CsOrAdmin`. Dual-provider stays: SQLite `EnsureCreated` for tests, additive Npgsql migrations applied to Neon at boot.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core 9 (+ Npgsql / Sqlite providers), ASP.NET Identity (int keys), xUnit + WebApplicationFactory.

## Global Constraints

- **Branch:** all work on `slice-1-people`; merge to `master` only at the final deploy task (Render auto-deploys `master` → must not ship half-built work to the live site).
- **Build gate:** `dotnet build` must stay clean under `Nullable=enable` (warnings-as-errors posture). `dotnet test` must stay green at every task boundary.
- **Hebrew-only, RTL:** every user-facing string is Hebrew (see design doc copy tables). Logical CSS properties only; numerals/phones/dates wrapped in `<bdi class="num">`.
- **Dates:** pure calendar dates use `DateOnly`; instants use `DateTime` (UTC, `timestamptz`). Int-backed enums via `.HasConversion<int>()`.
- **Archival:** the global query filter goes on **Class only**. NEVER on AcademicYear, School, Client, or Enrollment. `IsActive=false` (Client dropout) is orthogonal to `Archived` and must remain visible (see `Shared/IArchivable.cs` invariant comment).
- **Migrations:** additive only. Never squash applied history. Generate with `dotnet ef migrations add <Name> --project src/Orkabi.Web` (design-time factory uses Npgsql; no live DB needed to scaffold). Keep all historical migration files.
- **No placeholder data:** unlike the Slice-0 dashboards, do not hardcode fake counts/rows. Show real data or a proper empty state.
- **Authorization:** all People surfaces are `[Authorize(Roles = AppRoles.CsOrAdmin)]`.

---

## Task 1: Probe removal + core entities (AcademicYear, School, Class) + archival filter

**Files:**
- Modify: `src/Orkabi.Web/Data/AppDbContext.cs` (remove Probe; add 3 DbSets + config)
- Create: `src/Orkabi.Web/Modules/People/AcademicYear.cs`, `School.cs`, `Class.cs`
- Create (via EF): `src/Orkabi.Web/Migrations/<ts>_RemoveProbe.cs`, `<ts>_AddPeopleCore.cs`
- Modify: `tests/Orkabi.Web.Tests/ArchivalFilterTests.cs`, `AuditInterceptorTests.cs` (re-point off Probe → Class)

**Interfaces produced (later tasks consume these exact shapes):**
```csharp
namespace Orkabi.Web.Modules.People;

public class AcademicYear : Orkabi.Web.Shared.BaseEntity   // lookup — NOT IArchivable
{
    public string Label { get; set; } = "";        // e.g. "תשפ\"ו"
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public ICollection<Class> Classes { get; set; } = new List<Class>();
}

public class School : Orkabi.Web.Shared.BaseEntity        // NOT IArchivable (no status in spec §4)
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string? ExternalWebsiteUrl { get; set; }
    public ICollection<Class> Classes { get; set; } = new List<Class>();
}

public class Class : Orkabi.Web.Shared.BaseEntity, Orkabi.Web.Shared.IArchivable  // the ONLY archival aggregate
{
    public string Name { get; set; } = "";
    public int SchoolId { get; set; }
    public School School { get; set; } = null!;
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
    public Orkabi.Web.Shared.EntityStatus Status { get; set; } = Orkabi.Web.Shared.EntityStatus.Active;
    // SyllabusId DEFERRED to Slice 2 (Syllabus table does not exist yet).
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();  // Enrollment lands in Task 2
}
```
> Until Task 2 adds `Enrollment`, the `Enrollments` navigation references a not-yet-existing type. To keep Task 1 self-contained and compiling, **omit the `Enrollments` collection from `Class` in Task 1** and add it in Task 2 together with `Enrollment`. (Documented so a reader knows it's intentional, not forgotten.)

- [ ] **Step 1: Create the three entity files** exactly as above (minus `Class.Enrollments`, per the note).

- [ ] **Step 2: Remove Probe and wire core entities in `AppDbContext.cs`.** Delete the `Probe` class, its `DbSet`, and both `b.Entity<Probe>()` lines. Add:
```csharp
public DbSet<Orkabi.Web.Modules.People.AcademicYear> AcademicYears => Set<Orkabi.Web.Modules.People.AcademicYear>();
public DbSet<Orkabi.Web.Modules.People.School> Schools => Set<Orkabi.Web.Modules.People.School>();
public DbSet<Orkabi.Web.Modules.People.Class> Classes => Set<Orkabi.Web.Modules.People.Class>();
```
In `OnModelCreating`, replace the Probe lines with:
```csharp
// ARCHIVAL — Class is the only Slice 1 aggregate root. AcademicYear/School (and later
// Client/Enrollment) get NO filter. See Shared/IArchivable.cs for the is_active vs Archived invariant.
b.Entity<People.Class>().HasQueryFilter(c => c.Status == Shared.EntityStatus.Active);
b.Entity<People.Class>().Property(c => c.Status).HasConversion<int>();

b.Entity<People.Class>().Property(c => c.Name).HasMaxLength(200).IsRequired();
b.Entity<People.School>().Property(s => s.Name).HasMaxLength(200).IsRequired();
b.Entity<People.School>().Property(s => s.City).HasMaxLength(100).IsRequired();
b.Entity<People.AcademicYear>().Property(y => y.Label).HasMaxLength(20).IsRequired();

b.Entity<People.Class>().HasOne(c => c.School).WithMany(s => s.Classes)
    .HasForeignKey(c => c.SchoolId).OnDelete(DeleteBehavior.Restrict);
b.Entity<People.Class>().HasOne(c => c.AcademicYear).WithMany(y => y.Classes)
    .HasForeignKey(c => c.AcademicYearId).OnDelete(DeleteBehavior.Restrict);

// One current academic year, enforced at the DB (partial unique index).
b.Entity<People.AcademicYear>().HasIndex(y => y.IsCurrent).HasFilter("\"IsCurrent\" = true").IsUnique();
// Class name unique per school+year while Active (archived rows free the name).
b.Entity<People.Class>().HasIndex(c => new { c.SchoolId, c.AcademicYearId, c.Name })
    .HasFilter("\"Status\" = 0").IsUnique();
```
(`using Orkabi.Web.Modules;` or fully-qualify `People.*` — match the file's existing using style.)

- [ ] **Step 3: Generate the RemoveProbe migration FIRST** (before the new tables exist in a migration), then the core tables. EF diffs against the snapshot, so order matters — but both new entities are already in the model now, so a single `migrations add` would capture drop+create together. To keep them separate and readable, generate one migration capturing this whole task as `AddPeopleCore_RemoveProbe`:
```bash
dotnet ef migrations add AddPeopleCoreRemoveProbe --project src/Orkabi.Web
```
Open the generated `Up()` and CONFIRM it contains: `migrationBuilder.DropTable(name: "Probes")`, `CreateTable(name: "AcademicYears")`, `"Schools"`, `"Classes"` (with FKs to Schools + AcademicYears, `onDelete: Restrict`), and the two partial unique indexes (`filter: "\"IsCurrent\" = true"`, `filter: "\"Status\" = 0"`). If `Probes` drop is missing, the model still references Probe — fix step 2.
> Rationale for one combined migration here: the project is young and we control prod; a single coherent "swap Probe scaffold for People core" migration is cleaner than an empty-purpose RemoveProbe followed immediately by AddPeopleCore. Historical migrations are still preserved (never deleted).

- [ ] **Step 4: Re-point the Probe tests to Class (failing test first).** Rewrite `ArchivalFilterTests` to assert the Class global filter, arranging a School + AcademicYear first:
```csharp
[Fact]
public async Task Archived_classes_are_hidden_by_default_and_visible_with_IgnoreQueryFilters()
{
    using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var school = new School { Name = "בית ספר בדיקה", City = "חיפה" };
    var year = new AcademicYear { Label = "תשפ\"ו", StartDate = new DateOnly(2025,9,1), EndDate = new DateOnly(2026,6,30), IsCurrent = true };
    db.Schools.Add(school); db.AcademicYears.Add(year); await db.SaveChangesAsync();

    var tag = $"cls-{Guid.NewGuid():N}";
    db.Classes.Add(new Class { Name = tag + "-a", School = school, AcademicYear = year, Status = EntityStatus.Active });
    db.Classes.Add(new Class { Name = tag + "-b", School = school, AcademicYear = year, Status = EntityStatus.Archived });
    await db.SaveChangesAsync();

    Assert.Equal(1, await db.Classes.Where(c => c.Name.StartsWith(tag)).CountAsync());
    Assert.Equal(2, await db.Classes.IgnoreQueryFilters().Where(c => c.Name.StartsWith(tag)).CountAsync());
}
```
Add `using Orkabi.Web.Modules.People;`. Update `AuditInterceptorTests` to stamp a `School` (or `Class`) instead of `Probe` — same assertions (CreatedAt/UpdatedAt set, CreatedByUserId from the current user).

- [ ] **Step 5: Build + test green.**
```bash
dotnet build -clp:ErrorsOnly && dotnet test
```
Expected: build clean; tests ≥ previous count, 0 failed. (Probe tests replaced by Class tests.)

- [ ] **Step 6: Commit.**
```bash
git add -A && git commit -m "feat(people): remove Probe scaffold; add AcademicYear/School/Class entities + archival filter + migration"
```

---

## Task 2: Client + Enrollment (+ EnrollmentStatus) entities, indexes, migration

**Files:**
- Create: `src/Orkabi.Web/Modules/People/Client.cs`, `Enrollment.cs`, `EnrollmentStatus.cs`
- Modify: `src/Orkabi.Web/Modules/People/Class.cs` (add the `Enrollments` collection now)
- Modify: `src/Orkabi.Web/Data/AppDbContext.cs` (2 DbSets + config)
- Create (via EF): `<ts>_AddPeopleEnrollment.cs`
- Test: `tests/Orkabi.Web.Tests/EnrollmentTests.cs`

**Interfaces produced:**
```csharp
public enum EnrollmentStatus { Active = 0, Tryout = 1, Dropped = 2, Completed = 3 }

public class Client : BaseEntity   // NOT IArchivable — uses IsActive (orthogonal to archival)
{
    public string Name { get; set; } = "";
    public string? ParentPhone { get; set; }
    public int? Age { get; set; }
    public string? Address { get; set; }
    public DateOnly? Birthday { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public class Enrollment : BaseEntity   // join; own lifecycle via EnrollmentStatus
{
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int ClassId { get; set; }
    public Class Class { get; set; } = null!;
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public bool IsTryout { get; set; }       // "started as a tryout" (historical); Status.Tryout = "currently in tryout"
    public bool PaidMaterials { get; set; }
    public bool PaidMonthly { get; set; }
    public DateTime EnrolledAt { get; set; } // business instant (UTC); set by the service at create, NOT the audit interceptor
}
```

- [ ] **Step 1: Write the failing enrollment-uniqueness test** in `EnrollmentTests.cs`:
```csharp
[Fact]
public async Task Duplicate_active_enrollment_for_same_client_and_class_is_rejected()
{
    using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var (client, cls) = await SeedClientAndClass(db);   // helper arranges School+Year+Class+Client

    db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = cls.Id, EnrolledAt = DateTime.UtcNow });
    await db.SaveChangesAsync();
    db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = cls.Id, EnrolledAt = DateTime.UtcNow });

    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
}

[Fact]
public async Task Re_enrollment_allowed_after_drop()
{
    // arrange one Dropped enrollment, then a new Active one for same (client,class) → SaveChanges succeeds
}

[Fact]
public async Task Client_can_hold_multiple_active_enrollments_in_different_classes()
{
    // one client, two different classes → both Active enrollments save
}
```

- [ ] **Step 2: Run → fails** (`Enrollment` not defined). `dotnet test --filter EnrollmentTests`.

- [ ] **Step 3: Add entities + `EnrollmentStatus`; add `Class.Enrollments`.** Configure in `OnModelCreating`:
```csharp
public DbSet<People.Client> Clients => Set<People.Client>();
public DbSet<People.Enrollment> Enrollments => Set<People.Enrollment>();
// ...
b.Entity<People.Client>().Property(c => c.Name).HasMaxLength(200).IsRequired();
b.Entity<People.Enrollment>().Property(e => e.Status).HasConversion<int>();
b.Entity<People.Enrollment>().HasOne(e => e.Client).WithMany(c => c.Enrollments)
    .HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Restrict);
b.Entity<People.Enrollment>().HasOne(e => e.Class).WithMany(c => c.Enrollments)
    .HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Restrict);
// one enrollment per (client,class) while not Dropped (Status 2 = Dropped → re-enroll allowed)
b.Entity<People.Enrollment>().HasIndex(e => new { e.ClientId, e.ClassId })
    .HasFilter("\"Status\" <> 2").IsUnique();
```
> SQLite uses `<>` and `!=` interchangeably; Npgsql uses `<>`. Use `<>` for both.

- [ ] **Step 4: Run → passes.** `dotnet test --filter EnrollmentTests`.

- [ ] **Step 5: Generate the migration.**
```bash
dotnet ef migrations add AddPeopleEnrollment --project src/Orkabi.Web
```
Confirm `Up()` creates `Clients` + `Enrollments` (FKs `onDelete: Restrict`, the filtered unique index `filter: "\"Status\" <> 2"`), and adds no unexpected drops.

- [ ] **Step 6: Full build + test green, then commit.**
```bash
dotnet build -clp:ErrorsOnly && dotnet test
git add -A && git commit -m "feat(people): add Client + Enrollment join (EnrollmentStatus, partial-unique index, Restrict FKs) + migration"
```

---

## Task 3: AcademicYear seed + AppRoles.CsOrAdmin

**Files:**
- Modify: `src/Orkabi.Web/Data/DataSeeder.cs` (add `SeedAcademicYearAsync`)
- Modify: `src/Orkabi.Web/Program.cs` (call it after `SeedAdminAsync`, inside the `!Testing && !Sqlite` gate)
- Modify: `src/Orkabi.Web/Modules/Identity/AppRoles.cs` (add `CsOrAdmin`)
- Test: `tests/Orkabi.Web.Tests/AcademicYearSeedTests.cs`

- [ ] **Step 1: Add the role-group constant** to `AppRoles.cs`:
```csharp
public const string CsOrAdmin = Admin + "," + CustomerService;
```

- [ ] **Step 2: Add `SeedAcademicYearAsync`** to `DataSeeder` (idempotent; current Israeli school year **תשפ"ו / 5786**, 2025-09-01 → 2026-06-30):
```csharp
public static async Task SeedAcademicYearAsync(IServiceProvider sp)
{
    var db = sp.GetRequiredService<AppDbContext>();
    if (await db.AcademicYears.AnyAsync()) return;   // idempotent
    db.AcademicYears.Add(new Orkabi.Web.Modules.People.AcademicYear
    {
        Label = "תשפ\"ו",
        StartDate = new DateOnly(2025, 9, 1),
        EndDate = new DateOnly(2026, 6, 30),
        IsCurrent = true
    });
    await db.SaveChangesAsync();
}
```

- [ ] **Step 3: Wire it in `Program.cs`** inside the existing seed scope, after `SeedAdminAsync`:
```csharp
await DataSeeder.SeedAcademicYearAsync(scope.ServiceProvider);
```

- [ ] **Step 4: Test** (drives idempotency on SQLite via a direct seeder call, since boot-seed is gated off under Testing):
```csharp
[Fact]
public async Task Seed_creates_exactly_one_current_year_and_is_idempotent()
{
    using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
    using var scope = factory.Services.CreateScope();
    var sp = scope.ServiceProvider;
    await DataSeeder.SeedAcademicYearAsync(sp);
    await DataSeeder.SeedAcademicYearAsync(sp);   // second call must be a no-op
    var db = sp.GetRequiredService<AppDbContext>();
    Assert.Equal(1, await db.AcademicYears.CountAsync());
    Assert.True((await db.AcademicYears.SingleAsync()).IsCurrent);
}
```

- [ ] **Step 5: Build + test + commit.**
```bash
dotnet build -clp:ErrorsOnly && dotnet test
git add -A && git commit -m "feat(people): seed current academic year תשפ\"ו + add AppRoles.CsOrAdmin"
```

---

## Task 4: People service layer

**Files:**
- Create: `src/Orkabi.Web/Modules/People/AcademicYearService.cs`, `SchoolService.cs`, `ClassService.cs`, `ClientService.cs`, `EnrollmentService.cs`
- Modify: `src/Orkabi.Web/Program.cs` (register services scoped)
- Test: `tests/Orkabi.Web.Tests/PeopleServiceTests.cs`

**Design:** thin services over `AppDbContext` (constructor-injected). They own the non-trivial invariants so pages stay dumb. Key methods (exact signatures later tasks rely on):
```csharp
// AcademicYearService
Task<List<AcademicYear>> ListAsync();
Task<AcademicYear?> GetCurrentAsync();
Task SetCurrentAsync(int academicYearId);   // transaction: clear old current, set new (see invariant)

// SchoolService
Task<List<School>> ListAsync(string? q = null);   // q filters Name/City (ILIKE/Contains)
Task<School?> GetAsync(int id);
Task<School> CreateAsync(School s);
Task UpdateAsync(School s);

// ClassService
Task<List<Class>> ListAsync(int? schoolId, int? academicYearId, EntityStatus? status); // status==null => current (Active) view; Archived => IgnoreQueryFilters
Task<Class?> GetAsync(int id);
Task<Class> CreateAsync(Class c);
Task UpdateAsync(Class c);
Task ArchiveAsync(int id);   // sets Status=Archived; NO enrollment cascade

// ClientService
Task<List<Client>> ListAsync(string? q, bool activeOnly);  // activeOnly default true; q filters Name/ParentPhone
Task<Client?> GetAsync(int id);
Task<Client> CreateAsync(Client c);
Task UpdateAsync(Client c);

// EnrollmentService
Task<List<Enrollment>> ListByClassAsync(int classId);          // includes Client; excludes Dropped
Task<List<Client>> ListAvailableForClassAsync(int classId, string? q); // active clients not currently enrolled
Task<Enrollment> EnrollAsync(int classId, int clientId);        // guards duplicate (app-level) → throws InvalidOperationException with Hebrew msg
Task DropAsync(int enrollmentId);                               // Status=Dropped (soft)
Task ToggleAsync(int enrollmentId, string field);              // field in {materials,monthly,tryout}; tryout also flips Status to/from Tryout
```

- [ ] **Step 1: Write failing service tests** in `PeopleServiceTests.cs` for the invariants that matter:
  - `SetCurrentAsync` makes the target the only `IsCurrent` (and the partial index isn't violated mid-transaction).
  - `ClassService.ListAsync(status: Archived)` returns archived classes (bypasses the filter); default view excludes them.
  - `ClientService.ListAsync(activeOnly:true)` excludes `IsActive=false`; `activeOnly:false` includes them (dropout still visible — invariant).
  - `EnrollmentService.EnrollAsync` twice for the same (class,client) throws `InvalidOperationException` (app-level guard, friendly), not a raw `DbUpdateException`.
  - `ToggleAsync(id,"tryout")` flips `IsTryout` and sets `Status` Tryout↔Active.
  - `ListAvailableForClassAsync` excludes already-enrolled and inactive clients.

- [ ] **Step 2: Run → fails** (services not defined).

- [ ] **Step 3: Implement the services.** Notes:
  - `SetCurrentAsync`: `using var tx = await _db.Database.BeginTransactionAsync();` → `UPDATE` all current to false (`ExecuteUpdateAsync` or load+set) → set target true → `SaveChanges` → commit.
  - `ClassService.ListAsync`: when `status == EntityStatus.Archived` or "all", use `_db.Classes.IgnoreQueryFilters()` and filter explicitly; otherwise the global filter handles Active.
  - `EnrollAsync`: check `await _db.Enrollments.AnyAsync(e => e.ClassId==classId && e.ClientId==clientId && e.Status != EnrollmentStatus.Dropped)` → if true throw `new InvalidOperationException("התלמיד כבר רשום לכיתה זו")`; else add with `EnrolledAt = DateTime.UtcNow`, `Status = Active`.
  - Comment the `is_active` vs `Archived` invariant on `ClientService.ListAsync`.

- [ ] **Step 4: Register in `Program.cs`** (after the existing `AddScoped` lines):
```csharp
builder.Services.AddScoped<Orkabi.Web.Modules.People.AcademicYearService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.SchoolService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.ClassService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.ClientService>();
builder.Services.AddScoped<Orkabi.Web.Modules.People.EnrollmentService>();
```

- [ ] **Step 5: Run → passes; full build + test; commit.**
```bash
dotnet build -clp:ErrorsOnly && dotnet test
git add -A && git commit -m "feat(people): service layer (AcademicYear/School/Class/Client/Enrollment) with archival + enrollment invariants"
```

---

## Task 5: base.css design additions

**Files:**
- Modify: `src/Orkabi.Web/wwwroot/css/base.css` (append the §6 block from the design doc, before the `@supports not` block; add the two fallback lines into it)
- Test: `tests/Orkabi.Web.Tests/RtlLayoutTests.cs` (extend — assert a new People class is served)

- [ ] **Step 1: Verify every token** referenced in the design doc §6 exists in `wwwroot/css/tokens.css` (e.g. `--lg-fill-tint`, `--lg-fill-deep`, `--lg-fill-solid`, `--lg-shadow`, `--lg-shadow-lifted`, `--brand-ink-muted`, `--brand-ink-faint`, `--ok-soft`, `--warn-soft`, `--absent-soft`, `--ls-wide`, `--t-admin-*`, `--radius-*`, `--sp-*`, `--ease-*`, `--dur-*`). If any is MISSING, stop and report — do not invent values.

- [ ] **Step 2: Paste the design-doc §6 CSS block** into `base.css` (verbatim), and add the two lines into the existing `@supports not (...)` block.

- [ ] **Step 3: Build (static assets) + extend RtlLayoutTests** to assert the served `base.css` contains a People marker (e.g. `.roster-pane` or `.subnav`). Run `dotnet test --filter RtlLayoutTests`.

- [ ] **Step 4: Commit.**
```bash
git add -A && git commit -m "feat(ui): People surface CSS — tables, forms, segmented/toggle controls, roster two-pane, tryout tray"
```

---

## Task 6: People Hub + Schools pages

**Files:**
- Create: `Pages/People/Index.cshtml(.cs)` (hub), `Pages/People/Schools/Index.cshtml(.cs)`, `Create.cshtml(.cs)`, `Edit.cshtml(.cs)`
- Test: `tests/Orkabi.Web.Tests/PeopleAuthzTests.cs`

**Build to:** design doc §1 (hub) + §2 (Schools). All page models `[Authorize(Roles = AppRoles.CsOrAdmin)]`, inject the relevant service. Use a nested `InputModel` for forms (bind `Input.Name` etc. matching the design doc field names). Hub shows the current `AcademicYear` label via `AcademicYearService.GetCurrentAsync()` (or the "לא הוגדרה שנת לימודים" warn state if null).

- [ ] **Step 1: Write failing authz integration tests** in `PeopleAuthzTests.cs`:
```csharp
[Fact] public async Task Anonymous_is_redirected_to_login_from_people_hub() { /* GET /People → 302 /Account/Login */ }
[Fact] public async Task Instructor_is_forbidden_from_people_hub()       { /* login Instructor → GET /People → 403 */ }
[Fact] public async Task Cs_user_can_open_schools_index()                { /* login CS → GET /People/Schools → 200 */ }
```
Use `TestLogin.SignInAsync` + a seeded CS user (extend the factory/seed helpers as `AdminSeedTests`/`RoleRoutingTests` do — create a CS user with `UserManager` + `AddToRoleAsync(CustomerService)`).

- [ ] **Step 2: Run → fails** (pages 404).

- [ ] **Step 3: Implement the hub + Schools pages** per design doc §1/§2 (exact Hebrew copy, classes, recessed-well forms, empty states). Create posts via `SchoolService.CreateAsync`; edit via `GetAsync`/`UpdateAsync`. Server-side validation → Hebrew `ModelState` errors shown in `.form-field__error`.

- [ ] **Step 4: Run → passes; full test; commit.**
```bash
dotnet build -clp:ErrorsOnly && dotnet test
git add -A && git commit -m "feat(people): People hub + Schools CRUD pages (CsOrAdmin, Hebrew RTL glass)"
```

---

## Task 7: Classes pages

**Files:**
- Create: `Pages/People/Classes/Index.cshtml(.cs)`, `Create.cshtml(.cs)`, `Edit.cshtml(.cs)`
- Test: extend `PeopleAuthzTests` / add `ClassesPageTests`

**Build to:** design doc §3. Index supports filter by school/year/status; archived rows appear only under the `בארכיון` filter (via `ClassService.ListAsync(status: Archived)`). Create/Edit use selects populated from `SchoolService.ListAsync()` + `AcademicYearService.ListAsync()` (year defaults to current). Status is the segmented control.

- [ ] **Step 1: Failing test** — archived class hidden in default index, shown under archived filter:
```csharp
[Fact] public async Task Classes_index_hides_archived_by_default_and_shows_them_when_filtered() { /* seed 1 Active + 1 Archived; GET /People/Classes → only Active in body; GET ?status=Archived → archived present */ }
```
- [ ] **Step 2: Run → fails.**
- [ ] **Step 3: Implement** Classes pages per design doc §3.
- [ ] **Step 4: Run → passes; full test; commit** `feat(people): Classes CRUD + archived filter`.

---

## Task 8: Clients pages

**Files:**
- Create: `Pages/People/Clients/Index.cshtml(.cs)`, `Create.cshtml(.cs)`, `Edit.cshtml(.cs)`
- Test: add `ClientsPageTests`

**Build to:** design doc §4. Index search + `פעילים/כולם` filter; inactive clients dimmed (`.data-row--muted`) but listed when "כולם". Form: only `Name` required; phone LTR; age 3–21; birthday `type=date`; `IsActive` toggle. Numerals in `<bdi class="num">`.

- [ ] **Step 1: Failing test** — inactive client hidden under "פעילים", visible under "כולם":
```csharp
[Fact] public async Task Clients_index_active_filter_hides_inactive_but_all_filter_shows_them() { }
```
- [ ] **Step 2: Run → fails.**
- [ ] **Step 3: Implement** per design doc §4.
- [ ] **Step 4: Run → passes; full test; commit** `feat(people): Clients CRUD + active filter`.

---

## Task 9: Roster builder (the signature surface)

**Files:**
- Create: `Pages/People/Classes/Roster.cshtml(.cs)`
- Test: add `RosterTests`

**Build to:** design doc §5. Two-pane: enrolled (`EnrollmentService.ListByClassAsync`) + available (`ListAvailableForClassAsync`). POST handlers: `OnPostAdd(int clientId)` → `EnrollAsync`; `OnPostRemove(int enrollmentId)` → `DropAsync`; `OnPostToggle(int enrollmentId, string field)` → `ToggleAsync`. Full-page re-render (no HTMX in Slice 1). Duplicate-enroll surfaces as a Hebrew `ModelState`/toast message, never a 500. Tryout rows render in the tray with the TRYOUT badge.

- [ ] **Step 1: Failing tests** in `RosterTests.cs`:
```csharp
[Fact] public async Task Cs_can_enroll_a_client_then_it_appears_on_the_roster() { }
[Fact] public async Task Enrolling_same_client_twice_shows_friendly_error_not_500() { }
[Fact] public async Task Dropping_an_enrollment_moves_client_back_to_available() { }
[Fact] public async Task Toggling_tryout_marks_enrollment_and_moves_it_to_the_tray() { }
```
- [ ] **Step 2: Run → fails.**
- [ ] **Step 3: Implement** the Roster page + handlers per design doc §5.
- [ ] **Step 4: Run → passes; full build + test; commit** `feat(people): roster builder — enroll/drop/tryout+payment toggles (signature surface)`.

---

## Task 10: Deploy to Neon + verify + handoff

**Files:**
- Modify: `docs/HANDOFF.md` (mark Slice 1 done; update status), `docs/superpowers/plans/2026-06-23-orkabi-roadmap.md` (tick Slice 1 if desired)

- [ ] **Step 1: Final full green** on `slice-1-people`: `dotnet build -clp:ErrorsOnly && dotnet test`.
- [ ] **Step 2: Self-review vs spec §4** — every People entity/field present; archival only on Class; CS-or-Admin everywhere; no placeholder data.
- [ ] **Step 3: Whole-branch reviewer gate** (strongest-model reviewer agent) — fix any findings before merge.
- [ ] **Step 4: Apply migrations to Neon** (direct endpoint), confirming they apply cleanly on top of the live schema:
```bash
ORKABI_MIGRATIONS_CONNSTRING="<direct neon string>" dotnet ef database update --project src/Orkabi.Web
```
> If the live Neon string isn't available to the build environment, rely on boot-time `MigrateAsync` after merge — but verify the Render deploy log shows the three migrations applied and the year seeded. (Credentials are a genuine blocker → surface to the user only if `dotnet ef database update` is required and the string is unavailable.)
- [ ] **Step 5: Merge to master + push** (triggers Render auto-deploy):
```bash
git checkout master && git merge --no-ff slice-1-people && git push origin master
```
- [ ] **Step 6: Smoke test the live site** — CS login → /People → create School → create Class (current year) → create Client → open Roster → enroll → mark tryout → drop. Confirm boot log applied migrations + seeded תשפ"ו.
- [ ] **Step 7: Update `docs/HANDOFF.md`** (Slice 1 complete + live), commit, push.

---

## Self-Review (writing-plans checklist)

- **Spec §4 coverage:** AcademicYear ✔(T1,T3) · School ✔(T1) · Class+status+year, no SyllabusId ✔(T1) · Client+IsActive ✔(T2) · Enrollment+tryout/paid flags ✔(T2,T9). Archival on Class only ✔(T1). §7 RBAC CsOrAdmin ✔(T3,T6). §8 DateOnly/int-enums/additive-migrations ✔.
- **Roster (slice goal "CS can build rosters")** ✔(T9).
- **Type consistency:** entity names/props match across T1–T9 and the design doc field names (`Input.*`). EnrollmentStatus values fixed (Active0/Tryout1/Dropped2/Completed3); the `<> 2` index matches Dropped=2.
- **No placeholders:** UI references the on-disk design doc (exact copy/CSS), not "TBD".
- **Deferred (disclosed):** `Class.SyllabusId` → Slice 2; HTMX/optimistic roster → Slice 2; bulk-enroll → later; Google OAuth / Hebrew Identity errors / GoogleSchemeTests isolation → unchanged from HANDOFF (not Slice 1 scope).
