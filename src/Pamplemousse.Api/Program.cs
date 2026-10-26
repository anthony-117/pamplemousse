using Pamplemousse.Api.Features.System;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

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
