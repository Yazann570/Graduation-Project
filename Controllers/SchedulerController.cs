using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchedulerController : BaseController
    {
        private readonly ISchedulerService _svc;
        private readonly ILogger<SchedulerController> _logger;

        public SchedulerController(ISchedulerService svc, ILogger<SchedulerController> logger)
        {
            _svc = svc;
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
                var schedules = await _svc.GenerateSchedulesAsync(req.FilterId, studentId);
                var msg = schedules.Count == 0
                    ? "No conflict-free schedules found. Try relaxing your filters."
                    : $"{schedules.Count} schedule(s) generated.";
                return Ok(ApiResponse<List<ScheduleResultDto>>.Ok(schedules, msg));
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = ex;
                while (cur != null) { msgs.Add(cur.GetType().Name + ": " + cur.Message); cur = cur.InnerException; }
                var full = string.Join(" --> ", msgs);
                _logger.LogError("Generate failed: {E}", full);
                return StatusCode(500, ApiResponse<List<ScheduleResultDto>>.Fail(full));
            }
        }
    }
}