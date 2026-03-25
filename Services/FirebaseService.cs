using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Firebase.Database;
using Firebase.Database.Query;
using Google.Apis.Auth.OAuth2;

namespace SpeechLiftWebAPI.Services
{
    public class FirebaseService
    {
        private readonly FirebaseClient _database;

        public FirebaseService(IConfiguration config)
        {
            // Only initialise FirebaseApp once (singleton guard)
            if (FirebaseApp.DefaultInstance == null)
            {
                var creds = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS");

                GoogleCredential credential = string.IsNullOrEmpty(creds)
                    ? GoogleCredential.FromFile("firebase-admin.json")          // local dev
                    : GoogleCredential.FromStream(                               // Render / production
                        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(creds)));

                FirebaseApp.Create(new AppOptions { Credential = credential });
            }

            string dbUrl = config["Firebase:DatabaseUrl"]
                ?? throw new InvalidOperationException(
                    "Firebase:DatabaseUrl is missing from appsettings.json");

            _database = new FirebaseClient(dbUrl);
        }

        // ── Auto-increment user ID ────────────────────────────────────────────
        private async Task<int> GetNextUserIdAsync()
        {
            var counterRef = _database.Child("meta").Child("userCount");
            var current = await counterRef.OnceSingleAsync<int?>() ?? 0;
            int next = current + 1;
            await counterRef.PutAsync(next);
            return next;
        }

        // ════════════════════════════════════════════════════════════════════
        //  REGISTER
        // ════════════════════════════════════════════════════════════════════
        public async Task<(bool Success, string Message, int? NumericId)> RegisterAsync(
            string username, string email, string password)
        {
            try
            {
                var userArgs = new UserRecordArgs
                {
                    DisplayName = username,
                    Email = email,
                    Password = password,
                    Disabled = false
                };

                UserRecord user = await FirebaseAuth.DefaultInstance.CreateUserAsync(userArgs);
                int numericId = await GetNextUserIdAsync();

                await _database
                    .Child("users")
                    .Child(user.Uid)
                    .PutAsync(new
                    {
                        id = numericId,
                        uid = user.Uid,
                        username,
                        email,
                        created = DateTime.UtcNow.ToString("o"),
                        streak = 0,
                        weekNumber = 1,
                        frequencyLevel = 0,      // 0 = not yet assessed
                        soundDone = false,
                        wordDone = false,
                        sentenceDone = false,
                    });

                return (true, "Account created successfully.", numericId);
            }
            catch (FirebaseAuthException ex)
            {
                return (false, CleanFirebaseError(ex.Message), null);
            }
            catch (Exception ex)
            {
                return (false, $"Database error: {ex.Message}", null);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  LOGIN
        //  Returns frequencyLevel so the frontend can decide:
        //    0  → not yet assessed → redirect to survey
        //    1+ → already assessed → redirect to dashboard
        // ════════════════════════════════════════════════════════════════════
        public async Task<(bool Success, string Message, int? NumericId, string? FirebaseUid,
                   string? Username, int FrequencyLevel)> LoginAsync(string email)
        {
            try
            {
                UserRecord user;
        
                try
                {
                    user = await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(email);
                }
                catch (FirebaseAuthException)
                {
                    return (false, "No account found with that email.", null, null, null, 0);
                }
        
                var profile = await _database
                    .Child("users")
                    .Child(user.Uid)
                    .OnceSingleAsync<UserProfile>();
        
                if (profile == null)
                    return (false, "User profile not found in database.", null, null, null, 0);
        
                var assessment = await _database
                    .Child("users")
                    .Child(user.Uid)
                    .Child("assessment")
                    .OnceSingleAsync<AssessmentRecord>();
        
                string displayName = profile.Username ?? user.DisplayName ?? user.Email ?? "User";
                int frequencyLevel = assessment?.FrequencyLevel ?? profile.FrequencyLevel ?? 0;
        
                return (true, $"Welcome back, {displayName}!",
                        profile.Id, user.Uid, displayName, frequencyLevel);
            }
            catch (Exception ex)
            {
                Console.WriteLine("🔥 FIREBASE LOGIN ERROR: " + ex.Message);
                return (false, "Server error during login.", null, null, null, 0);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  GET USER PROFILE
        // ════════════════════════════════════════════════════════════════════
        public async Task<(bool Success, DashboardProfile? Profile)> GetUserProfileAsync(
            string firebaseUid)
        {
            try
            {
                var profile = await _database
                    .Child("users")
                    .Child(firebaseUid)
                    .OnceSingleAsync<UserProfile>();

                if (profile == null) return (false, null);

                var assessment = await _database
                    .Child("users")
                    .Child(firebaseUid)
                    .Child("assessment")
                    .OnceSingleAsync<AssessmentRecord>();

                var dash = new DashboardProfile
                {
                    Username = profile.Username ?? "User",
                    FrequencyLevel = assessment?.FrequencyLevel ?? profile.FrequencyLevel ?? 1,
                    WeekNumber = profile.WeekNumber ?? 1,
                    Streak = profile.Streak ?? 0,
                    JoinedAt = profile.Created,
                    SoundDone = profile.SoundDone ?? false,
                    WordDone = profile.WordDone ?? false,
                    SentenceDone = profile.SentenceDone ?? false,
                };

                return (true, dash);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GetUserProfileAsync error: {ex.Message}");
                return (false, null);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  MARK EXERCISE COMPLETE
        // ════════════════════════════════════════════════════════════════════
        public async Task<(bool Success, string Message)> MarkExerciseCompleteAsync(
            string firebaseUid, string exerciseType)
        {
            try
            {
                await EnsureDailyResetAsync(firebaseUid);

                string field = exerciseType switch
                {
                    "sound" => "soundDone",
                    "word" => "wordDone",
                    "sentence" => "sentenceDone",
                    _ => throw new ArgumentException("Invalid exercise type")
                };

                await _database
                    .Child("users")
                    .Child(firebaseUid)
                    .Child(field)
                    .PutAsync(true);

                return (true, $"{exerciseType} marked complete.");
            }
            catch (Exception ex)
            {
                return (false, $"Database error: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  UPDATE STREAK
        // ════════════════════════════════════════════════════════════════════
        public async Task<(bool Success, string Message)> UpdateStreakAsync(
            string firebaseUid, int streak)
        {
            try
            {
                await _database
                    .Child("users").Child(firebaseUid).Child("streak")
                    .PutAsync(streak);

                await _database
                    .Child("users").Child(firebaseUid).Child("lastStreakDate")
                    .PutAsync(DateTime.UtcNow.ToString("yyyy-MM-dd"));

                return (true, "Streak updated.");
            }
            catch (Exception ex)
            {
                return (false, $"Database error: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SAVE ASSESSMENT
        // ════════════════════════════════════════════════════════════════════
        public async Task<(bool Success, string Message)> SaveAssessmentAsync(
            string? firebaseUid,
            int? numericId,
            string? formattedUid,
            int frequencyLevel,
            int totalScore,
            int[]? answers)
        {
            try
            {
                var assessmentData = new
                {
                    formattedUid,
                    frequencyLevel,
                    totalScore,
                    answers,
                    takenAt = DateTime.UtcNow.ToString("o"),
                    levelLabel = frequencyLevel switch
                    {
                        1 => "Normal Speech",
                        2 => "Mild Speech Impairment",
                        3 => "Moderate Speech Impairment",
                        4 => "Severe Speech Impairment",
                        5 => "Profound Speech Impairment",
                        _ => "Unknown"
                    }
                };

                if (!string.IsNullOrWhiteSpace(firebaseUid))
                {
                    await _database
                        .Child("users").Child(firebaseUid).Child("assessment")
                        .PutAsync(assessmentData);

                    // Mirror frequencyLevel to top-level field for fast reads
                    await _database
                        .Child("users").Child(firebaseUid).Child("frequencyLevel")
                        .PutAsync(frequencyLevel);
                }
                else if (numericId.HasValue)
                {
                    await _database
                        .Child("assessments").Child(numericId.Value.ToString())
                        .PutAsync(assessmentData);
                }
                else
                {
                    return (false, "No valid user identifier provided.");
                }

                return (true, "Assessment saved.");
            }
            catch (Exception ex)
            {
                return (false, $"Database error: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  DAILY RESET — resets exercise flags at the start of a new day
        // ════════════════════════════════════════════════════════════════════
        private async Task EnsureDailyResetAsync(string firebaseUid)
        {
            try
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var lastActiveDate = await _database
                    .Child("users").Child(firebaseUid).Child("lastActiveDate")
                    .OnceSingleAsync<string?>();

                if (lastActiveDate != today)
                {
                    await _database.Child("users").Child(firebaseUid).Child("soundDone").PutAsync(false);
                    await _database.Child("users").Child(firebaseUid).Child("wordDone").PutAsync(false);
                    await _database.Child("users").Child(firebaseUid).Child("sentenceDone").PutAsync(false);
                    await _database.Child("users").Child(firebaseUid).Child("lastActiveDate").PutAsync(today);
                }
            }
            catch
            {
                // Non-critical — do not crash the request if this fails
            }
        }

        // ── Firebase error message cleaner ───────────────────────────────────
        private static string CleanFirebaseError(string message)
        {
            if (message.Contains("EMAIL_EXISTS") || message.Contains("email-already-exists"))
                return "An account with that email already exists.";
            if (message.Contains("WEAK_PASSWORD") || message.Contains("weak-password"))
                return "Password is too weak.";
            if (message.Contains("INVALID_EMAIL") || message.Contains("invalid-email"))
                return "The email address is invalid.";
            return message;
        }
    }
}

// ── Data Models ───────────────────────────────────────────────────────────────

public class UserProfile
{
    public int? Id { get; set; }
    public string? Uid { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Created { get; set; }
    public int? Streak { get; set; }
    public int? WeekNumber { get; set; }
    public int? FrequencyLevel { get; set; }
    public bool? SoundDone { get; set; }
    public bool? WordDone { get; set; }
    public bool? SentenceDone { get; set; }
    public string? LastActiveDate { get; set; }
    public string? LastStreakDate { get; set; }
}

public class AssessmentRecord
{
    public int? FrequencyLevel { get; set; }
    public int? TotalScore { get; set; }
    public string? LevelLabel { get; set; }
    public string? TakenAt { get; set; }
    public string? FormattedUid { get; set; }
}

public class DashboardProfile
{
    public string Username { get; set; } = "User";
    public int FrequencyLevel { get; set; } = 1;
    public int WeekNumber { get; set; } = 1;
    public int Streak { get; set; } = 0;
    public string? JoinedAt { get; set; }
    public bool SoundDone { get; set; }
    public bool WordDone { get; set; }
    public bool SentenceDone { get; set; }
}
