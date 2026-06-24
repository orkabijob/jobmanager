using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;
using ActionHub = Orkabi.Web.Modules.ActionHub;
using Curriculum = Orkabi.Web.Modules.Curriculum;
using Operations = Orkabi.Web.Modules.Operations;
using People = Orkabi.Web.Modules.People;
using Scheduling = Orkabi.Web.Modules.Scheduling;
using Logistics = Orkabi.Web.Modules.Logistics;

namespace Orkabi.Web.Data;

public class AppDbContext : IdentityDbContext<AppUser, AppRole, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<People.AcademicYear> AcademicYears => Set<People.AcademicYear>();
    public DbSet<People.School> Schools => Set<People.School>();
    public DbSet<People.Class> Classes => Set<People.Class>();
    public DbSet<People.Client> Clients => Set<People.Client>();
    public DbSet<People.Enrollment> Enrollments => Set<People.Enrollment>();

    public DbSet<Curriculum.Model> Models => Set<Curriculum.Model>();
    public DbSet<Curriculum.Syllabus> Syllabi => Set<Curriculum.Syllabus>();
    public DbSet<Curriculum.SyllabusModel> SyllabusModels => Set<Curriculum.SyllabusModel>();

    public DbSet<Scheduling.ShiftTemplate> ShiftTemplates => Set<Scheduling.ShiftTemplate>();
    public DbSet<Scheduling.ShiftInstance> ShiftInstances => Set<Scheduling.ShiftInstance>();
    public DbSet<Scheduling.SubstitutionRequest> SubstitutionRequests => Set<Scheduling.SubstitutionRequest>();
    public DbSet<Scheduling.LessonLog> LessonLogs => Set<Scheduling.LessonLog>();
    public DbSet<Scheduling.Attendance> Attendances => Set<Scheduling.Attendance>();

    public DbSet<Operations.ExtraHours> ExtraHours => Set<Operations.ExtraHours>();
    public DbSet<Operations.IncidentReport> IncidentReports => Set<Operations.IncidentReport>();
    public DbSet<Operations.VacationRequest> VacationRequests => Set<Operations.VacationRequest>();

    public DbSet<ActionHub.ActionItem> ActionItems => Set<ActionHub.ActionItem>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<Logistics.LogisticsOrder> LogisticsOrders => Set<Logistics.LogisticsOrder>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ARCHIVAL — Class is the only Slice 1 aggregate root. AcademicYear/School (and later
        // Client/Enrollment) get NO filter. See Shared/IArchivable.cs for the is_active vs Archived invariant.
        b.Entity<People.Class>().HasQueryFilter(c => c.Status == EntityStatus.Active);
        b.Entity<People.Class>().Property(c => c.Status).HasConversion<int>();

        b.Entity<People.Class>().Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.Entity<People.School>().Property(s => s.Name).HasMaxLength(200).IsRequired();
        b.Entity<People.School>().Property(s => s.City).HasMaxLength(100).IsRequired();
        b.Entity<People.AcademicYear>().Property(y => y.Label).HasMaxLength(20).IsRequired();

        b.Entity<People.Class>().HasOne(c => c.School).WithMany(s => s.Classes)
            .HasForeignKey(c => c.SchoolId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<People.Class>().HasOne(c => c.AcademicYear).WithMany(y => y.Classes)
            .HasForeignKey(c => c.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        // SetNull is the ONE intentional exception to the Restrict convention:
        // retiring/archiving a syllabus must not block existing classes.
        b.Entity<People.Class>().HasOne(c => c.Syllabus).WithMany(s => s.Classes)
            .HasForeignKey(c => c.SyllabusId).OnDelete(DeleteBehavior.SetNull);

        // One current academic year, enforced at the DB (partial unique index).
        b.Entity<People.AcademicYear>().HasIndex(y => y.IsCurrent).HasFilter("\"IsCurrent\" = true").IsUnique();
        // Class name unique per school+year while Active (archived rows free the name).
        b.Entity<People.Class>().HasIndex(c => new { c.SchoolId, c.AcademicYearId, c.Name })
            .HasFilter("\"Status\" = 0").IsUnique();

        // Client
        b.Entity<People.Client>().Property(c => c.Name).HasMaxLength(200).IsRequired();

        // Enrollment
        b.Entity<People.Enrollment>().Property(e => e.Status).HasConversion<int>();
        b.Entity<People.Enrollment>().HasOne(e => e.Client).WithMany(c => c.Enrollments)
            .HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<People.Enrollment>().HasOne(e => e.Class).WithMany(c => c.Enrollments)
            .HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Restrict);
        // One enrollment per (client, class) while not Dropped (Status 2 = Dropped → re-enroll allowed)
        b.Entity<People.Enrollment>().HasIndex(e => new { e.ClientId, e.ClassId })
            .HasFilter("\"Status\" <> 2").IsUnique();

        // CURRICULUM — Syllabus is the aggregate root. Model and SyllabusModel get NO filter.
        b.Entity<Curriculum.Syllabus>().HasQueryFilter(s => s.Status == EntityStatus.Active);
        b.Entity<Curriculum.Syllabus>().Property(s => s.Status).HasConversion<int>();
        b.Entity<Curriculum.Syllabus>().Property(s => s.Name).HasMaxLength(200).IsRequired();
        b.Entity<Curriculum.Model>().Property(m => m.Name).HasMaxLength(200).IsRequired();
        b.Entity<Curriculum.SyllabusModel>().HasKey(sm => new { sm.SyllabusId, sm.ModelId });
        b.Entity<Curriculum.SyllabusModel>().HasOne(sm => sm.Syllabus).WithMany(s => s.SyllabusModels)
            .HasForeignKey(sm => sm.SyllabusId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Curriculum.SyllabusModel>().HasOne(sm => sm.Model).WithMany(m => m.SyllabusModels)
            .HasForeignKey(sm => sm.ModelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Curriculum.SyllabusModel>().HasIndex(sm => new { sm.SyllabusId, sm.OrderIndex }).IsUnique();

        // ── SCHEDULING ──────────────────────────────────────────────────────

        // ShiftTemplate — archival aggregate root; gets the global query filter.
        b.Entity<Scheduling.ShiftTemplate>().HasQueryFilter(t => t.Status == EntityStatus.Active);
        b.Entity<Scheduling.ShiftTemplate>().Property(t => t.Status).HasConversion<int>();
        b.Entity<Scheduling.ShiftTemplate>().Property(t => t.DayOfWeek).HasConversion<int>();
        // TimeOnly → stored as TEXT "HH:mm:ss" (EF Core 9 SQLite; explicit converter is the root mapping).
        b.Entity<Scheduling.ShiftTemplate>().Property(t => t.StartTime)
            .HasConversion(v => v.ToString("HH:mm:ss"), v => TimeOnly.Parse(v));
        b.Entity<Scheduling.ShiftTemplate>().Property(t => t.EndTime)
            .HasConversion(v => v.ToString("HH:mm:ss"), v => TimeOnly.Parse(v));

        // ShiftTemplate FKs (all Restrict)
        b.Entity<Scheduling.ShiftTemplate>()
            .HasOne(t => t.Class).WithMany()
            .HasForeignKey(t => t.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.ShiftTemplate>()
            .HasOne(t => t.DefaultInstructor).WithMany()
            .HasForeignKey(t => t.DefaultInstructorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.ShiftTemplate>()
            .HasOne(t => t.AcademicYear).WithMany()
            .HasForeignKey(t => t.AcademicYearId).OnDelete(DeleteBehavior.Restrict);

        // ShiftTemplate indexes
        b.Entity<Scheduling.ShiftTemplate>()
            .HasIndex(t => new { t.ClassId, t.DayOfWeek, t.AcademicYearId });

        // ShiftInstance
        b.Entity<Scheduling.ShiftInstance>().Property(i => i.Status).HasConversion<int>();
        b.Entity<Scheduling.ShiftInstance>()
            .HasOne(i => i.Template).WithMany(t => t.ShiftInstances)
            .HasForeignKey(i => i.TemplateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.ShiftInstance>()
            .HasOne(i => i.ActualInstructor).WithMany()
            .HasForeignKey(i => i.ActualInstructorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.ShiftInstance>()
            .HasIndex(i => new { i.TemplateId, i.Date }).IsUnique();

        // SubstitutionRequest
        b.Entity<Scheduling.SubstitutionRequest>().Property(r => r.Status).HasConversion<int>();
        b.Entity<Scheduling.SubstitutionRequest>()
            .HasOne(r => r.ShiftInstance).WithMany(i => i.SubstitutionRequests)
            .HasForeignKey(r => r.ShiftInstanceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.SubstitutionRequest>()
            .HasOne(r => r.RequestingInstructor).WithMany()
            .HasForeignKey(r => r.RequestingInstructorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.SubstitutionRequest>()
            .HasOne(r => r.SubstituteInstructor).WithMany()
            .HasForeignKey(r => r.SubstituteInstructorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.SubstitutionRequest>()
            .HasOne(r => r.ApprovedByUser).WithMany()
            .HasForeignKey(r => r.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);

        // LessonLog — one-to-one with ShiftInstance
        b.Entity<Scheduling.LessonLog>().Property(l => l.Status).HasConversion<int>();
        b.Entity<Scheduling.LessonLog>()
            .HasOne(l => l.ShiftInstance).WithOne(i => i.LessonLog)
            .HasForeignKey<Scheduling.LessonLog>(l => l.ShiftInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.LessonLog>()
            .HasOne(l => l.Model).WithMany(m => m.LessonLogs)
            .HasForeignKey(l => l.ModelId).OnDelete(DeleteBehavior.Restrict);

        // Attendance
        b.Entity<Scheduling.Attendance>().Property(a => a.Status).HasConversion<int>();
        b.Entity<Scheduling.Attendance>()
            .Property(a => a.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.Entity<Scheduling.Attendance>()
            .HasOne(a => a.LessonLog).WithMany(l => l.Attendances)
            .HasForeignKey(a => a.LessonLogId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.Attendance>()
            .HasOne(a => a.Client).WithMany()
            .HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Scheduling.Attendance>()
            .HasIndex(a => new { a.LessonLogId, a.ClientId }).IsUnique();
        b.Entity<Scheduling.Attendance>()
            .HasIndex(a => a.IdempotencyKey).IsUnique();

        // ── OPERATIONS ───────────────────────────────────────────────────────
        // NOT IArchivable — no query filters on any Operations entity.

        // ExtraHours
        b.Entity<Operations.ExtraHours>().Property(e => e.Status).HasConversion<int>();
        b.Entity<Operations.ExtraHours>().Property(e => e.Hours).HasPrecision(5, 2);
        b.Entity<Operations.ExtraHours>().Property(e => e.Reason).HasMaxLength(500).IsRequired();
        b.Entity<Operations.ExtraHours>()
            .HasOne(e => e.ShiftInstance).WithMany()
            .HasForeignKey(e => e.ShiftInstanceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Operations.ExtraHours>()
            .HasOne(e => e.Instructor).WithMany()
            .HasForeignKey(e => e.InstructorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Operations.ExtraHours>()
            .HasOne(e => e.ApprovedByUser).WithMany()
            .HasForeignKey(e => e.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);

        // IncidentReport
        b.Entity<Operations.IncidentReport>().Property(r => r.Severity).HasConversion<int>();
        b.Entity<Operations.IncidentReport>().Property(r => r.Description).HasMaxLength(2000).IsRequired();
        b.Entity<Operations.IncidentReport>()
            .HasOne(r => r.ShiftInstance).WithMany()
            .HasForeignKey(r => r.ShiftInstanceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Operations.IncidentReport>()
            .HasOne(r => r.Instructor).WithMany()
            .HasForeignKey(r => r.InstructorId).OnDelete(DeleteBehavior.Restrict);

        // VacationRequest
        b.Entity<Operations.VacationRequest>().Property(v => v.Status).HasConversion<int>();
        b.Entity<Operations.VacationRequest>().Property(v => v.Reason).HasMaxLength(500);
        b.Entity<Operations.VacationRequest>().Property(v => v.AdminNote).HasMaxLength(500);
        b.Entity<Operations.VacationRequest>()
            .HasOne(v => v.Instructor).WithMany()
            .HasForeignKey(v => v.InstructorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Operations.VacationRequest>()
            .HasOne(v => v.ApprovedByUser).WithMany()
            .HasForeignKey(v => v.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);

        // ── LOGISTICS ────────────────────────────────────────────────────────
        // NOT IArchivable — no query filter.

        b.Entity<Logistics.LogisticsOrder>().Property(o => o.Status).HasConversion<int>();
        b.Entity<Logistics.LogisticsOrder>().Property(o => o.DisputeNotes).HasMaxLength(500);

        b.Entity<Logistics.LogisticsOrder>()
            .HasOne(o => o.Class).WithMany()
            .HasForeignKey(o => o.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Logistics.LogisticsOrder>()
            .HasOne(o => o.Model).WithMany()
            .HasForeignKey(o => o.ModelId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Logistics.LogisticsOrder>()
            .HasIndex(o => new { o.ClassId, o.ModelId });

        // ── ACTION HUB ───────────────────────────────────────────────────────
        // NOT IArchivable — no query filter on ActionItem.

        // ActionItem enum conversions
        b.Entity<ActionHub.ActionItem>().Property(a => a.Type).HasConversion<int>();
        b.Entity<ActionHub.ActionItem>().Property(a => a.Status).HasConversion<int>();

        // ActionItem string lengths
        b.Entity<ActionHub.ActionItem>().Property(a => a.AssignedToRole).HasMaxLength(50);
        b.Entity<ActionHub.ActionItem>().Property(a => a.Description).HasMaxLength(1000).IsRequired();
        b.Entity<ActionHub.ActionItem>().Property(a => a.DeduplicationKey).HasMaxLength(200);

        // ActionItem FK → AspNetUsers (Restrict — user delete must be blocked)
        b.Entity<ActionHub.ActionItem>()
            .HasOne(a => a.AssignedToUser).WithMany()
            .HasForeignKey(a => a.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);

        // Partial unique index on DeduplicationKey: only non-null values must be unique
        b.Entity<ActionHub.ActionItem>()
            .HasIndex(a => a.DeduplicationKey)
            .HasFilter("\"DeduplicationKey\" IS NOT NULL")
            .IsUnique();

        // ── OUTBOX ───────────────────────────────────────────────────────────
        // NOT BaseEntity — infrastructure; audit interceptor does NOT touch OutboxEvent.

        b.Entity<OutboxEvent>().Property(e => e.EventType).HasMaxLength(100).IsRequired();
        b.Entity<OutboxEvent>().Property(e => e.Payload).IsRequired();
    }
}
