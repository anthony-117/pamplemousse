using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Assistant;
using Pamplemousse.Application.Assistant.Queries.AskAssistant;

namespace Pamplemousse.Api.Mappings;

internal static class AssistantMappings
{
    public static AskAssistantQuery ToQuery(this AskAssistantRequest r) => new(
        r.Messages?.Select(m => new AssistantMessage(m.Role, m.Text)).ToList(),
        r.Context is null
            ? null
            : new AssistantTripInput(r.Context.DestinationId, r.Context.From, r.Context.To, r.Context.Adults, r.Context.Passport, r.Context.Currency, r.Context.OriginId));

    public static AssistantReplyViewModel ToViewModel(this AskAssistantResponse r) => new(r.Text, [.. r.Cards.Select(ToViewModel)], r.Warnings);

    private static AssistantCardViewModel ToViewModel(AssistantCard c) =>
        new(c.Kind, c.DestinationId, c.Title, c.Subtitle, c.From, c.To, c.Adults, c.OriginId, c.Category);
}
