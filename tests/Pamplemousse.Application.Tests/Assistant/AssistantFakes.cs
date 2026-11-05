using Microsoft.Extensions.AI;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Application.Tests.Assistant;

/// <summary>
/// Plays the chat model: each call to <see cref="GetResponseAsync"/> takes the next scripted step. Wrapped in the real
/// <see cref="FunctionInvokingChatClient"/>, a scripted tool call runs the real tool against fake handlers.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<ChatResponse>> _steps = new();
    private int _nextCall;

    /// <summary>The messages the model received on each call, in order.</summary>
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public ChatOptions? Options { get; private set; }

    public ScriptedChatClient CallTool(string name, Dictionary<string, object?>? arguments = null)
    {
        var id = $"call{++_nextCall}";
        _steps.Enqueue(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(id, name, arguments ?? [])])));
        return this;
    }

    public ScriptedChatClient Answer(string text)
    {
        _steps.Enqueue(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        return this;
    }

    public ScriptedChatClient Fail(Exception error)
    {
        _steps.Enqueue(() => throw error);
        return this;
    }

    /// <summary>The JSON a tool returned to the model, from the latest call that carried one.</summary>
    public string LastToolResult() => Calls
        .SelectMany(c => c)
        .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
        .Last().Result?.ToString() ?? "";

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add([.. messages]);
        Options = options;
        return Task.FromResult(_steps.Dequeue()());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>A use case that answers with <paramref name="answer"/> and remembers every query.</summary>
internal sealed class FakeHandler<TQuery, TResponse>(Func<TQuery, TResponse> answer) : IQueryHandler<TQuery, TResponse>
{
    public List<TQuery> Queries { get; } = [];

    public Task<TResponse> HandleAsync(TQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        return Task.FromResult(answer(query));
    }
}
