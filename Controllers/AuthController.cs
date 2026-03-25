using SpeechLiftWebAPI.Models;
using SpeechLiftWebAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace SpeechLiftWebAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase   // ← fixed: was Controller, should be ControllerBase for APIs
    {
        private readonly FirebaseService _firebase;

        public AuthController(FirebaseService firebase)
        {
            _firebase = firebase;
        }

        /// <summary>
        /// POST /api/auth/login
        /// Returns uid, firebaseUid, username AND frequencyLevel so the
        /// frontend can decide in one round-trip whether to skip the survey.
        ///   frequencyLevel == 0  →  never assessed → go to survey.html
        ///   frequencyLevel >= 1  →  already done   → go to dashboard.html
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                Console.WriteLine("📥 LOGIN REQUEST RECEIVED");
        
                if (string.IsNullOrWhiteSpace(request?.Email) ||
                    string.IsNullOrWhiteSpace(request?.Password))
                {
                    Console.WriteLine("❌ Missing email/password");
                    return BadRequest(new { message = "Email and password are required." });
                }
        
                var result = await _firebase.LoginAsync(request.Email);
        
                Console.WriteLine("✅ Firebase response received");
        
                if (!result.Success)
                {
                    Console.WriteLine("❌ Login failed: " + result.Message);
                    return Unauthorized(new { message = result.Message });
                }
        
                return Ok(new
                {
                    message = result.Message,
                    uid = result.NumericId,
                    firebaseUid = result.FirebaseUid,
                    username = result.Username,
                    frequencyLevel = result.FrequencyLevel
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("🔥 CONTROLLER CRASH:");
                Console.WriteLine(ex.ToString());
        
                return StatusCode(500, new
                {
                    message = "Server crashed",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// POST /api/auth/register
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Username) ||
                string.IsNullOrWhiteSpace(request?.Email) ||
                string.IsNullOrWhiteSpace(request?.Password))
                return BadRequest(new { message = "All fields are required." });

            if (request.Password.Length < 8)
                return BadRequest(new { message = "Password must be at least 8 characters." });

            var (success, message, uid) = await _firebase
                .RegisterAsync(request.Username, request.Email, request.Password);

            if (!success)
                return Conflict(new { message });

            return Ok(new { message, uid });
        }
    }
}
