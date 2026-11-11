using System.Text.Json;

namespace Pamplemousse.Api.Contracts;

/// <summary>What the Saved pane needs to show an item (contracts/saved-api.md). <see cref="Item"/> is the card's item.</summary>
public sealed record SaveItemRequest(string? DestinationId, string? Name, string? ImageUrl, double? Lat, double? Lng, JsonElement? Item);
