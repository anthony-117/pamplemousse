using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>
/// Opens the Firestore connection when the API starts. The first connection takes several seconds (credentials and
/// the gRPC channel), which would otherwise fall on the first traveller's search. Startup does not wait for it.
/// </summary>
public sealed partial class FirestoreWarmUp(FirestoreDb db, ILogger<FirestoreWarmUp> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await db.Collection("cities").Limit(1).GetSnapshotAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWarmUpFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not reach Firestore at startup; hotel data will be retried on each request")]
    private static partial void LogWarmUpFailed(ILogger logger, Exception exception);
}
