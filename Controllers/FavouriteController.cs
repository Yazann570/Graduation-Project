using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FavouriteController : ControllerBase
    {
        private readonly ISchedulerService _svc;
        private readonly string _studentId;

        public FavouriteController(ISchedulerService svc, IConfiguration config)
        {
            _svc = svc;
            _studentId = config["AppSettings:StudentId"]!;
        }

        // GET /api/favourite?filterId=3
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int filterId)
        {
            var favs = await _svc.GetFavouritesAsync(_studentId, filterId);
            return Ok(ApiResponse<List<FavouriteDto>>.Ok(favs));
        }

        // POST /api/favourite/toggle
        [HttpPost("toggle")]
        public async Task<IActionResult> Toggle([FromBody] ToggleFavouriteRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<ToggleResultDto>.Fail("Invalid request."));

            try
            {
                bool added = await _svc.ToggleFavouriteAsync(req, _studentId);
                return Ok(ApiResponse<ToggleResultDto>.Ok(
                    new ToggleResultDto { Added = added },
                    added ? "Added to favourites." : "Removed from favourites."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<ToggleResultDto>.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<ToggleResultDto>.Fail(ex.Message));
            }
        }
    }
}