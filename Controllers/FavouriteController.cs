using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FavouriteController : BaseController
    {
        private readonly ISchedulerService _svc;
        public FavouriteController(ISchedulerService svc) => _svc = svc;

        // GET /api/favourite/all  — all favourites for the student across all filters
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            var favs = await _svc.GetAllFavouritesAsync(studentId);
            return Ok(ApiResponse<List<FavouriteDto>>.Ok(favs));
        }

        // GET /api/favourite?filterId=3  — favourites for a specific filter
        [HttpGet]
        public async Task<IActionResult> GetByFilter([FromQuery] int filterId)
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            var favs = await _svc.GetFavouritesAsync(studentId, filterId);
            return Ok(ApiResponse<List<FavouriteDto>>.Ok(favs));
        }

        // POST /api/favourite/toggle
        [HttpPost("toggle")]
        public async Task<IActionResult> Toggle([FromBody] ToggleFavouriteRequest req)
        {
            var auth = RequireLogin(out var studentId);
            if (auth != null) return auth;

            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<ToggleResultDto>.Fail("Invalid request."));

            try
            {
                bool added = await _svc.ToggleFavouriteAsync(req, studentId);
                return Ok(ApiResponse<ToggleResultDto>.Ok(
                    new ToggleResultDto { Added = added },
                    added ? "Added to favourites." : "Removed from favourites."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<ToggleResultDto>.Fail(ex.Message));
            }
        }
    }
}