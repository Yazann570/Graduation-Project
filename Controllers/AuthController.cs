using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchedulingSystem.Data;
using SmartSchedulingSystem.Models;
using LoginRequest = SmartSchedulingSystem.Models.LoginRequest;

namespace SmartSchedulingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;

        // Only these 3 student IDs are allowed to log in
        private static readonly HashSet<string> AllowedStudents = new()
        {
            "20210001",
            "20220919",
            "20220187",
        };

        public AuthController(AppDbContext db) => _db = db;

        // POST /api/auth/login  — body: { "StudentId": "20210001" }
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.StudentId))
                return BadRequest(ApiResponse<object>.Fail("Please enter a student ID."));

            var id = req.StudentId.Trim();

            // Check against allowed list
            if (!AllowedStudents.Contains(id))
                return Unauthorized(ApiResponse<object>.Fail("Student ID not recognised."));

            // Check student exists in DB
            var student = await _db.Students.FindAsync(id);
            if (student == null)
                return Unauthorized(ApiResponse<object>.Fail(
                    $"Student '{id}' not found in the database. Make sure seed data was run."));

            // Store in session
            HttpContext.Session.SetString("StudentId", id);

            return Ok(ApiResponse<StudentDto>.Ok(new StudentDto
            {
                StId = student.StId,
                Email = student.Email,
                Phone = student.PhoneNum,
            }, "Login successful."));
        }

        // POST /api/auth/logout
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return Ok(ApiResponse<object>.Ok(null!, "Logged out."));
        }

        // GET /api/auth/me  — returns current session student or 401
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var id = HttpContext.Session.GetString("StudentId");
            if (string.IsNullOrEmpty(id))
                return Unauthorized(ApiResponse<object>.Fail("Not logged in."));

            var student = await _db.Students.FindAsync(id);
            if (student == null)
                return Unauthorized(ApiResponse<object>.Fail("Session invalid."));

            return Ok(ApiResponse<StudentDto>.Ok(new StudentDto
            {
                StId = student.StId,
                Email = student.Email,
                Phone = student.PhoneNum,
            }));
        }
    }
}