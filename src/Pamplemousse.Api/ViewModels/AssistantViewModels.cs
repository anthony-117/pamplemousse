using Pamplemousse.Application.Assistant;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Api.ViewModels;

/// <summary><see cref="Text"/> is empty when the assistant could not answer; <see cref="Warnings"/> says why.</summary>
public sealed record AssistantReplyViewModel(string Text, IReadOnlyList<AssistantCardViewModel> Cards, IReadOnlyList<string> Warnings);

/// <summary>Opens an app screen with the values of a search the assistant ran.</summary>
public sealed record AssistantCardViewModel(
    CardKind Kind,
    string DestinationId,
    string Title,
    string Subtitle,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? OriginId,
    PlaceCategory? Category);
