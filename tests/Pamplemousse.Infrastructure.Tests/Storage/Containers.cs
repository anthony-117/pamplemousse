using Google.Cloud.Firestore;
using Grpc.Core;
using StackExchange.Redis;
using Testcontainers.Firestore;
using Testcontainers.Redis;

namespace Pamplemousse.Infrastructure.Tests.Storage;

/// <summary>
/// The Firestore emulator in Docker, started once for the test classes that use it.
/// These tests need Docker running, as the Aspire app and the CI image build already do.
/// </summary>
public sealed class FirestoreEmulator : IAsyncLifetime
{
    private readonly FirestoreContainer _container = new FirestoreBuilder("gcr.io/google.com/cloudsdktool/google-cloud-cli:546.0.0-emulators").Build();

    public FirestoreDb Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var endpoint = new Uri(_container.GetEmulatorEndpoint());
        Db = await new FirestoreDbBuilder
        {
            ProjectId = "travelgenie-test",
            Endpoint = $"{endpoint.Host}:{endpoint.Port}",
            ChannelCredentials = ChannelCredentials.Insecure,
        }.BuildAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>A real Redis in Docker, started once for the test classes that use it.</summary>
public sealed class RedisServer : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7.4-alpine").Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
        await _container.DisposeAsync();
    }
}
