using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoursesController : BaseController
    {
        private readonly ISchedulerService _svc;
        private readonly ILogger<CoursesController> _logger;

        public CoursesController(ISchedulerService svc, ILogger<CoursesController> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        [HttpGet("remaining")]
        public async Task<IActionResult> GetRemaining()
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            var courses = await _svc.GetRemainingCoursesAsync(studentId);
            return Ok(ApiResponse<List<RemainingCourseDto>>.Ok(courses));
        }

        [HttpGet("selected")]
        public async Task<IActionResult> GetSelected()
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            var courses = await _svc.GetSelectedCoursesAsync(studentId);
            return Ok(ApiResponse<List<SelectedCourseDto>>.Ok(courses));
        }

        [HttpPost("selected")]
        public async Task<IActionResult> AddCourse([FromBody] AddCourseRequest req)
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<bool>.Fail("Invalid request."));

            _logger.LogInformation("AddCourse — Student: {S}, Course: {C}, Instructors: [{I}]",
                studentId, req.CourseId, string.Join(",", req.InstructorIds));
            try
            {
                await _svc.AddCourseAsync(studentId, req);
                return Ok(ApiResponse<bool>.Ok(true, "Course added."));
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = ex;
                while (cur != null) { msgs.Add(cur.GetType().Name + ": " + cur.Message); cur = cur.InnerException; }
                var full = string.Join(" --> ", msgs);
                _logger.LogError("AddCourse failed: {E}", full);
                return StatusCode(500, ApiResponse<bool>.Fail(full));
            }
        }

        [HttpDelete("selected/{courseId}")]
        public async Task<IActionResult> RemoveCourse(string courseId)
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            try
            {
                await _svc.RemoveCourseAsync(studentId, courseId);
                return Ok(ApiResponse<bool>.Ok(true, "Course removed."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<bool>.Fail(ex.Message));
            }
        }
    }
}