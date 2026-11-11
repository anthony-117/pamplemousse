using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>
/// The 21 tools of one answer (spec 010; ADR 0021). Descriptions stay one line, since every call sends them (FR-036).
/// </summary>
public sealed class ReplyTools(SearchTools search, PlanTools plans, ProfileTools profile)
{
    /// <summary>Compact JSON for tool results: indentation would only cost the model tokens.</summary>
    private static readonly JsonSerializerOptions ResultJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    internal IList<AITool> For(ReplyRun run)
    {
        var s = search.For(run);
        var p = plans.For(run);
        var m = profile.For(run);
        return
        [
            F(s.FindCityAsync, "find_city", "Finds cities by name; returns the id the other tools need."),
            F(s.GetOverviewAsync, "get_overview", "Visa, weather, exchange rate, holidays and safety for a city and dates."),
            F(s.ListPlacesAsync, "list_places", "Top sights, restaurants, nature places or nightlife in a city."),
            F(s.ListEventsAsync, "list_events", "Concerts, sports and shows in a city during the dates."),
            F(s.SearchFlightsAsync, "search_flights", "Recent flight prices from a departure city, ranked for the traveller."),
            F(s.SearchHotelsAsync, "search_hotels", "Hotels for the dates with the stay's total, ranked for the traveller. Limited quota: only when asked."),
            F(s.SearchCarsAsync, "search_cars", "Rental cars for the dates at the city centre or an airport, ranked for the traveller."),
            F(p.BuildPlanAsync, "build_plan", "Builds and saves a day-by-day plan with the best hotel and flight."),
            F(p.ListPlansAsync, "list_plans", "The traveller's saved plans."),
            F(p.GetPlanAsync, "get_plan", "One plan's days, items and their ids, offers and total."),
            F(p.UpdatePlanAsync, "update_plan", "Renames a plan, or marks it draft or planned."),
            F(p.RebuildPlanAsync, "rebuild_plan", "Builds a plan again in place with new dates, adults, departure city, pace or budget, keeping the traveller's day edits and chosen offers."),
            F(p.ListPlanAlternativesAsync, "list_plan_alternatives", "The plan's other hotels, flights or cars to swap in, ranked."),
            F(p.SwapPlanOfferAsync, "swap_plan_offer", "Swaps the plan's hotel, flight or car for one of its alternatives."),
            F(p.RemovePlanCarAsync, "remove_plan_car", "Removes the car from a plan."),
            F(p.SuggestDayAlternativesAsync, "suggest_day_alternatives", "Places or events that could replace a day's item or fill a new slot."),
            F(p.ChangeDayItemAsync, "change_day_item", "Replaces, adds or removes an item of a plan's day; times are rebuilt."),
            F(m.GetPreferencesAsync, "get_preferences", "The traveller's six preferences (0-100) and pace."),
            F(m.UpdatePreferencesAsync, "update_preferences", "Changes only the named preferences or the pace."),
            F(m.SaveItemAsync, "save_item", "Saves a hotel, place or event shown in this chat."),
            F(m.ListSavedAsync, "list_saved", "The traveller's saved hotels, places and events."),
        ];
    }

    private static AIFunction F(Delegate method, string name, string description) => AIFunctionFactory.Create(method, name, description, ResultJson);
}
