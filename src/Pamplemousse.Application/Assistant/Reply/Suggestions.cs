namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>
/// The 0–3 next steps of a reply (spec 010 FR-006), chosen by the server from what the tools did, so a suggestion never
/// offers something that wasn't found (SC-006). Each is under 60 characters.
/// </summary>
internal static class Suggestions
{
    public const int Max = 3;

    public static IReadOnlyList<string> For(IReadOnlySet<string> tools, bool answered)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (!answered || tools.Count == 0)
        {
            return [];
        }

        var next = new List<string>();
        void Offer(bool when, string text)
        {
            if (when && !next.Contains(text, StringComparer.Ordinal))
            {
                next.Add(text);
            }
        }

        var planned = tools.Overlaps(["build_plan", "rebuild_plan", "swap_plan_offer", "change_day_item", "remove_plan_car"]);
        Offer(planned && !tools.Contains("remove_plan_car"), "Add a car to my plan");
        Offer(planned, "Show other hotels for my plan");
        Offer(planned, "Make my days more relaxed");
        Offer(tools.Contains("search_hotels"), "Only hotels with free cancellation");
        Offer(tools.Contains("search_hotels") && !tools.Contains("search_flights"), "Find flights too");
        Offer(tools.Contains("search_flights") && !tools.Contains("search_hotels"), "Find hotels too");
        Offer(tools.Contains("search_flights") || tools.Contains("search_hotels"), "Build a plan with these");
        Offer(tools.Contains("search_cars"), "Show the cheapest cars");
        Offer(tools.Contains("list_places"), "Plan a day around these");
        Offer(tools.Contains("list_events"), "Save the first event");
        Offer(tools.Contains("get_overview"), "Show top sights there");
        Offer(tools.Contains("list_saved"), "Build a plan with my saved places");
        return [.. next.Take(Max)];
    }
}
