using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartSchedulingSystem.Models
{
    // ══════════════════════════════════════════════════════════
    //  ENTITIES
    // ══════════════════════════════════════════════════════════

    [Table("STUDENT")]
    public class Student
    {
        [Key]
        [Column("ST_ID")]
        [MaxLength(20)]
        public string StId { get; set; } = null!;

        [Column("S_EMAIL")]
        [MaxLength(100)]
        public string Email { get; set; } = null!;

        [Column("PHONE_NUM")]
        [MaxLength(20)]
        public string? PhoneNum { get; set; }

        public ICollection<StudentCourse> StudentCourses { get; set; } = new List<StudentCourse>();
        public ICollection<Filter> Filters { get; set; } = new List<Filter>();
        public ICollection<InstructorAdded> InstructorsAdded { get; set; } = new List<InstructorAdded>();
    }

    [Table("COURSE")]
    public class Course
    {
        [Key]
        [Column("C_ID")]
        [MaxLength(20)]
        public string CId { get; set; } = null!;

        [Column("C_NAME")]
        [MaxLength(200)]
        public string CName { get; set; } = null!;

        [Column("C_HRS")]
        public int CHrs { get; set; }

        [Column("AVG_GPA")]
        public decimal? AvgGpa { get; set; }

        // N = on-site | Y = online | B = blended
        [Column("IS_ONLINE")]
        [MaxLength(1)]
        public string IsOnline { get; set; } = "N";

        // CP | CS | CU | EU | EP
        [Column("C_TYPE")]
        [MaxLength(2)]
        public string CType { get; set; } = null!;

        public ICollection<Section> Sections { get; set; } = new List<Section>();
        public ICollection<StudentCourse> StudentCourses { get; set; } = new List<StudentCourse>();
        public ICollection<DayGroupSection> DayGroupSections { get; set; } = new List<DayGroupSection>();
        public ICollection<GeneratedSection> GeneratedSections { get; set; } = new List<GeneratedSection>();
        public ICollection<InstructorAdded> InstructorsAdded { get; set; } = new List<InstructorAdded>();
    }

    [Table("INSTRUCTOR")]
    public class Instructor
    {
        [Key]
        [Column("I_ID")]
        public int IId { get; set; }

        [Column("I_NAME")]
        [MaxLength(100)]
        public string IName { get; set; } = null!;

        public ICollection<Section> Sections { get; set; } = new List<Section>();
        public ICollection<InstructorAdded> InstructorsAdded { get; set; } = new List<InstructorAdded>();
    }

    [Table("SECTION")]
    public class Section
    {
        [Key]
        [Column("SEC_ID")]
        public int SecId { get; set; }

        [Column("S_TIME")][MaxLength(5)] public string STime { get; set; } = null!;  // "08:00"
        [Column("F_TIME")][MaxLength(5)] public string FTime { get; set; } = null!;  // "09:00"
        [Column("C_ID")][MaxLength(20)] public string CId { get; set; } = null!;
        [Column("I_ID")] public int IId { get; set; }

        public Course Course { get; set; } = null!;
        public Instructor Instructor { get; set; } = null!;
        public ICollection<DayGroupSection> DayGroupSections { get; set; } = new List<DayGroupSection>();
        public ICollection<GeneratedSection> GeneratedSections { get; set; } = new List<GeneratedSection>();
    }

    [Table("DAY_GROUP_SECTION")]
    public class DayGroupSection
    {
        [Column("SEC_ID")] public int SecId { get; set; }
        [Column("DAY")][MaxLength(3)] public string Day { get; set; } = null!;
        [Column("C_ID")][MaxLength(20)] public string CId { get; set; } = null!;
        public Section Section { get; set; } = null!;
        public Course Course { get; set; } = null!;
    }

    [Table("STUDENT_COURSE")]
    public class StudentCourse
    {
        [Column("ST_ID")][MaxLength(20)] public string StId { get; set; } = null!;
        [Column("C_ID")][MaxLength(20)] public string CId { get; set; } = null!;
        public Student Student { get; set; } = null!;
        public Course Course { get; set; } = null!;
    }

    [Table("FILTER")]
    public class Filter
    {
        [Key]
        [Column("F_ID")]
        public int FId { get; set; }

        [Column("ST_ID")][MaxLength(20)] public string StId { get; set; } = null!;
        [Column("S_TIME")][MaxLength(5)] public string STime { get; set; } = "08:00";
        [Column("F_TIME")][MaxLength(5)] public string FTime { get; set; } = "17:00";
        [Column("MIN_BREAK")] public int MinBreak { get; set; } = 0;
        [Column("MAX_BREAK")] public int MaxBreak { get; set; } = 120;

        public Student Student { get; set; } = null!;
        public ICollection<DayGroupFilter> DayGroupFilters { get; set; } = new List<DayGroupFilter>();
        public ICollection<GeneratedSchedule> GeneratedSchedules { get; set; } = new List<GeneratedSchedule>();
        public ICollection<Favourite> Favourites { get; set; } = new List<Favourite>();
    }

    [Table("DAY_GROUP_FILTER")]
    public class DayGroupFilter
    {
        [Column("F_ID")] public int FId { get; set; }
        [Column("DAY")][MaxLength(3)] public string Day { get; set; } = null!;
        public Filter Filter { get; set; } = null!;
    }

    [Table("INSTRUCTOR_ADDED")]
    public class InstructorAdded
    {
        [Column("I_ID")] public int IId { get; set; }
        [Column("C_ID")][MaxLength(20)] public string CId { get; set; } = null!;
        [Column("ST_ID")][MaxLength(20)] public string StId { get; set; } = null!;
        public Instructor Instructor { get; set; } = null!;
        public Course Course { get; set; } = null!;
        public Student Student { get; set; } = null!;
    }

    [Table("GENERATED_SCHEDULE")]
    public class GeneratedSchedule
    {
        [Key]
        [Column("SCHED_ID")]
        public int SchedId { get; set; }

        [Column("F_ID")] public int FId { get; set; }
        [Column("CREATION_DATE")] public DateTime CreationDate { get; set; } = DateTime.UtcNow;

        public Filter Filter { get; set; } = null!;
        public ICollection<GeneratedSection> GeneratedSections { get; set; } = new List<GeneratedSection>();
        public ICollection<Favourite> Favourites { get; set; } = new List<Favourite>();
    }

    [Table("GENERATED_SECTION")]
    public class GeneratedSection
    {
        [Column("SEC_ID")] public int SecId { get; set; }
        [Column("SCHED_ID")] public int SchedId { get; set; }
        [Column("C_ID")][MaxLength(20)] public string CId { get; set; } = null!;
        public Section Section { get; set; } = null!;
        public GeneratedSchedule GeneratedSchedule { get; set; } = null!;
        public Course Course { get; set; } = null!;
    }

    [Table("FAVOURITE")]
    public class Favourite
    {
        [Key]
        [Column("FAV_ID")]
        public int FavId { get; set; }

        [Column("F_ID")] public int FId { get; set; }
        [Column("SCHED_ID")] public int SchedId { get; set; }

        public Filter Filter { get; set; } = null!;
        public GeneratedSchedule GeneratedSchedule { get; set; } = null!;
    }

    // ══════════════════════════════════════════════════════════
    //  DTOs  (shapes the API controllers send/receive)
    // ══════════════════════════════════════════════════════════

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public T? Data { get; set; }

        public static ApiResponse<T> Ok(T data, string msg = "Success") =>
            new() { Success = true, Message = msg, Data = data };
        public static ApiResponse<T> Fail(string msg) =>
            new() { Success = false, Message = msg };
    }

    public class StudentDto
    {
        public string StId { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
    }

    public class RemainingCourseDto
    {
        public string CId { get; set; } = null!;
        public string CName { get; set; } = null!;
        public int CHrs { get; set; }
        public string CType { get; set; } = null!;
        public string RequirementLabel { get; set; } = null!;
        public string IsOnline { get; set; } = "N";
        public List<InstructorSummaryDto> AvailableInstructors { get; set; } = new();
    }

    public class InstructorSummaryDto
    {
        public int IId { get; set; }
        public string IName { get; set; } = null!;
    }

    public class CurrentScheduleCourseDto
    {
        public string CourseNumber { get; set; } = null!;
        public string Title { get; set; } = null!;
        public int Hours { get; set; }
        public int SectionNum { get; set; }
        public string Instructor { get; set; } = null!;
        public string Classroom { get; set; } = null!;
        public string Days { get; set; } = null!;
        public string StartTime { get; set; } = null!;
        public string EndTime { get; set; } = null!;
        public string IsOnline { get; set; } = "N";
        public int Absences { get; set; }
    }

    public class CreateFilterRequest
    {
        [Required] public string StartTime { get; set; } = "08:00";
        [Required] public string EndTime { get; set; } = "17:00";
        public int MinBreak { get; set; } = 0;
        public int MaxBreak { get; set; } = 120;
        [Required] public List<string> Days { get; set; } = new();
        [Required] public Dictionary<string, List<int>> CourseInstructors { get; set; } = new();
    }

    public class FilterDto
    {
        public int FId { get; set; }
        public string StartTime { get; set; } = null!;
        public string EndTime { get; set; } = null!;
        public int MinBreak { get; set; }
        public int MaxBreak { get; set; }
        public List<string> Days { get; set; } = new();
    }

    public class GenerateSchedulesRequest
    {
        [Required] public int FilterId { get; set; }
    }

    public class ScheduleResultDto
    {
        public int SchedId { get; set; }
        public int TotalHours { get; set; }
        public bool IsFavourite { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<ScheduledCourseDto> Courses { get; set; } = new();
    }

    public class ScheduledCourseDto
    {
        public string CourseNumber { get; set; } = null!;
        public string CourseName { get; set; } = null!;
        public string CType { get; set; } = null!;
        public string RequirementLabel { get; set; } = null!;
        public int SectionNum { get; set; }
        public string InstructorName { get; set; } = null!;
        public string StartTime { get; set; } = null!;
        public string EndTime { get; set; } = null!;
        public string Days { get; set; } = null!;
        public int Hours { get; set; }
    }

    public class ToggleFavouriteRequest
    {
        [Required] public int FilterId { get; set; }
        [Required] public int ScheduleId { get; set; }
    }

    public class FavouriteDto
    {
        public int FavId { get; set; }
        public int SchedId { get; set; }
        public ScheduleResultDto Schedule { get; set; } = null!;
    }

    public class ToggleResultDto
    {
        public bool Added { get; set; }
    }
}