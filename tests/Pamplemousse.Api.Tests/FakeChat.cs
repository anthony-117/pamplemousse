using Microsoft.Extensions.AI;

namespace Pamplemousse.Api.Tests;

/// <summary>
/// Plays Gemini for the endpoint tests, so they never call the internet even when the developer's .env has a key.
/// With <see cref="ToolToCall"/> set, the first call asks for that tool and the second answers with <see cref="Answer"/>;
/// the real tool loop runs the tool in between. <see cref="Script"/> calls several tools, one per round, before the
/// answer. <see cref="Gate"/> holds every call until it is completed. <see cref="Error"/> makes every call fail.
/// </summary>
public sealed class FakeChat : IChatClient
{
    public string Answer { get; set; } = "Hello from the fake model.";

    public string? ToolToCall { get; set; }

    public Exception? Error { get; set; }

    /// <summary>Tools to call in order, one per round, with their arguments; then <see cref="Answer"/>.</summary>
    public IList<(string Tool, Dictionary<string, object?> Args)> Script { get; } = [];

    public TaskCompletionSource? Gate { get; set; }

    /// <summary>The messages of the last call, to check what the model was sent.</summary>
    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

    public void Reset()
    {
        Error = null;
        ToolToCall = null;
        Answer = "Hello from the fake model.";
        Script.Clear();
        Gate = null;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (Error is not null)
        {
            throw Error;
        }

        var all = messages.ToList();
        LastMessages = all;
        var rounds = all.Count(m => m.Contents.OfType<FunctionResultContent>().Any());
        ChatMessage reply;
        if (rounds < Script.Count)
        {
            var (tool, args) = Script[rounds];
            reply = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call{rounds}", tool, args)]);
        }
        else
        {
            reply = ToolToCall is not null && rounds == 0
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call1", ToolToCall, new Dictionary<string, object?>(StringComparer.Ordinal))])
                : new ChatMessage(ChatRole.Assistant, Answer);
        }

        return new ChatResponse(reply);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
