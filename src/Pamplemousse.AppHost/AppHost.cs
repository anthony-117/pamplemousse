var builder = DistributedApplication.CreateBuilder(args);

// Postgres with the pgvector extension (RAG embeddings live in the same database).
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume();
var database = postgres.AddDatabase("travelgenie");

var cache = builder.AddRedis("cache");

builder.AddProject<Projects.Pamplemousse_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
