using System.Globalization;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Application.Assistant;

/// <summary>The app screen a card opens. Events are a section of the overview, so they open it too.</summary>
public enum CardKind
{
    Overview,
    Places,
    Flights,
    Hotels,
}

/// <summary>
/// A link into an existing screen for a search the assistant actually ran (spec 003, FR-002 and FR-003).
/// Built from tool calls, never from the model's text. The trip values are what the screen should open with.
/// </summary>
public sealed record AssistantCard(
    CardKind Kind,
    string DestinationId,
    string Title,
    string Subtitle,
    DateOnly? From = null,
    DateOnly? To = null,
    int? Adults = null,
    string? OriginId = null,
    PlaceCategory? Category = null)
{
    /// <summary>"10 – 15 Apr", or "28 Apr – 2 May" across months.</summary>
    internal static string Describe(DateRange dates) => dates.From.Month == dates.To.Month && dates.From.Year == dates.To.Year
        ? string.Create(CultureInfo.InvariantCulture, $"{dates.From:%d} – {dates.To:d MMM}")
        : string.Create(CultureInfo.InvariantCulture, $"{dates.From:d MMM} – {dates.To:d MMM}");

    internal static string Describe(int adults) => adults == 1 ? "1 adult" : string.Create(CultureInfo.InvariantCulture, $"{adults} adults");
}
