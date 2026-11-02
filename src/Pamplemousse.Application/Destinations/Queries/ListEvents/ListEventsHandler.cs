using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Common;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Application.Destinations.Queries.ListEvents;

public sealed record ListEventsQuery(string DestinationId, DateOnly? From, DateOnly? To, int? Limit);

public sealed record EventsResponse(DateRange Dates, IReadOnlyList<TravelEvent> Items, IReadOnlyList<string> Warnings);

public sealed class ListEventsHandler(IReferenceData data, IEventProvider events, TimeProvider clock, ILogger<ListEventsHandler> logger)
    : IQueryHandler<ListEventsQuery, EventsResponse>
{
    public async Task<EventsResponse> HandleAsync(ListEventsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = data.FindDestination(query.DestinationId) ?? throw new NotFoundException($"Destination '{query.DestinationId}' was not found.");
        var dates = DateRange.ForTrip(query.From, query.To, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        var sections = new SectionLoader(logger);
        var items = await sections.LoadAsync("Events", () => events.SearchAsync(destination, dates.From, dates.To, Math.Clamp(query.Limit ?? 20, 1, 50), ct));
        return new EventsResponse(dates, items ?? [], sections.Warnings);
    }
}
