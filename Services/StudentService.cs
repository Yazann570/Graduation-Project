using Microsoft.EntityFrameworkCore;
using SmartSchedulingSystem.Data;
using SmartSchedulingSystem.Models;

namespace SmartSchedulingSystem.Services
{
    public interface IStudentService
    {
        Task<StudentDto?> GetStudentAsync(string studentId);
        Task<List<CurrentScheduleCourseDto>> GetCurrentScheduleAsync(string studentId);
    }

    public class StudentService : IStudentService
    {
        private readonly AppDbContext _db;
        public StudentService(AppDbContext db) => _db = db;

        public async Task<StudentDto?> GetStudentAsync(string studentId)
        {
            var s = await _db.Students.FindAsync(studentId);
            if (s == null) return null;
            return new StudentDto { StId = s.StId, Email = s.Email, Phone = s.PhoneNum };
        }

        public async Task<List<CurrentScheduleCourseDto>> GetCurrentScheduleAsync(string studentId)
        {
            // Get all courses enrolled by this student
            var enrolled = await _db.StudentCourses
                .Include(sc => sc.Course)
                .Where(sc => sc.StId == studentId)
                .ToListAsync();

            var courseIds = enrolled.Select(sc => sc.CId).ToList();

            // Get one section per course for time/instructor info
            var sections = await _db.Sections
                .Include(s => s.Instructor)
                .Include(s => s.DayGroupSections)
                .Where(s => courseIds.Contains(s.CId))
                .ToListAsync();

            // Group by course, take first section
            var sectionMap = sections
                .GroupBy(s => s.CId)
                .ToDictionary(g => g.Key, g => g.First());

            return enrolled.Select((sc, i) =>
            {
                sectionMap.TryGetValue(sc.CId, out var sec);
                return new CurrentScheduleCourseDto
                {
                    CourseNumber = sc.CId,
                    Title = sc.Course.CName,
                    Hours = sc.Course.CHrs,
                    SectionNum = i + 1,
                    Instructor = sec?.Instructor.IName ?? "TBA",
                    Classroom = sc.Course.IsOnline == "Y" ? "Online" : "TBA",
                    Days = sec != null
                        ? string.Join(" ", sec.DayGroupSections.Select(d => d.Day))
                        : "TBA",
                    StartTime = sec?.STime ?? "TBA",
                    EndTime = sec?.FTime ?? "TBA",
                    IsOnline = sc.Course.IsOnline,
                    Absences = 0,
                };
            }).ToList();
        }
    }
}