using SpeechLiftWebAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace SpeechLiftWebAPI.Controllers
{
    // ── Request Models ────────────────────────────────────────────────────────

    public class CompleteExerciseRequest
    {
        public string? FirebaseUid { get; set; }
        public string? ExerciseType { get; set; }  // "sound" | "word" | "sentence"
    }

    public class UpdateStreakRequest
    {
        public string? FirebaseUid { get; set; }
        public int Streak { get; set; }
    }

    // ── Controller ────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/user")]
    public class UserController : ControllerBase
    {
        private readonly FirebaseService _firebase;

        public UserController(FirebaseService firebase)
        {
            _firebase = firebase;
        }

        /// <summary>
        /// GET /api/user/profile?firebaseUid=xxx
        /// Returns full user profile: frequency level, streak, exercise completion.
        /// </summary>
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile([FromQuery] string? firebaseUid)
        {
            if (string.IsNullOrWhiteSpace(firebaseUid))
                return BadRequest(new { message = "firebaseUid is required." });

            var (success, profile) = await _firebase.GetUserProfileAsync(firebaseUid);

            if (!success || profile == null)
                return NotFound(new { message = "User profile not found." });

            return Ok(profile);
        }

        /// <summary>
        /// POST /api/user/complete-exercise
        /// Marks one of the three daily exercises as done.
        /// </summary>
        [HttpPost("complete-exercise")]
        public async Task<IActionResult> CompleteExercise([FromBody] CompleteExerciseRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.FirebaseUid))
                return BadRequest(new { message = "firebaseUid is required." });

            var allowed = new[] { "sound", "word", "sentence" };
            if (!allowed.Contains(request.ExerciseType?.ToLower()))
                return BadRequest(new { message = "exerciseType must be 'sound', 'word', or 'sentence'." });

            var (success, message) = await _firebase.MarkExerciseCompleteAsync(
                request.FirebaseUid, request.ExerciseType!.ToLower());

            if (!success)
                return StatusCode(500, new { message });

            return Ok(new { message = $"{request.ExerciseType} exercise marked complete." });
        }

        /// <summary>
        /// POST /api/user/update-streak
        /// Updates the user's daily streak count.
        /// </summary>
        [HttpPost("update-streak")]
        public async Task<IActionResult> UpdateStreak([FromBody] UpdateStreakRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.FirebaseUid))
                return BadRequest(new { message = "firebaseUid is required." });

            var (success, message) = await _firebase.UpdateStreakAsync(
                request.FirebaseUid, request.Streak);

            if (!success)
                return StatusCode(500, new { message });

            return Ok(new { message = "Streak updated.", streak = request.Streak });
        }

        /// <summary>
        /// POST /api/user/logout
        /// Lightweight logout — the client clears its own session storage.
        /// </summary>
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // Firebase tokens are stateless JWTs — nothing to invalidate server-side.
            // The client is responsible for clearing sessionStorage / localStorage.
            return Ok(new { message = "Logged out successfully." });
        }
    }
}