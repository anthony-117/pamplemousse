using System.Runtime.CompilerServices;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Infrastructure.Assistant;

namespace Pamplemousse.Infrastructure.Tests.Assistant;

public sealed class ChatModelTests
{
    private static readonly ChatMessage[] Question = [new(ChatRole.User, "Hi")];

    /// <summary>A Gemini error as the SDK throws it (503 high demand, 429 over the limit…). Its constructor isn't public API, so it is skipped.</summary>
    private static ServerError GeminiError() => (ServerError)RuntimeHelpers.GetUninitializedObject(typeof(ServerError));

    public static TheoryData<string, Exception> Failures => new()
    {
        { "Gemini API error", GeminiError() },
        { "network", new HttpRequestException("no route") },
        { "time-out", new TaskCanceledException("deadline") },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Gemini_failures_become_assistant_unavailable(string why, Exception failure)
    {
        using var client = new GeminiErrorTranslator(new ThrowingChatClient(failure));

        var ex = await Assert.ThrowsAsync<AssistantUnavailableException>(() => client.GetResponseAsync(Question));

        Assert.True(ReferenceEquals(failure, ex.InnerException), why);
    }

    [Fact]
    public async Task The_callers_own_cancellation_passes_through()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var client = new GeminiErrorTranslator(new ThrowingChatClient(new OperationCanceledException(cancelled.Token)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetResponseAsync(Question, cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task Without_a_key_every_answer_names_the_variable()
    {
        using var services = Build(apiKey: "");
        var chat = services.GetRequiredService<IChatClient>();

        var ex = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => chat.GetResponseAsync(Question));

        Assert.IsType<NotConfiguredChatClient>(chat);
        Assert.Equal("GEMINI_API_KEY", ex.EnvironmentVariable);
    }

    [Fact]
    public void With_a_key_gemini_runs_inside_the_tool_loop_and_the_error_translation()
    {
        using var services = Build(apiKey: "test-key");
        var chat = services.GetRequiredService<IChatClient>();

        var loop = Assert.IsType<FunctionInvokingChatClient>(chat);
        Assert.Equal(6, loop.MaximumIterationsPerRequest);
        Assert.NotNull(chat.GetService<GeminiErrorTranslator>());
        Assert.Equal("gemini-test", chat.GetService<ChatClientMetadata>()?.DefaultModelId);
    }

    private static ServiceProvider Build(string apiKey)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddInfrastructure(keys =>
        {
            keys.GeminiApiKey = apiKey;
            keys.GeminiModel = "gemini-test";
        });
        return builder.Services.BuildServiceProvider();
    }

    private sealed class ThrowingChatClient(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(failure);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
