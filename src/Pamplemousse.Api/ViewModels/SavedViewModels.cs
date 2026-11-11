using System.Text.Json;
using Pamplemousse.Application.Saved;
using Pamplemousse.Domain.Saved;

namespace Pamplemousse.Api.ViewModels;

/// <summary>A saved item; <see cref="Item"/> is the card's item as it was saved, or null.</summary>
public sealed record SavedItemViewModel(
    SavedKind Kind,
    string Id,
    string DestinationId,
    string DestinationName,
    string Name,
    string? ImageUrl,
    double Lat,
    double Lng,
    DateTimeOffset SavedAt,
    JsonElement? Item)
{
    public static SavedItemViewModel Of(SavedItem i)
    {
        ArgumentNullException.ThrowIfNull(i);
        JsonElement? item = null;
        if (i.ItemJson is { } json)
        {
            using var doc = JsonDocument.Parse(json);
            item = doc.RootElement.Clone();
        }

        return new(i.Kind, i.Id, i.DestinationId, i.DestinationName, i.Name, i.ImageUrl, i.Location.Lat, i.Location.Lng, i.SavedAt, item);
    }
}

/// <summary>Newest first, with the account's count and the most it can keep.</summary>
public sealed record SavedListViewModel(IReadOnlyList<SavedItemViewModel> Items, int Count, int Max)
{
    public static SavedListViewModel Of(SavedList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new([.. list.Items.Select(SavedItemViewModel.Of)], list.Count, list.Max);
    }
}
