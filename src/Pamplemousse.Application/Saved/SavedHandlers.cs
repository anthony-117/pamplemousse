using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Saved;

/// <summary>Saves a hotel, place or event (FR-020). <see cref="ItemJson"/> is the card's item as the app sent it.</summary>
public sealed record SaveItemCommand(
    TravellerId Owner,
    string? Kind,
    string? Id,
    string? DestinationId,
    string? Name,
    string? ImageUrl,
    double? Lat,
    double? Lng,
    string? ItemJson);

/// <summary>Saving the same item again changes nothing and keeps its first time saved.</summary>
public sealed class SaveItemHandler(ISavedStore store, IReferenceData data, TimeProvider clock) : ICommandHandler<SaveItemCommand, SavedItem>
{
    public async Task<SavedItem> HandleAsync(SaveItemCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var kind = SavedItem.ParseKind(command.Kind);
        var city = (string.IsNullOrWhiteSpace(command.DestinationId) ? null : data.FindDestination(command.DestinationId.Trim()))
            ?? throw new DomainException("Choose a city from the list.", "destinationId");
        var item = SavedItem.Create(
            command.Owner, kind, command.Id, city.Id, city.Name, command.Name, command.ImageUrl, command.Lat, command.Lng, clock.GetUtcNow(), command.ItemJson);
        if (await store.GetAsync(command.Owner, kind, item.Id, ct) is { } existing)
        {
            return existing;
        }

        SavedItem.EnsureRoomFor(await store.CountAsync(command.Owner, ct));
        await store.PutAsync(item, ct);
        return item;
    }
}

public sealed record RemoveSavedCommand(TravellerId Owner, string? Kind, string? Id);

/// <summary>Removing an item that isn't saved is not an error.</summary>
public sealed class RemoveSavedHandler(ISavedStore store) : ICommandHandler<RemoveSavedCommand, bool>
{
    public async Task<bool> HandleAsync(RemoveSavedCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var kind = SavedItem.ParseKind(command.Kind);
        if (!string.IsNullOrWhiteSpace(command.Id))
        {
            await store.DeleteAsync(command.Owner, kind, command.Id.Trim(), ct);
        }

        return true;
    }
}

/// <summary>Newest first, optionally of one kind or one city.</summary>
public sealed record ListSavedQuery(TravellerId Owner, string? Kind = null, string? DestinationId = null);

public sealed record SavedList(IReadOnlyList<SavedItem> Items, int Count, int Max);

public sealed class ListSavedHandler(ISavedStore store) : IQueryHandler<ListSavedQuery, SavedList>
{
    public async Task<SavedList> HandleAsync(ListSavedQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        SavedKind? kind = string.IsNullOrWhiteSpace(query.Kind) ? null : SavedItem.ParseKind(query.Kind);
        var city = string.IsNullOrWhiteSpace(query.DestinationId) ? null : query.DestinationId.Trim();
        var all = await store.ListAsync(query.Owner, ct);
        return new SavedList(
            [.. all.Where(i => (kind is null || i.Kind == kind) && (city is null || string.Equals(i.DestinationId, city, StringComparison.Ordinal)))],
            all.Count,
            SavedItem.MaxItems);
    }
}

/// <summary>The saved ids by kind, so every card shows its bookmark.</summary>
public sealed record SavedIdsQuery(TravellerId Owner);

public sealed record SavedIds(IReadOnlyList<string> Hotel, IReadOnlyList<string> Place, IReadOnlyList<string> Event);

public sealed class SavedIdsHandler(ISavedStore store) : IQueryHandler<SavedIdsQuery, SavedIds>
{
    public async Task<SavedIds> HandleAsync(SavedIdsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await store.ListAsync(query.Owner, ct);
        IReadOnlyList<string> Of(SavedKind kind) => [.. all.Where(i => i.Kind == kind).Select(i => i.Id)];
        return new SavedIds(Of(SavedKind.Hotel), Of(SavedKind.Place), Of(SavedKind.Event));
    }
}
