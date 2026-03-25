using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoursesController : ControllerBase
    {
        private readonly ISchedulerService _svc;
        private readonly string _studentId;

        public CoursesController(ISchedulerService svc, IConfiguration config)
        {
            _svc = svc;
            _studentId = config["AppSettings:StudentId"]!;
        }

        // GET /api/courses/remaining
        [HttpGet("remaining")]
        public async Task<IActionResult> GetRemaining()
        {
            var courses = await _svc.GetRemainingCoursesAsync(_studentId);
            return Ok(ApiResponse<List<RemainingCourseDto>>.Ok(courses));
        }
    }
}