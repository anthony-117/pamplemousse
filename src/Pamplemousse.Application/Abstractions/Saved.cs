using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Abstractions;

/// <summary>
/// A traveller's saved hotels, places and events (spec 010 FR-020, Firestore <c>travellers/{uid}/saved</c>). Every call
/// names the owner. The store is unreachable → <see cref="ServiceUnavailableException"/>.
/// </summary>
public interface ISavedStore
{
    Task<SavedItem?> GetAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct);

    Task<int> CountAsync(TravellerId owner, CancellationToken ct);

    Task PutAsync(SavedItem item, CancellationToken ct);

    Task DeleteAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<SavedItem>> ListAsync(TravellerId owner, CancellationToken ct);

    Task DeleteAllAsync(TravellerId owner, CancellationToken ct);
}
