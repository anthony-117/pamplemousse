using System.Globalization;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Itinerary;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Api.Mappings;

internal static class PlanMappings
{
    public static BuildPlanCommand ToCommand(this BuildPlanRequest r, TravellerId owner)
    {
        var p = r.Preferences;
        return new BuildPlanCommand(
            owner,
            r.DestinationId,
            r.From,
            r.To,
            r.Adults,
            r.Currency,
            r.OriginId,
            p is null ? null : new RankingInput(p.Budget, p.Culture, p.Food, p.Nature, p.Nightlife, p.Comfort, r.TripBudget),
            r.Pace,
            r.TripBudget);
    }

    public static PlanViewModel ToViewModel(this Plan plan)
    {
        var request = plan.Request;
        var w = request.Weights;
        var offers = plan.Offers;
        var built = plan.Built;
        var t = built.Total;
        return new PlanViewModel(
            plan.Id,
            plan.Name,
            plan.Status,
            new PlanDestinationViewModel(request.Destination.Id, request.Destination.Name, request.Destination.CountryCode),
            request.Dates.From,
            request.Dates.To,
            request.Adults,
            request.Currency,
            request.OriginId,
            new WeightsViewModel(w.Budget, w.Culture, w.Food, w.Nature, w.Nightlife, w.Comfort),
            request.Pace.ToText(),
            request.TripBudget,
            built.Match is { } m
                ? new PlanMatchViewModel(
                    m.Percent,
                    m.Reason,
                    [.. m.Parts.Select(x => new PlanMatchPartViewModel(x.Part, x.Weight, x.Percent))],
                    [.. m.Days.Select(x => new DaysFitViewModel(x.Criterion, x.Weight, x.Percent))])
                : null,
            offers.Hotel is { } hotel ? new(Hotel(hotel, plan.Alternatives), plan.Alternatives.HotelsCheckedAt) : null,
            offers.Flight is { } flight ? new(Flight(flight), plan.Alternatives.FlightsCheckedAt) : null,
            offers.Car is { } car ? new(Car(car, plan.Alternatives), plan.Alternatives.CarsCheckedAt) : null,
            new PlanTotalViewModel(t.Amount, t.Currency, t.Flight, t.Hotel, t.Car, t.Events, t.Unpriced, t.OtherCurrency, t.OverBudgetBy),
            [.. built.Days.Select(ToViewModel)],
            built.Notes,
            built.Warnings,
            plan.CreatedAt,
            plan.UpdatedAt);
    }

    public static PlanListViewModel ToViewModel(this PlanList list) => new(
        [.. list.Items.Select(s => s.ToViewModel())],
        list.Count,
        list.Max);

    public static object AlternativesOf(this Plan plan, OfferKind kind)
    {
        var a = plan.Alternatives;
        return kind switch
        {
            OfferKind.Hotel => new PlanAlternativesViewModel<HotelViewModel>(kind, plan.HotelId, a.HotelsCheckedAt, [.. a.Hotels.Select(h => Hotel(h, a))]),
            OfferKind.Flight => new PlanAlternativesViewModel<FlightViewModel>(kind, plan.FlightId, a.FlightsCheckedAt, [.. a.Flights.Select(Flight)]),
            _ => new PlanAlternativesViewModel<CarViewModel>(kind, plan.CarId, a.CarsCheckedAt, [.. (a.Cars ?? []).Select(c => Car(c, a))]),
        };
    }

    private static PlanSummaryViewModel ToViewModel(this PlanSummary s) =>
        new(s.Id, s.Name, s.Status, s.DestinationName, s.From, s.To, s.Total, s.Currency, s.MatchPercent, s.ImageUrl, s.UpdatedAt);

    private static PlanDayViewModel ToViewModel(PlanDay d) => new(
        d.Number,
        d.Date,
        d.Title,
        d.Weather is { } w ? new PlanWeatherViewModel(w.Summary, w.MaxC, w.MinC, w.RainChance) : null,
        d.WalkKm,
        [.. d.Items.Select(i => new PlanItemViewModel(
            i.Kind,
            i.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
            i.Minutes,
            i.Name,
            i.WalkKm,
            i.Price is { } p ? new MoneyViewModel(p.Amount, p.Currency) : null,
            i.Ref is { } r ? new ItemRefViewModel(r.Kind, r.Id, r.Location?.Lat, r.Location?.Lng, r.ImageUrl, r.Url) : null))]);

    private static HotelViewModel Hotel(Ranked<HotelOffer> h, PlanAlternatives a) =>
        h.Offer.ToViewModel(a.HotelSource ?? "", One(h.Offer.Hotel.Id, h.Match));

    private static FlightViewModel Flight(Ranked<FlightOffer> f) => f.Offer.ToViewModel(One(f.Offer.Id, f.Match));

    private static CarViewModel Car(Ranked<CarOffer> c, PlanAlternatives a) =>
        c.Offer.ToViewModel(a.CarBookingUrl ?? "", a.CarSource ?? "", One(c.Offer.Id, c.Match));

    /// <summary>The kept match of one offer, in the shape the list mappings read it from.</summary>
    private static RankingResult One(string id, Match match) =>
        new(Weights.Neutral, null, new Dictionary<string, Match>(StringComparer.Ordinal) { [id] = match }, null);
}
