using System.Text.Json.Serialization;
using Pamplemousse.Api;
using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Middleware;
using Pamplemousse.Api.RateLimiting;
using Pamplemousse.Application;
using Pamplemousse.Application.Cars;
using Pamplemousse.Application.Hotels;
using Pamplemousse.Infrastructure;
using Scalar.AspNetCore;

Env.LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

// How much hotel data each search gathers (FR-008c); the defaults suit the free request plan.
builder.Services.AddSingleton(builder.Configuration.GetSection("Hotels").Get<HotelSettings>() ?? new HotelSettings());
builder.Services.AddSingleton(builder.Configuration.GetSection("Cars").Get<CarSettings>() ?? new CarSettings());
builder.Services.AddApplication();
builder.AddInfrastructure(keys =>
{
    keys.OpenTripMapApiKey = Env.OpenTripMapApiKey;
    keys.TicketmasterApiKey = Env.TicketmasterApiKey;
    keys.TravelpayoutsApiToken = Env.TravelpayoutsApiToken;
    keys.TravelpayoutsMarker = Env.TravelpayoutsMarker;
    keys.RapidApiKey = Env.RapidApiKey;
    keys.FirebaseProjectId = Env.FirebaseProjectId;
    keys.GoogleCredentialsPath = Env.GoogleApplicationCredentials;
    keys.GeminiApiKey = Env.GeminiApiKey;
    keys.GeminiModel = Env.GeminiModel;
});

// Browsers only call the API from the TravelGenie web app: CORS_ALLOWED_ORIGINS (e.g. the Firebase domain)
// in every environment, plus any localhost port during development (flutter run -d chrome).
const string AppCorsPolicy = "app";
var allowedOrigins = Env.CorsAllowedOrigins;
var allowLoopback = builder.Environment.IsDevelopment();
builder.Services.AddCors(options => options.AddPolicy(AppCorsPolicy, policy => policy
    .SetIsOriginAllowed(origin =>
        allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || (allowLoopback && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback))
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Every route needs a Firebase sign-in unless marked [AllowAnonymous] (spec 009, ADR 0019).
builder.Services.AddFirebaseSignIn(Env.FirebaseProjectId);

// The assistant spends the shared Gemini free tier: limited per client and overall (spec 003, FR-015).
builder.Services.AddAssistantRateLimits(builder.Configuration);

var app = builder.Build();

// Caddy sits in front in production; the client's own IP is what the assistant's limits count.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(AppCorsPolicy);

// After CORS, so a browser can read a 401.
app.UseAuthentication();
app.UseAuthorization();

// After CORS, so the web app can read a 429.
app.UseRateLimiter();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    // Interactive API reference at /scalar, built from /openapi/v1.json.
    app.MapScalarApiReference(o => o.WithTitle("pamplemousse API")).AllowAnonymous();
}

app.MapDefaultEndpoints();

app.MapControllers();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;
