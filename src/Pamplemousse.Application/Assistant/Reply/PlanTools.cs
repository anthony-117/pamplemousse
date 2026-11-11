using System.ComponentModel;
using System.Globalization;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Itinerary;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>
/// The plan and day-edit tools (spec 010 FR-012 to FR-018): the itinerary's own use cases, so a change through the
/// assistant equals the same change through the routes (SC-002). None of them deletes a plan (FR-014).
/// </summary>
public sealed class PlanTools(
    IReferenceData data,
    ICardJson cards,
    ICommandHandler<BuildPlanCommand, Plan> build,
    IQueryHandler<ListPlansQuery, PlanList> list,
    IQueryHandler<GetPlanQuery, Plan> get,
    ICommandHandler<ChangePlanCommand, Plan> change,
    ICommandHandler<RebuildPlanCommand, Plan> rebuild,
    IQueryHandler<GetAlternativesQuery, Plan> alternatives,
    ICommandHandler<SwapOfferCommand, Plan> swap,
    IQueryHandler<GetDayAlternativesQuery, DayAlternativesAnswer> dayAlternatives,
    ICommandHandler<ChangeDayItemCommand, Plan> changeDay)
{
    internal IReferenceData Data { get; } = data;

    internal ICardJson Cards { get; } = cards;

    internal ICommandHandler<BuildPlanCommand, Plan> Build { get; } = build;

    internal IQueryHandler<ListPlansQuery, PlanList> List { get; } = list;

    internal IQueryHandler<GetPlanQuery, Plan> Get { get; } = get;

    internal ICommandHandler<ChangePlanCommand, Plan> Change { get; } = change;

    internal ICommandHandler<RebuildPlanCommand, Plan> Rebuild { get; } = rebuild;

    internal IQueryHandler<GetAlternativesQuery, Plan> Alternatives { get; } = alternatives;

    internal ICommandHandler<SwapOfferCommand, Plan> Swap { get; } = swap;

    internal IQueryHandler<GetDayAlternativesQuery, DayAlternativesAnswer> DayAlternatives { get; } = dayAlternatives;

    internal ICommandHandler<ChangeDayItemCommand, Plan> ChangeDay { get; } = changeDay;

    internal Run For(ReplyRun run) => new(this, run);

    internal sealed class Run(PlanTools t, ReplyRun run)
    {
        public Task<object> BuildPlanAsync(
            [Description("City id; empty for the trip's city.")] string? cityId = null,
            [Description("First day, yyyy-MM-dd; empty for the trip's.")] DateOnly? from = null,
            [Description("Last day, yyyy-MM-dd; empty for the trip's.")] DateOnly? to = null,
            [Description("Adults, 1 to 8; empty for the trip's.")] int? adults = null,
            [Description("Departure city id; empty for the trip's (no flight without one).")] string? originCityId = null,
            [Description("Total budget for the trip; empty for the trip's.")] decimal? tripBudget = null,
            [Description("'relaxed', 'balanced' or 'packed'; empty for the traveller's.")] string? pace = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "build_plan", "Plans", async () =>
            {
                var city = Tool.City(t.Data, cityId, run);
                var (start, end) = Tool.Dates(run, from, to);
                var w = run.Job.Weights;
                var budget = tripBudget ?? run.Job.TripBudget;
                var plan = await t.Build.HandleAsync(
                    new BuildPlanCommand(
                        run.Owner, city.Id, start, end, adults ?? run.Job.Trip.Adults, run.Job.Trip.Currency, originCityId ?? run.Job.Trip.OriginId,
                        new RankingInput(w.Budget, w.Culture, w.Food, w.Nature, w.Nightlife, w.Comfort, budget), pace ?? run.Job.Pace.ToString().ToLowerInvariant(), budget),
                    ct);
                return Planned("build_plan", plan, plan.Built.Match is { } m
                    ? string.Create(CultureInfo.InvariantCulture, $"Built your plan · {m.Percent} % match")
                    : "Built your plan");
            });

        public Task<object> ListPlansAsync(CancellationToken ct = default) => Tool.RunAsync(run, "list_plans", "Plans", async () =>
        {
            var plans = await t.List.HandleAsync(new ListPlansQuery(run.Owner), ct);
            var full = new List<string>();
            foreach (var summary in plans.Items.Take(AssistantTools.MaxItems))
            {
                full.Add(t.Cards.Plan(await t.Get.HandleAsync(new GetPlanQuery(run.Owner, summary.Id), ct), run.Now));
            }

            run.Step("list_plans", string.Create(CultureInfo.InvariantCulture, $"Found {plans.Count} plans"), true, plans.Count, full);
            return new { plans = plans.Items.Take(AssistantTools.MaxItems).Select(p => new { p.Id, p.Name, status = p.Status.ToString(), city = p.DestinationName, p.From, p.To, p.Total, p.Currency, match = p.MatchPercent }).ToList(), plans.Count };
        });

        public Task<object> GetPlanAsync(
            [Description("Plan id from list_plans or a plan card.")] string planId,
            CancellationToken ct = default) => Tool.RunAsync(run, "get_plan", "Plans", async () =>
            {
                var plan = await t.Get.HandleAsync(new GetPlanQuery(run.Owner, planId), ct);
                run.Step("get_plan", $"Opened {plan.Name}", true, null, [t.Cards.Plan(plan, run.Now)]);
                return Describe(plan);
            });

        public Task<object> UpdatePlanAsync(
            [Description("Plan id.")] string planId,
            [Description("New name, 1 to 60 characters.")] string? name = null,
            [Description("'draft' or 'planned'.")] string? status = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "update_plan", "Plans", async () =>
            {
                var plan = await t.Change.HandleAsync(new ChangePlanCommand(run.Owner, planId, name, status), ct);
                return Planned("update_plan", plan, name is not null ? $"Renamed your plan {plan.Name}" : $"Marked {plan.Name} as {plan.Status.ToString().ToLowerInvariant()}");
            });

        public Task<object> RebuildPlanAsync(
            [Description("Plan id.")] string planId,
            [Description("New first day, yyyy-MM-dd.")] DateOnly? from = null,
            [Description("New last day, yyyy-MM-dd.")] DateOnly? to = null,
            [Description("New number of adults.")] int? adults = null,
            [Description("New departure city id.")] string? originCityId = null,
            [Description("'relaxed', 'balanced' or 'packed'.")] string? pace = null,
            [Description("New total budget.")] decimal? tripBudget = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "rebuild_plan", "Plans", async () =>
            {
                var plan = await t.Rebuild.HandleAsync(new RebuildPlanCommand(run.Owner, planId, from, to, adults, originCityId, null, pace, tripBudget), ct);
                return Planned("rebuild_plan", plan, $"Rebuilt {plan.Name}");
            });

        public Task<object> ListPlanAlternativesAsync(
            [Description("Plan id.")] string planId,
            [Description("'hotel', 'flight' or 'car'.")] string kind,
            CancellationToken ct = default) => Tool.RunAsync(run, "list_plan_alternatives", "Plans", async () =>
            {
                var offerKind = KindOf(kind);
                var plan = await t.Alternatives.HandleAsync(new GetAlternativesQuery(run.Owner, planId, offerKind), ct);
                var cards = t.Cards.PlanOffers(plan, offerKind, AssistantTools.MaxItems, run.Now);
                run.Step("list_plan_alternatives", $"Other {kind.Trim().ToLowerInvariant()}s for {plan.Name}, ranked for you", true, cards.Count, cards);
                return new { choices = cards.Select(CardInfo.Read).Select(c => new { c.Id, c.Name }).ToList(), current = offerKind switch { OfferKind.Hotel => plan.HotelId, OfferKind.Flight => plan.FlightId, _ => plan.CarId } };
            });

        public Task<object> SwapPlanOfferAsync(
            [Description("Plan id.")] string planId,
            [Description("'hotel', 'flight' or 'car'.")] string kind,
            [Description("An id from list_plan_alternatives.")] string offerId,
            CancellationToken ct = default) => Tool.RunAsync(run, "swap_plan_offer", "Plans", async () =>
            {
                var plan = await t.Swap.HandleAsync(new SwapOfferCommand(run.Owner, planId, KindOf(kind), offerId), ct);
                return Planned("swap_plan_offer", plan, $"Swapped the {kind.Trim().ToLowerInvariant()} in {plan.Name}");
            });

        public Task<object> RemovePlanCarAsync(
            [Description("Plan id.")] string planId,
            CancellationToken ct = default) => Tool.RunAsync(run, "remove_plan_car", "Plans", async () =>
            {
                var plan = await t.Swap.HandleAsync(new SwapOfferCommand(run.Owner, planId, OfferKind.Car, null, RemoveCar: true), ct);
                return Planned("remove_plan_car", plan, $"Removed the car from {plan.Name}");
            });

        public Task<object> SuggestDayAlternativesAsync(
            [Description("Plan id.")] string planId,
            [Description("Day number, from 1.")] int day,
            [Description("The item to replace (its id); empty for a new slot.")] string? itemId = null,
            [Description("'sight', 'nature', 'restaurant', 'nightlife' or 'event'; empty for the item's kind.")] string? kind = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "suggest_day_alternatives", "Places", async () =>
            {
                var answer = await t.DayAlternatives.HandleAsync(new GetDayAlternativesQuery(run.Owner, planId, day, itemId, kind), ct);
                var a = answer.Alternatives;
                var city = new CardCity(answer.Plan.Request.Destination.Id, answer.Plan.Request.Destination.Name, answer.Plan.Request.Destination.Centre);
                var cards = t.Cards.Places([.. a.Items.Select(i => i.Item.Place).OfType<Domain.Places.Place>()], city, run.Now)
                    .Concat(t.Cards.Events([.. a.Items.Select(i => i.Item.Event).OfType<Domain.TravelEvents.TravelEvent>()], city, run.Now))
                    .ToList();
                run.Step("suggest_day_alternatives", a.Message ?? string.Create(CultureInfo.InvariantCulture, $"Found {a.Items.Count} options for day {day}, nearest first"), a.Items.Count > 0, a.Items.Count, cards);
                return new
                {
                    a.Message,
                    options = a.Items.Select(i => new { i.Item.Id, i.Item.Name, kind = i.Item.Kind.ToString().ToLowerInvariant(), walkKm = Math.Round(i.WalkKmFromDay, 1) }).ToList(),
                };
            });

        public Task<object> ChangeDayItemAsync(
            [Description("Plan id.")] string planId,
            [Description("Day number, from 1.")] int day,
            [Description("'replace', 'add' or 'remove'.")] string action,
            [Description("For replace and remove: the item's id on that day.")] string? itemId = null,
            [Description("For replace and add: an id from suggest_day_alternatives.")] string? newItemId = null,
            [Description("For replace and add: 'place' or 'event'.")] string? newItemKind = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "change_day_item", "Plans", async () =>
            {
                var change = action?.Trim().ToUpperInvariant() switch
                {
                    "REMOVE" => new ChangeDayItemCommand(run.Owner, planId, day, DayChange.Remove, null, itemId, null),
                    "REPLACE" => new ChangeDayItemCommand(run.Owner, planId, day, DayChange.AddOrReplace, newItemKind ?? "place", newItemId, itemId),
                    "ADD" => new ChangeDayItemCommand(run.Owner, planId, day, DayChange.AddOrReplace, newItemKind ?? "place", newItemId, null),
                    _ => throw new DomainException("Use replace, add or remove."),
                };
                var plan = await t.ChangeDay.HandleAsync(change, ct);
                return Planned("change_day_item", plan, string.Create(CultureInfo.InvariantCulture, $"Changed day {day} of {plan.Name}"));
            });

        /// <summary>A plan built or changed: a step with its card, and the app reloads it (planChanged).</summary>
        private object Planned(string tool, Plan plan, string summary)
        {
            run.Changed(plan.Id);
            run.Step(tool, summary, true, null, [t.Cards.Plan(plan, run.Now)]);
            return Describe(plan);
        }

        /// <summary>What the model needs to talk about a plan and change it, kept small (FR-036).</summary>
        private static object Describe(Plan plan) => new
        {
            plan.Id,
            plan.Name,
            status = plan.Status.ToString().ToLowerInvariant(),
            city = plan.Request.Destination.Name,
            plan.Request.Dates.From,
            plan.Request.Dates.To,
            plan.Request.Adults,
            pace = plan.Request.Pace.ToString().ToLowerInvariant(),
            total = plan.Built.Total.Amount,
            plan.Built.Total.Currency,
            overBudgetBy = plan.Built.Total.OverBudgetBy,
            match = plan.Built.Match?.Percent,
            hotel = plan.Offers.Hotel?.Offer.Hotel.Name,
            flight = plan.Offers.Flight is { } f ? $"{f.Offer.OriginAirport} → {f.Offer.DestinationAirport}" : null,
            car = plan.Offers.Car?.Offer.Model,
            days = plan.Built.Days.Select(d => new
            {
                d.Number,
                d.Title,
                items = d.Items
                    .Where(i => i.Kind is not (ItemKind.FreeTime or ItemKind.Arrival or ItemKind.Departure))
                    .Select(i => new { id = i.Ref?.Id, i.Name, kind = i.Kind.ToString().ToLowerInvariant(), start = i.Start.ToString("HH:mm", CultureInfo.InvariantCulture) })
                    .ToList(),
            }).ToList(),
            plan.Built.Notes,
        };

        private static OfferKind KindOf(string? kind) => kind?.Trim().ToUpperInvariant() switch
        {
            "HOTEL" => OfferKind.Hotel,
            "FLIGHT" => OfferKind.Flight,
            "CAR" => OfferKind.Car,
            _ => throw new DomainException("Use hotel, flight or car."),
        };
    }
}
