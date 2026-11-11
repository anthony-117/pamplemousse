using System.ComponentModel;
using System.Globalization;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Saved;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Saved;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>
/// Preferences and saved items (spec 010 FR-019, FR-020). Removing a saved item stays an app action (FR-014).
/// </summary>
public sealed class ProfileTools(
    ITravelProfileStore profiles,
    ICardJson cards,
    TimeProvider clock,
    ICommandHandler<SaveItemCommand, SavedItem> save,
    IQueryHandler<ListSavedQuery, SavedList> listSaved)
{
    internal ITravelProfileStore Profiles { get; } = profiles;

    internal ICardJson Cards { get; } = cards;

    internal TimeProvider Clock { get; } = clock;

    internal ICommandHandler<SaveItemCommand, SavedItem> Save { get; } = save;

    internal IQueryHandler<ListSavedQuery, SavedList> ListSaved { get; } = listSaved;

    internal Run For(ReplyRun run) => new(this, run);

    internal sealed class Run(ProfileTools t, ReplyRun run)
    {
        public Task<object> GetPreferencesAsync(CancellationToken ct = default) => Tool.RunAsync(run, "get_preferences", "Preferences", async () =>
        {
            var profile = await t.Profiles.GetAsync(run.Owner, ct);
            run.Step("get_preferences", "Read your travel preferences", true);
            return profile is null
                ? new { note = "No saved profile yet: every preference is 50 and the pace balanced.", preferences = Of(Weights.Neutral), pace = "balanced" }
                : (object)new { preferences = Of(profile.Preferences), pace = profile.Pace.ToString().ToLowerInvariant() };
        });

        public Task<object> UpdatePreferencesAsync(
            [Description("0 to 100, how much a low price matters.")] int? budget = null,
            [Description("0 to 100.")] int? culture = null,
            [Description("0 to 100.")] int? food = null,
            [Description("0 to 100.")] int? nature = null,
            [Description("0 to 100.")] int? nightlife = null,
            [Description("0 to 100.")] int? comfort = null,
            [Description("'relaxed', 'balanced' or 'packed'.")] string? pace = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "update_preferences", "Preferences", async () =>
            {
                var profile = await t.Profiles.GetAsync(run.Owner, ct)
                    ?? throw new DomainException("The traveller has no saved profile yet; ask them to open their profile once.");
                var old = profile.Preferences;
                var weights = Weights.Create(
                    budget ?? old.Budget, culture ?? old.Culture, food ?? old.Food, nature ?? old.Nature, nightlife ?? old.Nightlife, comfort ?? old.Comfort);
                var changed = profile with
                {
                    Preferences = weights,
                    Pace = pace is null ? profile.Pace : PaceRules.Parse(pace),
                    UpdatedAt = t.Clock.GetUtcNow(),
                };
                await t.Profiles.SaveAsync(changed, ct);
                run.Step("update_preferences", "Updated your travel preferences", true);
                return new
                {
                    before = new { preferences = Of(old), pace = profile.Pace.ToString().ToLowerInvariant() },
                    after = new { preferences = Of(weights), pace = changed.Pace.ToString().ToLowerInvariant() },
                    note = "Existing plans are not rebuilt; offer rebuild_plan if the traveller wants one to follow.",
                };
            });

        public Task<object> SaveItemAsync(
            [Description("'hotel', 'place' or 'event'.")] string kind,
            [Description("The item's id, from a card of this chat.")] string id,
            CancellationToken ct = default) => Tool.RunAsync(run, "save_item", "Saved items", async () =>
            {
                var savedKind = SavedItem.ParseKind(kind);
                var card = run.FindCard(savedKind.ToString().ToLowerInvariant(), id?.Trim() ?? "")
                    ?? throw new DomainException("Only an item shown as a card in this chat can be saved.");
                var item = await t.Save.HandleAsync(
                    new SaveItemCommand(run.Owner, savedKind.ToString(), card.Id, card.DestinationId ?? run.Job.Trip.DestinationId, card.Name, card.ImageUrl, card.Lat, card.Lng, card.ItemJson),
                    ct);
                run.Step("save_item", $"Saved {item.Name}", true);
                return new { saved = item.Name, kind = savedKind.ToString().ToLowerInvariant() };
            });

        public Task<object> ListSavedAsync(
            [Description("'hotel', 'place' or 'event'; empty for all.")] string? kind = null,
            [Description("City id; empty for every city.")] string? cityId = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "list_saved", "Saved items", async () =>
            {
                var list = await t.ListSaved.HandleAsync(new ListSavedQuery(run.Owner, kind, cityId), ct);
                var cards = t.Cards.Saved([.. list.Items.Take(AssistantTools.MaxItems * 3)]);
                run.Step("list_saved", string.Create(CultureInfo.InvariantCulture, $"Found {list.Items.Count} saved items"), true, list.Items.Count, cards);
                return new { items = list.Items.Take(AssistantTools.MaxItems * 3).Select(i => new { kind = i.Kind.ToString().ToLowerInvariant(), i.Id, i.Name, city = i.DestinationName }).ToList() };
            });

        private static object Of(Weights w) => new { w.Budget, w.Culture, w.Food, w.Nature, w.Nightlife, w.Comfort };
    }
}
