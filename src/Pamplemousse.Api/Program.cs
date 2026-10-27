using Pamplemousse.Api.Features.System;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

// Browsers only call the API from the TravelGenie web app: listed origins (e.g. the Firebase domain)
// in every environment, plus any localhost port during development (flutter run -d chrome).
const string AppCorsPolicy = "app";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;
