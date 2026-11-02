using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Application.Common;

/// <summary>Loads the sections of a page independently: one failing source becomes a warning and never breaks the whole response.</summary>
internal sealed partial class SectionLoader(ILogger logger)
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public async Task<T?> LoadAsync<T>(string name, Func<Task<T>> load)
    {
        try
        {
            return await load();
        }
        catch (ProviderNotConfiguredException ex)
        {
            AddWarning(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogSectionFailed(logger, name, ex);
            AddWarning($"{name} is unavailable right now.");
        }

        return default;
    }

    public void AddWarning(string message)
    {
        lock (_warnings)
        {
            _warnings.Add(message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Section} failed")]
    private static partial void LogSectionFailed(ILogger logger, string section, Exception exception);
}
