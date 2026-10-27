using System.Text.Json.Serialization;
using Pamplemousse.Api;
using Pamplemousse.Api.Features.Destinations;
using Pamplemousse.Api.Features.System;
using Pamplemousse.Infrastructure;

Env.LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
// Bad query strings return 400 Problem Details in every environment (Development would otherwise throw → 500).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

builder.AddInfrastructure(keys =>
{
    keys.GooglePlacesApiKey = Env.GooglePlacesApiKey;
    keys.OpenTripMapApiKey = Env.OpenTripMapApiKey;
    keys.TicketmasterApiKey = Env.TicketmasterApiKey;
});
builder.Services.AddScoped<DestinationService>();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

var api = app.MapGroup("/api/v1");
api.MapSystemEndpoints();
api.MapDestinationEndpoints();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;
