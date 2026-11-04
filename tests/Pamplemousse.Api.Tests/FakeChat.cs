using Microsoft.Extensions.AI;

namespace Pamplemousse.Api.Tests;

/// <summary>
/// Plays Gemini for the endpoint tests, so they never call the internet even when the developer's .env has a key.
/// With <see cref="ToolToCall"/> set, the first call asks for that tool and the second answers with <see cref="Answer"/>;
/// the real tool loop runs the tool in between. <see cref="Error"/> makes every call fail.
/// </summary>
public sealed class FakeChat : IChatClient
{
    public string Answer { get; set; } = "Hello from the fake model.";

    public string? ToolToCall { get; set; }

    public Exception? Error { get; set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (Error is not null)
        {
            return Task.FromException<ChatResponse>(Error);
        }

        var toolAlreadyRan = messages.Any(m => m.Contents.OfType<FunctionResultContent>().Any());
        var reply = ToolToCall is not null && !toolAlreadyRan
            ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call1", ToolToCall, new Dictionary<string, object?>(StringComparer.Ordinal))])
            : new ChatMessage(ChatRole.Assistant, Answer);
        return Task.FromResult(new ChatResponse(reply));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
