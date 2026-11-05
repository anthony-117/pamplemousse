using Microsoft.Extensions.AI;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Infrastructure.Assistant;

/// <summary>Stands in for Gemini when GEMINI_API_KEY is empty: the API starts, and each answer is a warning naming the variable (FR-007).</summary>
internal sealed class NotConfiguredChatClient : IChatClient
{
    public const string EnvironmentVariable = "GEMINI_API_KEY";

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
