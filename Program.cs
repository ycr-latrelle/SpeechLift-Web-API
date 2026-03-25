using SpeechLiftWebAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ─────────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// Register FirebaseService as singleton.
// Reads FIREBASE_CREDENTIALS env var when deployed on Render;
// falls back to firebase-admin.json for local development.
builder.Services.AddSingleton<FirebaseService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    var creds = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS");

    if (string.IsNullOrEmpty(creds))
        Console.WriteLine("⚠️  FIREBASE_CREDENTIALS is NOT SET — falling back to local file.");
    else
        Console.WriteLine("✅ FIREBASE_CREDENTIALS is SET.");

    return new FirebaseService(config);
});

// CORS — allow all origins so your frontend can call this API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────

app.UseRouting();

// CORS must come BEFORE MapControllers
app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

// ── Startup ───────────────────────────────────────────────────────────────────

// Render injects PORT automatically; 8080 is the fallback for local dev
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
