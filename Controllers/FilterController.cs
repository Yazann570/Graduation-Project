using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FilterController : BaseController
    {
        private readonly ISchedulerService _svc;
        public FilterController(ISchedulerService svc) => _svc = svc;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            var filters = await _svc.GetAllFiltersAsync(studentId);
            return Ok(ApiResponse<List<FilterDto>>.Ok(filters));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFilterRequest req)
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<FilterDto>.Fail("Invalid request."));

            try
            {
                var filter = await _svc.SaveFilterAsync(studentId, req);
                return Ok(ApiResponse<FilterDto>.Ok(filter, "Filter saved."));
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = ex;
                while (cur != null) { msgs.Add(cur.GetType().Name + ": " + cur.Message); cur = cur.InnerException; }
                return StatusCode(500, ApiResponse<FilterDto>.Fail(string.Join(" --> ", msgs)));
            }
        }
    }
}