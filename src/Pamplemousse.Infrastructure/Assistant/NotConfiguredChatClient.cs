using Microsoft.Extensions.AI;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Infrastructure.Assistant;

/// <summary>
/// Stands in for the model when the chosen provider's key is empty: the API starts, and each answer is a warning naming the
/// variable (spec 003 FR-007, spec 010 FR-035).
/// </summary>
internal sealed class NotConfiguredChatClient(string environmentVariable = NotConfiguredChatClient.GeminiKey) : IChatClient
{
    public const string GeminiKey = "GEMINI_API_KEY";

    public string EnvironmentVariable { get; } = environmentVariable;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new ProviderNotConfiguredException("Assistant", EnvironmentVariable);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new ProviderNotConfiguredException("Assistant", EnvironmentVariable);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType?.IsInstanceOfType(this) == true ? this : null;

    public void Dispose()
    {
        // Nothing to release.
    }
}
