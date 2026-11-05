namespace Pamplemousse.Application.Assistant;

/// <summary>What the tools found while answering one message: a card per distinct search and the sources' warnings.</summary>
internal sealed class AssistantSession
{
    private readonly List<AssistantCard> _cards = [];
    private readonly List<string> _warnings = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<AssistantCard> Cards
    {
        get
        {
            lock (_lock)
            {
                return [.. _cards];
            }
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_lock)
            {
                return [.. _warnings];
            }
        }
    }

    /// <summary>The model may run the same search twice; the traveller sees one card for it.</summary>
    public void AddCard(AssistantCard card)
    {
        lock (_lock)
        {
            if (!_cards.Contains(card))
            {
                _cards.Add(card);
            }
        }
    }

    public void AddWarnings(IEnumerable<string> warnings)
    {
        lock (_lock)
        {
            foreach (var warning in warnings)
            {
                if (!_warnings.Contains(warning, StringComparer.Ordinal))
                {
                    _warnings.Add(warning);
                }
            }
        }
    }
}
