using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using SmartSchedulingSystem.Models;

namespace SmartSchedulingSystem.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Student> Students { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Instructor> Instructors { get; set; }
        public DbSet<Section> Sections { get; set; }
        public DbSet<DayGroupSection> DayGroupSections { get; set; }
        public DbSet<StudentCourse> StudentCourses { get; set; }
        public DbSet<StudentRemainingCourse> StudentRemainingCourses { get; set; }
        public DbSet<Filter> Filters { get; set; }
        public DbSet<DayGroupFilter> DayGroupFilters { get; set; }
        public DbSet<InstructorAdded> InstructorsAdded { get; set; }
        public DbSet<GeneratedSchedule> GeneratedSchedules { get; set; }
        public DbSet<GeneratedSection> GeneratedSections { get; set; }
        public DbSet<Favourite> Favourites { get; set; }
        public DbSet<StudentGrade> StudentGrades { get; set; }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            // ── Section ───────────────────────────────────────
            mb.Entity<Section>()
                .HasOne(s => s.Course).WithMany(c => c.Sections).HasForeignKey(s => s.CId);
            mb.Entity<Section>()
                .HasOne(s => s.Instructor).WithMany(i => i.Sections).HasForeignKey(s => s.IId);
            mb.Entity<Section>()
            .Property(s => s.SectionNo)
            .HasColumnName("SECTION_NO");

            // ── StudentGrade ───────────────────────────────
            mb.Entity<StudentGrade>()
            .HasOne(x => x.Student)
            .WithMany(x => x.StudentGrades)
            .HasForeignKey(x => x.StId);

            // ── DayGroupSection ───────────────────────────────
            mb.Entity<DayGroupSection>().HasKey(d => new { d.SecId, d.Day });
            mb.Entity<DayGroupSection>()
                .HasOne(d => d.Section).WithMany(s => s.DayGroupSections).HasForeignKey(d => d.SecId);
            mb.Entity<DayGroupSection>()
                .HasOne(d => d.Course).WithMany(c => c.DayGroupSections).HasForeignKey(d => d.CId);

            // ── StudentCourse ─────────────────────────────────
            mb.Entity<StudentRemainingCourse>().HasKey(sr => new { sr.StId, sr.CId });
            mb.Entity<StudentRemainingCourse>()
                .HasOne(sr => sr.Student).WithMany(s => s.RemainingCourses).HasForeignKey(sr => sr.StId);
            mb.Entity<StudentRemainingCourse>()
                .HasOne(sr => sr.Course).WithMany(c => c.StudentRemainingCourses).HasForeignKey(sr => sr.CId);

            mb.Entity<StudentCourse>().HasKey(sc => new { sc.StId, sc.CId });
            mb.Entity<StudentCourse>()
                .HasOne(sc => sc.Student).WithMany(s => s.StudentCourses).HasForeignKey(sc => sc.StId);
            mb.Entity<StudentCourse>()
                .HasOne(sc => sc.Course).WithMany(c => c.StudentCourses).HasForeignKey(sc => sc.CId);

            // ── Filter ────────────────────────────────────────
            // Must explicitly map FK so EF doesn't invent "StudentStId"
            mb.Entity<Filter>()
                .HasOne(f => f.Student).WithMany(s => s.Filters).HasForeignKey(f => f.StId);

            // ── DayGroupFilter ────────────────────────────────
            mb.Entity<DayGroupFilter>().HasKey(d => new { d.FId, d.Day });
            mb.Entity<DayGroupFilter>()
                .HasOne(d => d.Filter).WithMany(f => f.DayGroupFilters).HasForeignKey(d => d.FId);

            // ── InstructorAdded ───────────────────────────────
            mb.Entity<InstructorAdded>().HasKey(ia => new { ia.IId, ia.CId, ia.StId });
            mb.Entity<InstructorAdded>()
                .HasOne(ia => ia.Instructor).WithMany(i => i.InstructorsAdded).HasForeignKey(ia => ia.IId);
            mb.Entity<InstructorAdded>()
                .HasOne(ia => ia.Course).WithMany(c => c.InstructorsAdded).HasForeignKey(ia => ia.CId);
            mb.Entity<InstructorAdded>()
                .HasOne(ia => ia.Student).WithMany(s => s.InstructorsAdded).HasForeignKey(ia => ia.StId);

            // ── GeneratedSchedule ─────────────────────────────
            // Must explicitly map FK so EF doesn't invent "FilterFId"
            mb.Entity<GeneratedSchedule>()
                .HasOne(g => g.Filter).WithMany(f => f.GeneratedSchedules).HasForeignKey(g => g.FId);

            // ── GeneratedSection ──────────────────────────────
            mb.Entity<GeneratedSection>().HasKey(gs => new { gs.SecId, gs.SchedId });
            mb.Entity<GeneratedSection>()
                .HasOne(gs => gs.Section).WithMany(s => s.GeneratedSections).HasForeignKey(gs => gs.SecId);
            mb.Entity<GeneratedSection>()
                .HasOne(gs => gs.GeneratedSchedule).WithMany(g => g.GeneratedSections).HasForeignKey(gs => gs.SchedId);
            mb.Entity<GeneratedSection>()
                .HasOne(gs => gs.Course).WithMany(c => c.GeneratedSections).HasForeignKey(gs => gs.CId);

            // ── Favourite ─────────────────────────────────────
            mb.Entity<Favourite>()
                .HasIndex(f => new { f.FId, f.SchedId }).IsUnique();
            mb.Entity<Favourite>()
                .HasOne(f => f.Filter).WithMany(fi => fi.Favourites).HasForeignKey(f => f.FId);
            mb.Entity<Favourite>()
                .HasOne(f => f.GeneratedSchedule).WithMany(g => g.Favourites).HasForeignKey(f => f.SchedId);

            // Fix decimal precision warning for AvgGpa
            mb.Entity<Course>().Property(c => c.AvgGpa).HasPrecision(4, 2);

            // All PKs are set explicitly via raw SQL inserts using NEXTVAL.
            // Mark as ValueGeneratedNever so EF Core never interferes with the ID.
            mb.Entity<Filter>().Property(f => f.FId).ValueGeneratedNever();
            mb.Entity<GeneratedSchedule>().Property(g => g.SchedId).ValueGeneratedNever();
            mb.Entity<Favourite>().Property(f => f.FavId).ValueGeneratedNever();

            // FAV_ORDER — renamed to avoid Oracle reserved word ORDER
        }
    }
}