using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudentController : BaseController
    {
        private readonly IStudentService _svc;
        public StudentController(IStudentService svc) => _svc = svc;

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            var student = await _svc.GetStudentAsync(studentId);
            if (student == null)
                return NotFound(ApiResponse<StudentDto>.Fail("Student not found."));
            return Ok(ApiResponse<StudentDto>.Ok(student));
        }

        [HttpGet("current-schedule")]
        public async Task<IActionResult> GetCurrentSchedule()
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            var schedule = await _svc.GetCurrentScheduleAsync(studentId);
            return Ok(ApiResponse<List<CurrentScheduleCourseDto>>.Ok(schedule));
        }
    }
}