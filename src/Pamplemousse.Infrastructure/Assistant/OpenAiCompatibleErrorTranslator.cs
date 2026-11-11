using System.ClientModel;
using Microsoft.Extensions.AI;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Infrastructure.Assistant;

/// <summary>
/// Turns an OpenAI-compatible provider's failures (429 over its limit, 503, other API errors, network errors, time-outs) into
/// <see cref="AssistantUnavailableException"/>, so the answer ends with the busy warning (spec 010 SC-007), as for Gemini.
/// </summary>
internal sealed class OpenAiCompatibleErrorTranslator(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException ex)
        {
            throw new AssistantUnavailableException("The model provider refused the request.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AssistantUnavailableException("The model provider could not be reached.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AssistantUnavailableException("The model provider took too long to answer.", ex);
        }
    }
}
