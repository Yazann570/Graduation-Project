using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchedulerController : ControllerBase
    {
        private readonly ISchedulerService _svc;
        private readonly string _studentId;
        private readonly ILogger<SchedulerController> _logger;

        public SchedulerController(ISchedulerService svc, IConfiguration config,
            ILogger<SchedulerController> logger)
        {
            _svc = svc;
            _studentId = config["AppSettings:StudentId"]!;
            _logger = logger;
        }

        // POST /api/scheduler/generate
        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] GenerateSchedulesRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<List<ScheduleResultDto>>.Fail("Invalid request."));

            // Log to terminal so we can see exactly what values are used
            _logger.LogInformation("Generate called — FilterId: {FilterId}, StudentId: {StudentId}",
                req.FilterId, _studentId);

            try
            {
                var schedules = await _svc.GenerateSchedulesAsync(req.FilterId, _studentId);
                var msg = schedules.Count == 0
                    ? "No conflict-free schedules found. Try relaxing your filters."
                    : $"{schedules.Count} schedule(s) generated.";
                return Ok(ApiResponse<List<ScheduleResultDto>>.Ok(schedules, msg));
            }
            catch (Exception ex)
            {
                // Walk the full exception chain and log every level
                var messages = new List<string>();
                var current = ex;
                while (current != null)
                {
                    messages.Add(current.GetType().Name + ": " + current.Message);
                    _logger.LogError("  Exception level: {Msg}", current.Message);
                    current = current.InnerException;
                }
                var fullMessage = string.Join(" --> ", messages);
                _logger.LogError("Full error: {FullMessage}", fullMessage);

                return StatusCode(500, ApiResponse<List<ScheduleResultDto>>.Fail(fullMessage));
            }
        }
    }
}