using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchedulingSystem.Data;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;
using System.Diagnostics;
namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchedulerController : BaseController
    {
        private readonly ISchedulerService _svc;
        private readonly GeminiScheduleRanker _geminiRanker;
        private readonly ILogger<SchedulerController> _logger;
        private readonly AppDbContext _context;
        public SchedulerController(
            ISchedulerService svc,
            GeminiScheduleRanker geminiRanker,AppDbContext context,
            ILogger<SchedulerController> logger)
        {
            _svc = svc;
            _geminiRanker = geminiRanker;
            _context = context;
            _logger = logger;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] GenerateSchedulesRequest req)
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<List<ScheduleResultDto>>.Fail("Invalid request."));

            _logger.LogInformation("Generate — FilterId: {F}, Student: {S}", req.FilterId, studentId);

            try
            {
                var totalWatch = Stopwatch.StartNew();
                var generateWatch = Stopwatch.StartNew();
                var schedules = await _svc.GenerateSchedulesAsync(req.FilterId, studentId);
                generateWatch.Stop();
                Console.WriteLine($"TIME - GenerateSchedulesAsync: {generateWatch.ElapsedMilliseconds} ms");
                Console.WriteLine($"COUNT - Generated schedules: {schedules.Count}");
                if (schedules.Count > 0)
                {
                    var dbWatch = Stopwatch.StartNew();
                    var student = await _context.Students.FirstOrDefaultAsync(s => s.StId == studentId);
                    if (student == null)
                    {
                        return BadRequest("Student not found.");
                    }
                    var studentGrades = await _context.StudentGrades
                    .Where(g => g.StId == studentId)
                    .Select(g => new
                    {
                        g.CourseName,
                        g.CourseGpa,
                        g.PassFlag
                    })
                    .ToListAsync();
                    dbWatch.Stop();
                    Console.WriteLine($"TIME - Load student + grades: {dbWatch.ElapsedMilliseconds} ms");
                    Console.WriteLine($"COUNT - Student grades: {studentGrades.Count}");
                    var requestData = new
                    {
                        StudentId = studentId,
                        GPA = student.Gpa, // from STUDENT table
                        CompletedCourses = studentGrades,
                        Schedules = schedules
                    };
                    var geminiWatch = Stopwatch.StartNew();
                    var sortedIds = await _geminiRanker.RankSchedulesAsync(requestData);
                    geminiWatch.Stop(); Console.WriteLine($"TIME - Gemini ranking: {geminiWatch.ElapsedMilliseconds} ms");
                    var sortedSchedules = sortedIds
                        .Select(id => schedules.FirstOrDefault(s => s.SchedId == id))
                        .OfType<ScheduleResultDto>()
                        .ToList();

                    var remainingSchedules = schedules
                        .Where(s => !sortedIds.Contains(s.SchedId))
                        .ToList();

                    schedules = sortedSchedules!
                        .Concat(remainingSchedules)
                        .ToList();
                }

                var msg = schedules.Count == 0
                    ? "No conflict-free schedules found. Try relaxing your filters."
                    : $"{schedules.Count} schedule(s) generated.";
                totalWatch.Stop();
                Console.WriteLine($"TIME - TOTAL API: {totalWatch.ElapsedMilliseconds} ms");
                return Ok(ApiResponse<List<ScheduleResultDto>>.Ok(schedules, msg));
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = ex;

                while (cur != null)
                {
                    msgs.Add(cur.GetType().Name + ": " + cur.Message);
                    cur = cur.InnerException;
                }

                var full = string.Join(" --> ", msgs);
                _logger.LogError("Generate failed: {E}", full);

                return StatusCode(500, ApiResponse<List<ScheduleResultDto>>.Fail(full));
            }
        }
    }
}