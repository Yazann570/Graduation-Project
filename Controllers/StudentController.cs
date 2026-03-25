using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudentController : ControllerBase
    {
        private readonly IStudentService _svc;
        private readonly string _studentId;

        public StudentController(IStudentService svc, IConfiguration config)
        {
            _svc = svc;
            _studentId = config["AppSettings:StudentId"]
                ?? throw new InvalidOperationException("AppSettings:StudentId not set in appsettings.json");
        }

        // GET /api/student/me
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var student = await _svc.GetStudentAsync(_studentId);
            if (student == null)
                return NotFound(ApiResponse<StudentDto>.Fail("Student not found."));
            return Ok(ApiResponse<StudentDto>.Ok(student));
        }

        // GET /api/student/current-schedule
        [HttpGet("current-schedule")]
        public async Task<IActionResult> GetCurrentSchedule()
        {
            var schedule = await _svc.GetCurrentScheduleAsync(_studentId);
            return Ok(ApiResponse<List<CurrentScheduleCourseDto>>.Ok(schedule));
        }
    }
}