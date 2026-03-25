using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FilterController : ControllerBase
    {
        private readonly ISchedulerService _svc;
        private readonly string _studentId;

        public FilterController(ISchedulerService svc, IConfiguration config)
        {
            _svc = svc;
            _studentId = config["AppSettings:StudentId"]!;
        }

        // GET /api/filter  — returns all saved filters for the student
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var filters = await _svc.GetAllFiltersAsync(_studentId);
            return Ok(ApiResponse<List<FilterDto>>.Ok(filters));
        }

        // POST /api/filter
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFilterRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<FilterDto>.Fail("Invalid request."));

            try
            {
                var filter = await _svc.SaveFilterAsync(_studentId, req);
                return Ok(ApiResponse<FilterDto>.Ok(filter, "Filter saved."));
            }
            catch (Exception ex)
            {
                var messages = new List<string>();
                var current = ex;
                while (current != null)
                {
                    messages.Add(current.GetType().Name + ": " + current.Message);
                    current = current.InnerException;
                }
                return StatusCode(500, ApiResponse<FilterDto>.Fail(string.Join(" --> ", messages)));
            }
        }
    }
}