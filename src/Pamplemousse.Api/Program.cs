using System.Text.Json.Serialization;
using Pamplemousse.Api;
using Pamplemousse.Api.Middleware;
using Pamplemousse.Application;
using Pamplemousse.Application.Hotels;
using Pamplemousse.Infrastructure;

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

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(AppCorsPolicy);
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.MapControllers();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;
