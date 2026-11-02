namespace Pamplemousse.Application.Abstractions;

/// <summary>One read use case. API controllers, the chatbot and the Telegram bot all go through these.</summary>
public interface IQueryHandler<in TQuery, TResponse>
{
    Task<TResponse> HandleAsync(TQuery query, CancellationToken ct);
}
