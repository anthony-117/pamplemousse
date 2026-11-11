using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Tests.Saved;

internal sealed class FakeSavedStore : ISavedStore
{
    private readonly List<SavedItem> _items = [];

    public Task<SavedItem?> GetAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct) =>
        Task.FromResult(_items.FirstOrDefault(i => i.Owner == owner && i.Kind == kind && string.Equals(i.Id, id, StringComparison.Ordinal)));

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) => Task.FromResult(_items.Count(i => i.Owner == owner));

    public Task PutAsync(SavedItem item, CancellationToken ct)
    {
        _items.RemoveAll(i => i.Owner == item.Owner && i.Kind == item.Kind && string.Equals(i.Id, item.Id, StringComparison.Ordinal));
        _items.Add(item);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct)
    {
        _items.RemoveAll(i => i.Owner == owner && i.Kind == kind && string.Equals(i.Id, id, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SavedItem>> ListAsync(TravellerId owner, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SavedItem>>([.. _items.Where(i => i.Owner == owner).OrderByDescending(i => i.SavedAt)]);

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct)
    {
        _items.RemoveAll(i => i.Owner == owner);
        return Task.CompletedTask;
    }
}
