using System.Text;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Domain.Saved;

/// <summary>What can be saved (spec 010 FR-020): flights and cars are kept in plans instead.</summary>
public enum SavedKind
{
    Hotel,
    Place,
    Event,
}

/// <summary>
/// A hotel, place or event the traveller keeps (FR-021), with what the app needs to show it without another search.
/// <see cref="ItemJson"/> is the card's item as the app sent it.
/// </summary>
public sealed record SavedItem(
    TravellerId Owner,
    SavedKind Kind,
    string Id,
    string DestinationId,
    string DestinationName,
    string Name,
    string? ImageUrl,
    GeoPoint Location,
    DateTimeOffset SavedAt,
    string? ItemJson)
{
    public const int MaxItems = 200;
    public const int MaxNameLength = 200;
    public const int MaxItemBytes = 20 * 1024;
    public const int MaxIdLength = 200;

    /// <summary>"hotel", "place" or "event"; a flight or car names the field <c>kind</c>.</summary>
    public static SavedKind ParseKind(string? kind) => kind?.Trim().ToUpperInvariant() switch
    {
        "HOTEL" => SavedKind.Hotel,
        "PLACE" => SavedKind.Place,
        "EVENT" => SavedKind.Event,
        "FLIGHT" or "CAR" => throw new DomainException("Flights and cars are kept in plans, not in Saved.", "kind"),
        _ => throw new DomainException("Use hotel, place or event.", "kind"),
    };

    /// <summary>Checks every value, naming the field at fault.</summary>
    public static SavedItem Create(
        TravellerId owner,
        SavedKind kind,
        string? id,
        string destinationId,
        string destinationName,
        string? name,
        string? imageUrl,
        double? lat,
        double? lng,
        DateTimeOffset savedAt,
        string? itemJson)
    {
        var trimmedId = id?.Trim();
        if (string.IsNullOrEmpty(trimmedId) || trimmedId.Length > MaxIdLength)
        {
            throw new DomainException("Give the item's id.", "id");
        }

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > MaxNameLength)
        {
            throw new DomainException($"Give a name of 1 to {MaxNameLength} characters.", "name");
        }

        if (lat is not { } la || lng is not { } ln || la is < -90 or > 90 || ln is < -180 or > 180)
        {
            throw new DomainException("Give the item's position.", "lat");
        }

        if (itemJson is not null && Encoding.UTF8.GetByteCount(itemJson) > MaxItemBytes)
        {
            throw new DomainException("The item is too big to keep: at most 20 KB.", "item");
        }

        var image = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        return new SavedItem(owner, kind, trimmedId, destinationId, destinationName, trimmedName, image, new GeoPoint(la, ln), savedAt, itemJson);
    }

    /// <summary>Refuses item 201 (FR-020).</summary>
    public static void EnsureRoomFor(int existing)
    {
        if (existing >= MaxItems)
        {
            throw new SavedLimitException();
        }
    }
}

/// <summary>The account already keeps <see cref="SavedItem.MaxItems"/> items. The API answers 409 <c>too-many-saved</c>.</summary>
public sealed class SavedLimitException() : DomainException($"You can keep up to {SavedItem.MaxItems} saved items. Remove one to make room.");
