using Microsoft.AspNetCore.Mvc;
using SmartSchedulingSystem.Models;

namespace SmartSchedulingSystem.Controllers
{
    /// <summary>
    /// All API controllers inherit this.
    /// Provides StudentId from session and returns 401 if not logged in.
    /// </summary>
    public abstract class BaseController : ControllerBase
    {
        protected string? GetStudentId()
            => HttpContext.Session.GetString("StudentId");

        /// <summary>
        /// Returns the student ID from session, or writes a 401 response and returns null.
        /// Usage:  var id = AuthenticatedStudentId(); if (id == null) return;
        /// </summary>
        protected IActionResult? RequireLogin(out string studentId)
        {
            var id = HttpContext.Session.GetString("StudentId");
            if (string.IsNullOrEmpty(id))
            {
                studentId = "";
                return Unauthorized(ApiResponse<object>.Fail("Not logged in."));
            }
            studentId = id;
            return null;
        }
    }
}