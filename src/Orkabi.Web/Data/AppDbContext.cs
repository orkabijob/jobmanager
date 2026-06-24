using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;
using Curriculum = Orkabi.Web.Modules.Curriculum;
using People = Orkabi.Web.Modules.People;

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
    }
}
