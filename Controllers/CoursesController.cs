using Microsoft.AspNetCore.Mvc;
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
        private readonly ILogger<CoursesController> _logger;

        public CoursesController(ISchedulerService svc, IConfiguration config,
            ILogger<CoursesController> logger)
        {
            _svc = svc;
            _studentId = config["AppSettings:StudentId"]!;
            _logger = logger;
        }

        // GET /api/courses/remaining
        [HttpGet("remaining")]
        public async Task<IActionResult> GetRemaining()
        {
            var courses = await _svc.GetRemainingCoursesAsync(_studentId);
            return Ok(ApiResponse<List<RemainingCourseDto>>.Ok(courses));
        }

        // GET /api/courses/selected
        [HttpGet("selected")]
        public async Task<IActionResult> GetSelected()
        {
            var courses = await _svc.GetSelectedCoursesAsync(_studentId);
            return Ok(ApiResponse<List<SelectedCourseDto>>.Ok(courses));
        }

        // POST /api/courses/selected
        [HttpPost("selected")]
        public async Task<IActionResult> AddCourse([FromBody] AddCourseRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<bool>.Fail("Invalid request."));

            _logger.LogInformation("AddCourse called — CourseId: {CourseId}, InstructorIds: [{Ids}]",
                req.CourseId, string.Join(",", req.InstructorIds));

            try
            {
                await _svc.AddCourseAsync(_studentId, req);
                _logger.LogInformation("AddCourse succeeded for {CourseId}", req.CourseId);
                return Ok(ApiResponse<bool>.Ok(true, "Course added."));
            }
            catch (Exception ex)
            {
                // Walk full chain
                var msgs = new List<string>();
                var cur = ex;
                while (cur != null) { msgs.Add(cur.GetType().Name + ": " + cur.Message); cur = cur.InnerException; }
                var full = string.Join(" --> ", msgs);
                _logger.LogError("AddCourse failed: {Error}", full);
                return StatusCode(500, ApiResponse<bool>.Fail(full));
            }
        }

        // DELETE /api/courses/selected/{courseId}
        [HttpDelete("selected/{courseId}")]
        public async Task<IActionResult> RemoveCourse(string courseId)
        {
            _logger.LogInformation("RemoveCourse called — CourseId: {CourseId}", courseId);
            try
            {
                await _svc.RemoveCourseAsync(_studentId, courseId);
                return Ok(ApiResponse<bool>.Ok(true, "Course removed."));
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = ex;
                while (cur != null) { msgs.Add(cur.GetType().Name + ": " + cur.Message); cur = cur.InnerException; }
                var full = string.Join(" --> ", msgs);
                _logger.LogError("RemoveCourse failed: {Error}", full);
                return StatusCode(500, ApiResponse<bool>.Fail(full));
            }
        }
    }
}