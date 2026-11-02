namespace Pamplemousse.Api.ViewModels;

public sealed record EventsViewModel(DateRangeViewModel Dates, IReadOnlyList<EventViewModel> Items, IReadOnlyList<string> Warnings);

public sealed record EventViewModel(
    string Id,
    string Name,
    DateOnly Date,
    string? Time,
    string? Venue,
    string? Category,
    MoneyViewModel? PriceFrom,
    string? ImageUrl,
    string Url,
    string Source);

public sealed record MoneyViewModel(decimal Amount, string Currency);
