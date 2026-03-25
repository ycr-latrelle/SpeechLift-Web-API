using SpeechLiftWebAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace SpeechLiftWebAPI.Controllers
{
    // ── Request Model ─────────────────────────────────────────────────────────

    public class AssessmentRequest
    {
        public string? Uid { get; set; }  // formatted UID e.g. "26230301"
        public int? NumericId { get; set; }  // sequential DB id
        public string? FirebaseUid { get; set; }  // raw Firebase UID
        public int FrequencyLevel { get; set; }  // 1 – 5
        public int TotalScore { get; set; }  // 5 – 25
        public int[]? Answers { get; set; }  // per-question values
    }

    // ── Controller ────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/assessment")]
    public class AssessmentController : ControllerBase
    {
        private readonly FirebaseService _firebase;

        public AssessmentController(FirebaseService firebase)
        {
            _firebase = firebase;
        }

        /// <summary>
        /// POST /api/assessment/submit
        /// Receives survey results and saves them to Firebase under the user's profile.
        /// </summary>
        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] AssessmentRequest? request)
        {
            // ── Validation ────────────────────────────────────────────────────
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            if (string.IsNullOrWhiteSpace(request.FirebaseUid) && request.NumericId == null)
                return BadRequest(new { message = "A valid user identifier is required." });

            if (request.FrequencyLevel < 1 || request.FrequencyLevel > 5)
                return BadRequest(new { message = "Frequency level must be between 1 and 5." });

            if (request.TotalScore < 5 || request.TotalScore > 25)
                return BadRequest(new { message = "Total score must be between 5 and 25." });

            // ── Save ──────────────────────────────────────────────────────────
            var (success, message) = await _firebase.SaveAssessmentAsync(
                firebaseUid: request.FirebaseUid,
                numericId: request.NumericId,
                formattedUid: request.Uid,
                frequencyLevel: request.FrequencyLevel,
                totalScore: request.TotalScore,
                answers: request.Answers
            );

            if (!success)
                return StatusCode(500, new { message });

            return Ok(new
            {
                message = "Assessment saved successfully.",
                uid = request.Uid,
                frequencyLevel = request.FrequencyLevel,
                totalScore = request.TotalScore,
            });
        }
    }
}