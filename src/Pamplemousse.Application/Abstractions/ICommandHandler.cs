namespace Pamplemousse.Application.Abstractions;

/// <summary>One use case that changes something. Like <see cref="IQueryHandler{TQuery, TResponse}"/>, every client goes through these.</summary>
public interface ICommandHandler<in TCommand, TResponse>
{
    Task<TResponse> HandleAsync(TCommand command, CancellationToken ct);
}
