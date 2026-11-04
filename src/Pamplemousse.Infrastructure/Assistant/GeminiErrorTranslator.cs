using Google.GenAI;
using Microsoft.Extensions.AI;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Infrastructure.Assistant;

/// <summary>
/// Turns Gemini's failures (429 over the free limit, 503 high demand, other API errors, network errors, time-outs) into
/// <see cref="AssistantUnavailableException"/>, so the handler answers with a warning instead of a server error (spec 003, FR-008).
/// </summary>
internal sealed class GeminiErrorTranslator(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            throw new AssistantUnavailableException("Gemini refused the request.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AssistantUnavailableException("Gemini could not be reached.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AssistantUnavailableException("Gemini took too long to answer.", ex);
        }
    }
}
