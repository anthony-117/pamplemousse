using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;

namespace Pamplemousse.Infrastructure.Assistant;

/// <summary>
/// Deletes every chat, with its messages, 90 days after its last message (spec 010 FR-026, research R-8): once soon after
/// the API starts, then once a day. Reads already treat an expired chat as missing, so a late sweep is never seen.
/// </summary>
public sealed partial class ChatSweeper(IChatStore chats, TimeProvider clock, ILogger<ChatSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    /// <summary>After start-up, so the first sweep doesn't slow the first requests.</summary>
    public static readonly TimeSpan FirstAfter = TimeSpan.FromMinutes(5);

    /// <summary>One sweep; how many chats it deleted.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var deleted = await chats.DeleteExpiredAsync(clock.GetUtcNow().AddDays(-ChatRules.KeepDays), ct);
        LogSwept(logger, deleted);
        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstAfter, clock, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (ServiceUnavailableException ex)
                {
                    // Tomorrow's sweep tries again.
                    LogFailed(logger, ex);
                }

                await Task.Delay(Every, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The API is stopping.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} chats past their 90 days")]
    private static partial void LogSwept(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The chat sweep failed; it runs again tomorrow")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
