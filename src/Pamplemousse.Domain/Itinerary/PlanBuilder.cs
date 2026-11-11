using System.Globalization;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Domain.Itinerary;

/// <summary>
/// The starting values of research R-2 to R-6, in minutes from midnight unless said otherwise. They can change without
/// changing the API (FR-005).
/// </summary>
public static class PlanRules
{
    public const int DayStart = 9 * 60;
    public const int DayEnd = 22 * 60;
    public const int PlacesEnd = (18 * 60) + 30;
    public const int NoFlightStart = 14 * 60;
    public const int NoFlightEnd = 12 * 60;
    public const int MinutesPerSlot = 3 * 60;

    public const int SightMinutes = 120;
    public const int PackedSightMinutes = 90;
    public const int NatureMinutes = 90;

    public const int LunchFrom = 12 * 60;
    public const int LunchTarget = (12 * 60) + 30;
    public const int LunchLatest = 14 * 60;
    public const int LunchMinutes = 60;
    public const int DinnerAt = 19 * 60;
    public const int DinnerEarliest = (17 * 60) + 30;
    public const int DinnerMinutes = 75;
    public const int NightlifeFrom = 20 * 60;
    public const int NightlifeMinutes = 60;
    public const int EventDefault = 20 * 60;
    public const int EventLatest = 21 * 60;
    public const int EventMinutes = 120;
    public const int ShortestEvent = 60;
    public const int FreeGap = 120;

    public const int AfterLanding = 60;
    public const int AirportToHotel = 45;
    public const int BeforeDeparture = 3 * 60;
    public const int UnknownFlightMinutes = 4 * 60;

    public const double WalkFactor = 1.3;
    public const double WalkKmh = 4.5;
    public const double FoodNearKm = 0.8;

    /// <summary>A weight under this is Low; the app's Low sends 10 or 20.</summary>
    public const int LowBelow = 35;

    /// <summary>A weight from this is High; the app's High sends 90.</summary>
    public const int HighFrom = 70;

    /// <summary>Walking distance: a straight line × 1.3 (spec Assumptions).</summary>
    public static double WalkKm(GeoPoint from, GeoPoint to) => Math.Round(from.DistanceKmTo(to) * WalkFactor, 2);

    /// <summary>At 4.5 km/h, rounded up to 5 minutes.</summary>
    public static int WalkMinutes(double km) => km <= 0 ? 0 : (int)Math.Ceiling(km / WalkKmh * 60 / 5) * 5;
}

/// <summary>
/// Builds the days, total and match of a plan by fixed rules (ADR 0002, FR-005, FR-018). The same inputs always give the same
/// plan (SC-005); nothing here calls a source.
/// </summary>
public static class PlanBuilder
{
    /// <summary>
    /// Builds the plan; <paramref name="edits"/> are the traveller's day edits (spec 010, research R-5), applied while the
    /// days are written. <see cref="BuiltPlan.Applied"/> says which of them took effect.
    /// </summary>
    public static BuiltPlan Build(TripRequest trip, PlanOffers offers, PlacePools pools, IReadOnlyList<PlanEdit>? edits = null)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(pools);

        var notes = new List<string>();
        var warnings = new List<string>(pools.Warnings);
        if (offers.Hotel is null)
        {
            warnings.Add("No hotel was found, so the days start from the city centre and the total has no hotel.");
        }

        var book = new EditBook(edits ?? []);
        var context = new Context(trip, offers, pools, book);
        var frames = Frames(trip, offers, notes);
        var slots = frames.Sum(f => f.Slots);
        var (picks, sightsAsked, natureAsked) = Choose(trip, pools, slots);
        var (groups, seeds) = Group(picks, frames, context.Hotel);
        PinEdits(book, frames, groups, seeds, context.Hotel);
        AddPlaces(book, frames, groups, context.Hotel);
        var nightlifeDays = NightlifeDays(trip.Days, trip.Weights.Nightlife);

        var days = frames
            .Select(f => new DayWriter(context, f, groups[f.Index], nightlifeDays.Contains(f.Index)).Write(seeds[f.Index]))
            .ToList();

        if (picks.Count < slots)
        {
            notes.Add("There weren't enough places to fill every day, so some time is free.");
        }

        if (context.Dropped > 0)
        {
            notes.Add("Some places didn't fit in the day's time and were left out.");
        }

        if (context.MealsMissing > 0)
        {
            notes.Add("No restaurant was found for some meals.");
        }

        if (offers.Flight is not null && trip.Adults > 1)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Flight prices are per traveller, so the total counts the flight for each of the {trip.Adults} travellers."));
        }

        if (book.Applied.Count < book.All.Count)
        {
            notes.Add("Some of your changes to the days no longer fit the plan and were dropped.");
        }

        return new BuiltPlan(days, Totals(trip, offers, days), Match(trip, offers, context, (sightsAsked, natureAsked, nightlifeDays.Count)), notes, warnings)
        {
            Applied = book.Applied,
        };
    }

    /// <summary>
    /// Keeps each replacement and removal on its day when a rebuild groups the places differently (research R-5: edits
    /// last through rebuilds). An item grouped on another day comes back to the edit's day, trading places with its
    /// lowest-ranked pick; a sight or nature place replacing an item the build didn't choose at all takes that pick's
    /// slot instead. A removal of an item the build didn't choose holds as it is.
    /// </summary>
    private static void PinEdits(EditBook book, List<Frame> frames, List<Pick>[] groups, Place?[] seeds, GeoPoint hotel)
    {
        foreach (var edit in book.All.Where(e => e.Action != EditAction.Add && e.ItemId is not null && e.Day >= 1 && e.Day <= groups.Length))
        {
            var day = edit.Day - 1;
            if (groups[day].Exists(p => string.Equals(p.Place.Id, edit.ItemId, StringComparison.Ordinal)))
            {
                continue;
            }

            var from = Array.FindIndex(groups, g => g.Exists(p => string.Equals(p.Place.Id, edit.ItemId, StringComparison.Ordinal)));
            var lowest = groups[day]
                .Where(p => p.Edit is null && !ReferenceEquals(p.Place, seeds[day])
                    && !book.All.Any(e => e.Day == edit.Day && string.Equals(e.ItemId, p.Place.Id, StringComparison.Ordinal)))
                .MaxBy(p => p.RankKey);
            var room = groups[day].Count < frames.First(f => f.Index == day).Slots;
            if (from >= 0)
            {
                var pick = groups[from].First(p => string.Equals(p.Place.Id, edit.ItemId, StringComparison.Ordinal));
                if (lowest is null && !room)
                {
                    continue;
                }

                groups[from].Remove(pick);
                if (lowest is not null)
                {
                    groups[day].Remove(lowest);
                    groups[from].Add(lowest);
                }

                groups[day].Add(pick);
                groups[from] = NearestFirst(groups[from], hotel);
                groups[day] = NearestFirst(groups[day], hotel);
            }
            else if (edit.Action == EditAction.Remove)
            {
                book.Done(edit);
            }
            else if (edit.NewItem?.Place is { } place && KindOf(place) is ItemKind.Sight or ItemKind.Nature && (room || lowest is not null))
            {
                if (!room)
                {
                    groups[day].Remove(lowest!);
                }

                groups[day] = NearestFirst([.. groups[day], new Pick(place, KindOf(place), 0, edit)], hotel);
            }
        }
    }

    /// <summary>Places the traveller added go in their day's free slots: a removed place frees its slot (research R-5).</summary>
    private static void AddPlaces(EditBook book, List<Frame> frames, List<Pick>[] groups, GeoPoint hotel)
    {
        foreach (var frame in frames)
        {
            var day = frame.Index + 1;
            var adds = book.All.Where(e => e.Day == day && e.Action == EditAction.Add && e.NewItem?.Place is not null).ToList();
            if (adds.Count == 0)
            {
                continue;
            }

            var kept = groups[frame.Index].Count(p => book.Find(day, EditAction.Remove, p.Place.Id) is null);
            var room = Math.Max(0, frame.Slots - kept);
            var added = adds.Take(room).Select(e => new Pick(e.NewItem!.Place!, KindOf(e.NewItem.Place!), 0, e));
            groups[frame.Index] = NearestFirst([.. groups[frame.Index], .. added], hotel);
        }
    }

    /// <summary>A place's kind as a visit: a sight, a nature place, a restaurant or an evening out.</summary>
    internal static ItemKind KindOf(Place place) => place.Category switch
    {
        PlaceCategory.Nature => ItemKind.Nature,
        PlaceCategory.Restaurant => ItemKind.Restaurant,
        PlaceCategory.Nightlife => ItemKind.Nightlife,
        _ => ItemKind.Sight,
    };

    /// <summary>The day edits of one build, and which of them took effect.</summary>
    private sealed class EditBook(IReadOnlyList<PlanEdit> edits)
    {
        private readonly HashSet<string> _new = new(edits.Select(e => e.NewItem?.Id).OfType<string>(), StringComparer.Ordinal);
        private readonly List<PlanEdit> _applied = [];

        public IReadOnlyList<PlanEdit> All { get; } = edits;

        /// <summary>In the order the traveller made them.</summary>
        public IReadOnlyList<PlanEdit> Applied => [.. All.Where(_applied.Contains)];

        /// <summary>An item a replacement or addition brings: it is never chosen anywhere else.</summary>
        public bool IsNew(string id) => _new.Contains(id);

        public PlanEdit? Find(int day, EditAction action, string itemId) =>
            All.FirstOrDefault(e => e.Day == day && e.Action == action && string.Equals(e.ItemId, itemId, StringComparison.Ordinal));

        public PlanEdit? EventAdd(int day) => All.FirstOrDefault(e => e.Day == day && e.Action == EditAction.Add && e.NewItem?.Event is not null);

        public bool IsDone(PlanEdit edit) => _applied.Contains(edit);

        /// <summary>
        /// A replacement of this day by a place of <paramref name="kind"/> that hasn't found its item yet: the rebuild
        /// chose another restaurant or evening out, and the traveller's choice takes the last one of the day instead.
        /// </summary>
        public PlanEdit? Loose(int day, ItemKind kind) => All.FirstOrDefault(e => e.Day == day
            && e.Action == EditAction.Replace
            && e.NewItem?.Place is { } place
            && KindOf(place) == kind
            && !_applied.Contains(e));

        public void Done(PlanEdit edit)
        {
            if (!_applied.Contains(edit))
            {
                _applied.Add(edit);
            }
        }
    }

    /// <summary>
    /// Keeps the days of <paramref name="previous"/> and works out the total and match again for new offers: used when a swap
    /// can't fetch the places again (research R-7).
    /// </summary>
    public static BuiltPlan Reprice(TripRequest trip, PlanOffers offers, BuiltPlan previous, string warning)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(previous);
        var match = previous.Match is null ? null : RepriceMatch(trip, offers, previous.Match);
        return previous with
        {
            Total = Totals(trip, offers, previous.Days),
            Match = match,
            Warnings = [.. previous.Warnings.Where(w => !string.Equals(w, warning, StringComparison.Ordinal)), warning],
        };
    }

    // ---- Day frames: when each day starts and ends, and how many places it holds (R-2, R-5) ----

    private sealed record Frame(int Index, DateOnly Date, int Start, int End, bool IsArrival, bool IsDeparture, int Slots, PlanItem? Arrival, PlanItem? Departure);

    private static List<Frame> Frames(TripRequest trip, PlanOffers offers, List<string> notes)
    {
        var flight = offers.Flight?.Offer;
        int firstStart;
        int lastEnd;
        PlanItem? arrival = null;
        PlanItem? departure = null;
        if (flight is null)
        {
            firstStart = PlanRules.NoFlightStart;
            lastEnd = PlanRules.NoFlightEnd;
            notes.Add(trip.OriginId is null
                ? "Choose a departure city to add a flight. Without one, the first day starts at 14:00 and the last day ends at 12:00."
                : "No recent fare was found for these dates, so the plan has no flight. The first day starts at 14:00 and the last day ends at 12:00.");
        }
        else
        {
            var minutes = flight.DurationMinutes ?? offers.FallbackFlightMinutes ?? PlanRules.UnknownFlightMinutes;
            if (flight.DurationMinutes is null)
            {
                notes.Add(offers.FallbackFlightMinutes is null
                    ? "The flight time isn't known, so landing is estimated 4 hours after take-off."
                    : "The flight time isn't known, so landing is estimated from the fastest fare on the same route.");
            }

            var landing = LocalMinutes(flight.DepartureAt.AddMinutes(minutes), trip.UtcOffset);
            var reference = FlightRef(flight);
            arrival = new PlanItem(
                ItemKind.Arrival, Time(landing), PlanRules.AfterLanding + PlanRules.AirportToHotel, $"Land at {flight.DestinationAirport}, then to the hotel", 0, null, reference);
            var ready = landing + PlanRules.AfterLanding + PlanRules.AirportToHotel;
            firstStart = ready >= 24 * 60 ? PlanRules.DayEnd : RoundUp(ready, 15);
            notes.Add("The time from the airport to the hotel is estimated at 45 minutes.");

            if (flight.ReturnAt is { } back)
            {
                var leave = LocalMinutes(back, trip.UtcOffset);
                lastEnd = Math.Max(0, leave - PlanRules.BeforeDeparture);
                departure = new PlanItem(
                    ItemKind.Departure, Time(lastEnd), Math.Min(PlanRules.BeforeDeparture, leave), $"To {flight.DestinationAirport} for the {Clock(leave)} flight home", 0, null, reference);
            }
            else
            {
                lastEnd = PlanRules.NoFlightEnd;
                notes.Add("The fare is one way, so the last day ends at 12:00.");
            }
        }

        var perDay = trip.Pace.PlacesPerDay();
        var frames = new List<Frame>(trip.Days);
        for (var i = 0; i < trip.Days; i++)
        {
            var isArrival = i == 0;
            var isDeparture = i == trip.Days - 1;
            var start = Math.Max(isArrival ? firstStart : PlanRules.DayStart, PlanRules.DayStart);
            var end = isDeparture ? lastEnd : PlanRules.DayEnd;
            var room = Math.Min(end, PlanRules.PlacesEnd) - start;
            var slots = isArrival || isDeparture ? Math.Clamp(room / PlanRules.MinutesPerSlot, 0, perDay) : perDay;
            frames.Add(new Frame(i, trip.Dates.From.AddDays(i), start, end, isArrival, isDeparture, slots, isArrival ? arrival : null, isDeparture ? departure : null));
        }

        return frames;
    }

    private static int LocalMinutes(DateTimeOffset at, TimeSpan offset)
    {
        var local = at.ToUniversalTime().DateTime + offset;
        return (local.Hour * 60) + local.Minute;
    }

    private static int RoundUp(int minutes, int step) => (minutes + step - 1) / step * step;

    private static TimeOnly Time(int minutes) => new(Math.Clamp(minutes, 0, (24 * 60) - 1) / 60, Math.Clamp(minutes, 0, (24 * 60) - 1) % 60);

    private static string Clock(int minutes) => Time(minutes).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static ItemRef FlightRef(FlightOffer flight) => new(RefKind.Flight, flight.Id, null, null, flight.BookingUrl);

    // ---- Which places (R-2) and how they are grouped into days (R-3) ----

    private sealed record Pick(Place Place, ItemKind Kind, double RankKey, PlanEdit? Edit = null);

    private static (List<Pick> Picks, int SightsAsked, int NatureAsked) Choose(TripRequest trip, PlacePools pools, int slots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sights = Distinct(pools.Sights, seen);
        var nature = Distinct(pools.Nature, seen);

        var (culture, natureWeight) = (trip.Weights.Culture, trip.Weights.Nature);
        var share = culture + natureWeight == 0 ? 0.5 : (double)culture / (culture + natureWeight);
        var sightsWanted = (int)Math.Round(slots * share, MidpointRounding.AwayFromZero);
        var s = Math.Min(sightsWanted, sights.Count);
        var n = Math.Min(slots - sightsWanted, nature.Count);

        // Fewer of one kind than asked: the other fills the slots (spec Edge Cases: free time only when both run out).
        var spare = slots - s - n;
        if (spare > 0)
        {
            var more = Math.Min(spare, sights.Count - s);
            s += more;
            n += Math.Min(spare - more, nature.Count - n);
        }

        List<Pick> picks = [.. sights.Take(s).Select((p, i) => new Pick(p, ItemKind.Sight, (double)i / s))
            .Concat(nature.Take(n).Select((p, i) => new Pick(p, ItemKind.Nature, (double)i / n)))
            .OrderBy(p => p.RankKey)
            .ThenBy(p => p.Kind)
            .ThenBy(p => p.Place.Id, StringComparer.Ordinal)];
        return (picks, sightsWanted, slots - sightsWanted);
    }

    /// <summary>The same place can come from two sources under two ids, so names count too.</summary>
    private static List<Place> Distinct(IEnumerable<Place> places, HashSet<string> seen) =>
        [.. places.Where(p => !string.IsNullOrWhiteSpace(p.Name) && seen.Add("id:" + p.Id) && seen.Add("name:" + p.Name.Trim()))];

    private static (List<Pick>[] Groups, Place?[] Seeds) Group(List<Pick> picks, List<Frame> frames, GeoPoint hotel)
    {
        var groups = frames.Select(_ => new List<Pick>()).ToArray();
        var seeds = new Place?[frames.Count];
        var left = new List<Pick>(picks);
        foreach (var frame in frames.OrderByDescending(f => f.Slots).ThenBy(f => f.Index))
        {
            if (frame.Slots == 0 || left.Count == 0)
            {
                continue;
            }

            var seed = left[0];
            left.RemoveAt(0);
            var group = new List<Pick> { seed };
            while (group.Count < frame.Slots && left.Count > 0)
            {
                var next = left.MinBy(p => seed.Place.Location.DistanceKmTo(p.Place.Location))!;
                left.Remove(next);
                group.Add(next);
            }

            groups[frame.Index] = NearestFirst(group, hotel);
            seeds[frame.Index] = seed.Place;
        }

        return (groups, seeds);
    }

    private static List<Pick> NearestFirst(List<Pick> group, GeoPoint from)
    {
        var ordered = new List<Pick>(group.Count);
        var left = new List<Pick>(group);
        var here = from;
        while (left.Count > 0)
        {
            var next = left.MinBy(p => here.DistanceKmTo(p.Place.Location))!;
            left.Remove(next);
            ordered.Add(next);
            here = next.Place.Location;
        }

        return ordered;
    }

    /// <summary>Evenings out on round(days × nightlife ÷ 100) days, the latest first, never the departure day (R-2).</summary>
    private static HashSet<int> NightlifeDays(int days, int nightlife)
    {
        var asked = (int)Math.Round(days * nightlife / 100.0, MidpointRounding.AwayFromZero);
        return [.. Enumerable.Range(0, Math.Max(0, days - 1)).Reverse().Take(asked)];
    }

    // ---- What the whole plan shares while its days are written ----

    private sealed class Context(TripRequest trip, PlanOffers offers, PlacePools pools, EditBook book)
    {
        // What the edits bring is only placed by its edit, never chosen as the nearest restaurant or evening out.
        private readonly HashSet<string> _used = new(book.All.Select(e => e.NewItem?.Id).OfType<string>(), StringComparer.Ordinal);
        private readonly int _wellKnownCount = Math.Max(1, pools.Restaurants.Count / 4);

        public TripRequest Trip { get; } = trip;

        public EditBook Book { get; } = book;

        public PlacePools Pools { get; } = pools;

        public GeoPoint Hotel { get; } = offers.Hotel?.Offer.Hotel.Location ?? trip.Destination.Centre;

        public bool FoodHigh => Trip.Weights.Food >= PlanRules.HighFrom;

        public bool EventsWanted => Trip.Weights.Culture >= PlanRules.LowBelow || Trip.Weights.Nightlife >= PlanRules.LowBelow;

        public int Dropped { get; set; }

        public int Meals { get; set; }

        public int MealsMissing { get; set; }

        public int MealsWellKnown { get; set; }

        public int Evenings { get; set; }

        public int Sights { get; set; }

        public int NatureSites { get; set; }

        /// <summary>The nearest unused restaurant; at Food High, the best-known within 800 m first (R-2). Not yet marked _used.</summary>
        public (Place Place, bool WellKnown)? Restaurant(GeoPoint from)
        {
            var list = Pools.Restaurants;
            var free = list.Select((p, i) => (Place: p, Index: i)).Where(x => !_used.Contains(x.Place.Id)).ToList();
            if (free.Count == 0)
            {
                return null;
            }

            if (FoodHigh)
            {
                var known = free.FirstOrDefault(x => from.DistanceKmTo(x.Place.Location) <= PlanRules.FoodNearKm);
                if (known.Place is not null)
                {
                    return (known.Place, known.Index < _wellKnownCount);
                }
            }

            var nearest = free.MinBy(x => from.DistanceKmTo(x.Place.Location));
            return (nearest.Place, nearest.Index < _wellKnownCount);
        }

        public Place? NightlifePlace(GeoPoint from) =>
            Pools.Nightlife.Where(p => !_used.Contains(p.Id)).MinBy(p => from.DistanceKmTo(p.Location));

        public void Use(Place place) => _used.Add(place.Id);

        /// <summary>The first event on the day by start time, then id, that fits the day (FR-008, R-2).</summary>
        public (TravelEvent Event, int Start)? EventFor(DateOnly date, int dayStart, int dayEnd)
        {
            if (!EventsWanted)
            {
                return null;
            }

            return Pools.Events
                .Where(e => e.Date == date && !Book.IsNew(e.Id))
                .Select(e => (Event: e, Start: EventStart(e.Time)))
                .Where(x => EventFits(x.Start, dayStart, dayEnd))
                .OrderBy(x => x.Start)
                .ThenBy(x => x.Event.Id, StringComparer.Ordinal)
                .Select(x => ((TravelEvent, int)?)x)
                .FirstOrDefault();
        }

    }

    private static int EventStart(string? time) =>
        TimeOnly.TryParse(time, CultureInfo.InvariantCulture, out var t) ? (t.Hour * 60) + t.Minute : PlanRules.EventDefault;

    private static bool EventFits(int start, int dayStart, int dayEnd) =>
        start <= PlanRules.EventLatest && start >= dayStart && start + PlanRules.ShortestEvent <= Math.Min(dayEnd, PlanRules.DayEnd);

    // ---- One day's timeline (R-4) ----

    private sealed class DayWriter(Context context, Frame frame, List<Pick> places, bool nightlifeDay)
    {
        private readonly List<PlanItem> _items = [];
        private readonly int _day = frame.Index + 1;
        private readonly (TravelEvent Event, int Start, PlanEdit? Edit)? _dayEvent = DayEvent(context, frame);
        private int _cursor = frame.Start;
        private GeoPoint _here = context.Hotel;
        private bool _eventDone;
        private bool _eveningEvent;

        public PlanDay Write(Place? seed)
        {
            if (frame.Arrival is { } arrival)
            {
                _items.Add(arrival);
            }

            var lunchAllowed = frame.Start <= PlanRules.LunchLatest && frame.End >= PlanRules.LunchFrom + PlanRules.LunchMinutes;
            var lunchAfter = lunchAllowed ? LunchAfter() : int.MinValue;
            if (lunchAfter == -1)
            {
                Lunch();
            }

            for (var i = 0; i < places.Count; i++)
            {
                Visit(places[i]);
                if (i == lunchAfter)
                {
                    Lunch();
                }
            }

            Evening();
            if (frame.Departure is { } departure)
            {
                _items.Add(departure);
            }

            var all = FillFreeTime();
            var title = (frame.IsArrival, frame.IsDeparture) switch
            {
                (true, true) => "Arrival and departure",
                (true, false) => "Arrival",
                (false, true) => "Departure",
                _ => seed is null
                    ? string.Create(CultureInfo.InvariantCulture, $"Day {frame.Index + 1} · Free day")
                    : string.Create(CultureInfo.InvariantCulture, $"Day {frame.Index + 1} · {seed.Name} area"),
            };
            var weather = context.Pools.Weather.FirstOrDefault(w => w.Date == frame.Date);
            return new PlanDay(frame.Index + 1, frame.Date, title, weather, Math.Round(all.Sum(i => i.WalkKm), 1), all);
        }

        /// <summary>
        /// Where lunch goes: after the place that ends closest to 12:30 and not before 12:00; -1 is before the first place,
        /// when the day starts after noon.
        /// </summary>
        private int LunchAfter()
        {
            var ends = new List<int>();
            var time = frame.Start;
            var at = _here;
            foreach (var pick in places)
            {
                time += PlanRules.WalkMinutes(PlanRules.WalkKm(at, pick.Place.Location)) + Minutes(pick);
                ends.Add(time);
                at = pick.Place.Location;
            }

            var candidates = Enumerable.Range(-1, places.Count + 1)
                .Select(k => (K: k, End: k == -1 ? frame.Start : ends[k]))
                .Where(x => x.End >= PlanRules.LunchFrom)
                .ToList();
            return candidates.Count > 0
                ? candidates.MinBy(x => (Math.Abs(x.End - PlanRules.LunchTarget), x.K)).K
                : places.Count - 1;
        }

        private int Minutes(Pick pick) => pick.Kind switch
        {
            ItemKind.Nature => PlanRules.NatureMinutes,
            ItemKind.Restaurant => PlanRules.LunchMinutes,
            ItemKind.Nightlife => PlanRules.NightlifeMinutes,
            _ => context.Trip.Pace == Pace.Packed ? PlanRules.PackedSightMinutes : PlanRules.SightMinutes,
        };

        /// <summary>The day's event: the one the rules choose, unless an edit removes or replaces it, or adds one.</summary>
        private static (TravelEvent Event, int Start, PlanEdit? Edit)? DayEvent(Context context, Frame frame)
        {
            var day = frame.Index + 1;
            var book = context.Book;
            if (context.EventFor(frame.Date, frame.Start, frame.End) is { } chosen)
            {
                if (book.Find(day, EditAction.Replace, chosen.Event.Id) is { NewItem.Event: { } other } replacement)
                {
                    var at = EventStart(other.Time);
                    return EventFits(at, frame.Start, frame.End) && other.Date == frame.Date ? (other, at, replacement) : (chosen.Event, chosen.Start, null);
                }

                if (book.Find(day, EditAction.Remove, chosen.Event.Id) is not { } removal)
                {
                    return (chosen.Event, chosen.Start, null);
                }

                // Removed: an event the traveller added that day takes its place.
                book.Done(removal);
            }

            if (book.EventAdd(day) is { NewItem.Event: { } added } addition)
            {
                var at = EventStart(added.Time);
                return EventFits(at, frame.Start, frame.End) && added.Date == frame.Date ? (added, at, addition) : null;
            }

            return null;
        }

        /// <summary>
        /// The place an edit puts instead of <paramref name="place"/> (a replacement), or null when an edit removes it.
        /// <paramref name="edit"/> is the replacement, marked done once the new place is in the day. With
        /// <paramref name="loose"/>, the day's last place of that kind, a replacement whose item the build didn't choose
        /// takes it (research R-5: edits last through rebuilds).
        /// </summary>
        private Place? Edited(Place place, out PlanEdit? edit, ItemKind? loose = null)
        {
            edit = null;
            var book = context.Book;
            if (book.Find(_day, EditAction.Remove, place.Id) is { } removal)
            {
                book.Done(removal);
                context.Use(place);
                return null;
            }

            if (book.Find(_day, EditAction.Replace, place.Id) is { NewItem.Place: { } other } replacement && !book.IsDone(replacement))
            {
                context.Use(place);
                edit = replacement;
                return other;
            }

            if (loose is { } kind && book.Loose(_day, kind) is { NewItem.Place: { } chosen } orphan)
            {
                context.Use(place);
                edit = orphan;
                return chosen;
            }

            return place;
        }

        private void Visit(Pick pick)
        {
            if (pick.Edit is null)
            {
                if (context.Book.IsNew(pick.Place.Id))
                {
                    // Kept for the edit that brings it to its own day.
                    return;
                }

                if (Edited(pick.Place, out var replacement) is not { } edited)
                {
                    return;
                }

                pick = replacement is null ? pick : new Pick(edited, KindOf(edited), pick.RankKey, replacement);
            }

            var place = pick.Place;
            var km = PlanRules.WalkKm(_here, place.Location);
            var start = _cursor + PlanRules.WalkMinutes(km);
            var minutes = Minutes(pick);
            if (EventBefore(start + minutes))
            {
                start = _cursor + PlanRules.WalkMinutes(km);
            }

            if (start + minutes > Math.Min(frame.End, PlanRules.PlacesEnd))
            {
                context.Dropped++;
                return;
            }

            Add(pick.Kind, start, minutes, place.Name, km, null, PlaceRef(place));
            context.Use(place);
            if (pick.Edit is { } edit)
            {
                context.Book.Done(edit);
            }

            if (pick.Kind == ItemKind.Nature)
            {
                context.NatureSites++;
            }
            else if (pick.Kind == ItemKind.Sight)
            {
                context.Sights++;
            }

            _here = place.Location;
        }

        /// <summary>The meal's restaurant after the day's edits; null when an edit removed the meal.</summary>
        private (Place Place, bool WellKnown, PlanEdit? Edit)? EditedMeal((Place Place, bool WellKnown) choice, out bool removed, bool last = false)
        {
            removed = false;
            if (Edited(choice.Place, out var edit, last ? ItemKind.Restaurant : null) is not { } place)
            {
                removed = true;
                return null;
            }

            // The traveller's own choice counts as well known.
            return edit is null ? (choice.Place, choice.WellKnown, null) : (place, true, edit);
        }

        private void Lunch()
        {
            context.Meals++;
            if (context.Restaurant(_here) is not { } found)
            {
                context.MealsMissing++;
                return;
            }

            if (EditedMeal(found, out _) is not { } choice)
            {
                context.Meals--;
                return;
            }

            var km = PlanRules.WalkKm(_here, choice.Place.Location);
            var start = Math.Max(_cursor + PlanRules.WalkMinutes(km), PlanRules.LunchFrom);
            if (EventBefore(start + PlanRules.LunchMinutes))
            {
                start = Math.Max(_cursor + PlanRules.WalkMinutes(km), PlanRules.LunchFrom);
            }

            if (start + PlanRules.LunchMinutes > frame.End || start > PlanRules.LunchLatest)
            {
                context.Meals--;
                return;
            }

            Meal("Lunch", start, PlanRules.LunchMinutes, km, choice);
        }

        /// <summary>Dinner, then the day's event, then an evening out (R-2, R-4).</summary>
        private void Evening()
        {
            var pending = _dayEvent is { } e && !_eventDone ? e.Start : (int?)null;
            var dinnerRemoved = false;
            var choice = context.Restaurant(_here) is { } found ? EditedMeal(found, out dinnerRemoved, last: true) : null;
            var km = choice is { } c ? PlanRules.WalkKm(_here, c.Place.Location) : 0;
            var earliest = _cursor + PlanRules.WalkMinutes(km);
            int? dinnerAt = Math.Max(earliest, PlanRules.DinnerAt);
            var dinnerLater = false;
            if (pending is { } es && dinnerAt + PlanRules.DinnerMinutes > es)
            {
                var before = es - PlanRules.DinnerMinutes;
                if (before >= earliest && before >= PlanRules.DinnerEarliest)
                {
                    dinnerAt = before;
                }
                else
                {
                    dinnerAt = null;
                    dinnerLater = true;
                }
            }

            var dinnerFits = (int at) => !dinnerRemoved && at + PlanRules.DinnerMinutes <= Math.Min(frame.End, PlanRules.DayEnd);
            if (dinnerAt is { } at && dinnerFits(at))
            {
                Dinner(at, km, choice);
            }

            PlaceEvent();

            if (dinnerLater)
            {
                var later = _cursor + PlanRules.WalkMinutes(km);
                if (dinnerFits(later))
                {
                    Dinner(later, km, choice);
                }
            }

            if (nightlifeDay && !_eveningEvent)
            {
                NightOut();
            }
            else if (nightlifeDay)
            {
                context.Evenings++;
            }
        }

        private void Dinner(int at, double km, (Place Place, bool WellKnown, PlanEdit? Edit)? choice)
        {
            context.Meals++;
            if (choice is not { } c)
            {
                context.MealsMissing++;
                return;
            }

            Meal("Dinner", at, PlanRules.DinnerMinutes, km, c);
        }

        private void NightOut()
        {
            if (context.NightlifePlace(_here) is not { } found || Edited(found, out var edit, ItemKind.Nightlife) is not { } place)
            {
                return;
            }

            var km = PlanRules.WalkKm(_here, place.Location);
            var start = Math.Max(_cursor + PlanRules.WalkMinutes(km), PlanRules.NightlifeFrom);
            if (start + PlanRules.NightlifeMinutes > Math.Min(frame.End, PlanRules.DayEnd))
            {
                return;
            }

            Add(ItemKind.Nightlife, start, PlanRules.NightlifeMinutes, place.Name, km, null, PlaceRef(place));
            context.Use(place);
            if (edit is not null)
            {
                context.Book.Done(edit);
            }

            context.Evenings++;
            _here = place.Location;
        }

        private void Meal(string meal, int start, int minutes, double km, (Place Place, bool WellKnown, PlanEdit? Edit) choice)
        {
            Add(ItemKind.Restaurant, start, minutes, $"{meal} at {choice.Place.Name}", km, null, PlaceRef(choice.Place));
            context.Use(choice.Place);
            if (choice.Edit is { } edit)
            {
                context.Book.Done(edit);
            }

            if (choice.WellKnown || !context.FoodHigh)
            {
                context.MealsWellKnown++;
            }

            _here = choice.Place.Location;
        }

        /// <summary>Places the day's event first when the next item would run into it; true when it did.</summary>
        private bool EventBefore(int nextEnd)
        {
            if (_dayEvent is not { } e || _eventDone || _cursor > e.Start || nextEnd <= e.Start)
            {
                return false;
            }

            PlaceEvent();
            return true;
        }

        private void PlaceEvent()
        {
            if (_dayEvent is not { } e || _eventDone || _cursor > e.Start)
            {
                return;
            }

            _eventDone = true;
            var end = Math.Min(Math.Min(e.Start + PlanRules.EventMinutes, PlanRules.DayEnd), frame.End);
            if (end - e.Start < PlanRules.ShortestEvent)
            {
                return;
            }

            var ev = e.Event;
            var price = ev.PriceFrom is { } p ? new Money(p.Amount * context.Trip.Adults, p.Currency) : null;
            Add(ItemKind.Event, e.Start, end - e.Start, ev.Name, 0, price, new ItemRef(RefKind.Event, ev.Id, null, ev.ImageUrl, ev.Url));
            if (e.Edit is { } edit)
            {
                context.Book.Done(edit);
            }

            _eveningEvent = e.Start >= PlanRules.PlacesEnd;
        }

        private void Add(ItemKind kind, int start, int minutes, string name, double km, Money? price, ItemRef? reference)
        {
            _items.Add(new PlanItem(kind, Time(start), minutes, name, km, price, reference));
            _cursor = start + minutes;
        }

        /// <summary>Free time in every gap of 2 hours or more, and at the end of the day's sightseeing (R-4).</summary>
        private List<PlanItem> FillFreeTime()
        {
            var ordered = _items.OrderBy(i => i.Start).ThenBy(i => i.Kind).ToList();
            var result = new List<PlanItem>();
            var free = frame.Start;
            foreach (var item in ordered)
            {
                if (item.Kind != ItemKind.Arrival)
                {
                    var gapEnd = Mins(item.Start) - PlanRules.WalkMinutes(item.WalkKm);
                    AddFree(result, free, gapEnd);
                    free = Math.Max(free, Mins(item.Start) + item.Minutes);
                }

                result.Add(item);
            }

            AddFree(result, free, Math.Min(frame.End, PlanRules.DinnerAt));
            return [.. result.OrderBy(i => i.Start).ThenBy(i => i.Kind)];
        }

        private static void AddFree(List<PlanItem> result, int from, int to)
        {
            if (to - from < PlanRules.FreeGap || from >= PlanRules.DayEnd)
            {
                return;
            }

            var name = from < PlanRules.LunchFrom ? "Free morning" : from < 18 * 60 ? "Free afternoon" : "Free evening";
            result.Add(new PlanItem(ItemKind.FreeTime, Time(from), to - from, name, 0, null, null));
        }

        private static int Mins(TimeOnly time) => (time.Hour * 60) + time.Minute;

        private static ItemRef PlaceRef(Place place) => new(RefKind.Place, place.Id, place.Location, place.ImageUrl, place.Website);
    }

    // ---- Total (FR-010) ----

    private static PlanTotal Totals(TripRequest trip, PlanOffers offers, IReadOnlyList<PlanDay> days)
    {
        var currency = trip.Currency;
        var other = 0;
        decimal amount = 0;

        decimal? Part(decimal value, string partCurrency)
        {
            if (!string.Equals(partCurrency, currency, StringComparison.OrdinalIgnoreCase))
            {
                other++;
                return null;
            }

            amount += value;
            return value;
        }

        var flight = offers.Flight is { } f ? Part(f.Offer.Price.Amount * trip.Adults, f.Offer.Price.Currency) : null;
        var hotel = offers.Hotel is { } h ? Part(h.Offer.Price.Total, h.Offer.Price.Currency) : null;
        var car = offers.Car is { } c ? Part(c.Offer.Price.Total, c.Offer.Price.Currency) : null;

        decimal events = 0;
        var unpriced = 0;
        foreach (var item in days.SelectMany(d => d.Items))
        {
            switch (item.Kind)
            {
                case ItemKind.Event when item.Price is { } price:
                    events += Part(price.Amount, price.Currency) ?? 0;
                    break;
                case ItemKind.Event or ItemKind.Sight or ItemKind.Nature or ItemKind.Restaurant or ItemKind.Nightlife:
                    unpriced++;
                    break;
                default:
                    break;
            }
        }

        var over = trip.TripBudget is { } budget && amount > budget ? amount - budget : (decimal?)null;
        return new PlanTotal(amount, currency, flight, hotel, car, events, unpriced, other, over);
    }

    // ---- Match (FR-009, R-6) ----

    /// <summary>Each interest: what it got ÷ what its preference asked for, capped at 1; 1 when it asked for nothing.</summary>
    private static double Fit(int got, int asked) => asked <= 0 ? 1 : Math.Min(1, (double)got / asked);

    private static PlanMatch? Match(TripRequest trip, PlanOffers offers, Context context, (int Sights, int Nature, int Evenings) asked)
    {
        var w = trip.Weights;
        var nightlife = Fit(context.Evenings, asked.Evenings);
        var food = context.Meals == 0 ? 1 : (double)context.MealsWellKnown / context.Meals;

        DaysFitPart[] daysParts =
        [
            new(Criterion.Culture, w.Culture, Fit(context.Sights, asked.Sights)),
            new(Criterion.Nature, w.Nature, Fit(context.NatureSites, asked.Nature)),
            new(Criterion.Nightlife, w.Nightlife, nightlife),
            new(Criterion.Food, w.Food, food),
        ];
        return Combine(trip, offers, daysParts);
    }

    private static PlanMatch? RepriceMatch(TripRequest trip, PlanOffers offers, PlanMatch previous) => Combine(trip, offers, previous.Days);

    private static PlanMatch? Combine(TripRequest trip, PlanOffers offers, IReadOnlyList<DaysFitPart> daysParts)
    {
        var w = trip.Weights;
        var daysWeight = daysParts.Sum(p => p.Weight);
        var daysValue = daysWeight == 0 ? 0 : daysParts.Sum(p => p.Weight * p.Value) / daysWeight;
        var travel = w.Budget + w.Comfort;

        var parts = new List<PlanMatchPart>();
        if (offers.Hotel is { } hotel)
        {
            parts.Add(new PlanMatchPart(MatchPartKind.Hotel, w.Budget + w.Culture + w.Food + w.Nature + w.Nightlife + w.Comfort, hotel.Match.Total));
        }

        if (offers.Flight is { } flight)
        {
            parts.Add(new PlanMatchPart(MatchPartKind.Flight, travel, flight.Match.Total));
        }

        if (offers.Car is { } car)
        {
            parts.Add(new PlanMatchPart(MatchPartKind.Car, travel, car.Match.Total));
        }

        parts.Add(new PlanMatchPart(MatchPartKind.Days, daysWeight, daysValue));
        parts.RemoveAll(p => p.Weight == 0);
        var total = parts.Sum(p => p.Weight);
        if (total == 0)
        {
            return null;
        }

        var value = parts.Sum(p => p.Weight * p.Value) / total;
        return new PlanMatch(value, parts, daysParts, Reason(parts, daysParts));
    }

    /// <summary>The strongest part, then the second strongest or, when one part is weak, that part (R-6).</summary>
    private static string Reason(List<PlanMatchPart> parts, IReadOnlyList<DaysFitPart> days)
    {
        var strongest = parts.OrderByDescending(p => p.Weight * p.Value).ThenBy(p => p.Part).First();
        var weakest = parts.OrderBy(p => p.Value).ThenBy(p => p.Part).First();
        var lead = Strong(strongest, days);
        if (weakest.Value < 0.6 && weakest != strongest)
        {
            return $"{lead}, but {Weak(weakest, days)}";
        }

        var second = parts.Where(p => p != strongest).OrderByDescending(p => p.Weight * p.Value).ThenBy(p => p.Part).FirstOrDefault();
        return second is null ? lead : $"{lead}, {LowerFirst(Strong(second, days))}";
    }

    private static string Strong(PlanMatchPart part, IReadOnlyList<DaysFitPart> days) => part.Part switch
    {
        MatchPartKind.Hotel => "Great hotel for you",
        MatchPartKind.Flight => "Good flight",
        MatchPartKind.Car => "Good car",
        _ => days.Where(d => d.Weight > 0).OrderByDescending(d => d.Weight * d.Value).ThenBy(d => d.Criterion).FirstOrDefault()?.Criterion switch
        {
            Criterion.Nature => "Days full of parks",
            Criterion.Nightlife => "Lively evenings",
            Criterion.Food => "Good food on the way",
            _ => "Days full of sights",
        },
    };

    private static string Weak(PlanMatchPart part, IReadOnlyList<DaysFitPart> days) => part.Part switch
    {
        MatchPartKind.Hotel => "the hotel is a weaker match",
        MatchPartKind.Flight => "the flight is a weaker match",
        MatchPartKind.Car => "the car is a weaker match",
        _ => days.Where(d => d.Weight > 0).OrderBy(d => d.Value).ThenByDescending(d => d.Weight).ThenBy(d => d.Criterion).FirstOrDefault()?.Criterion switch
        {
            Criterion.Nature => "few parks nearby",
            Criterion.Nightlife => "few evenings out",
            Criterion.Food => "few well-known restaurants nearby",
            _ => "few sights nearby",
        },
    };

    private static string LowerFirst(string phrase) => char.ToLowerInvariant(phrase[0]) + phrase[1..];
}
