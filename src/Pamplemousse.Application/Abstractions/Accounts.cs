using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Abstractions;

/// <summary>One travel profile per traveller (Firestore <c>travellers/{uid}</c>, spec 009).</summary>
public interface ITravelProfileStore
{
    Task<TravelProfile?> GetAsync(TravellerId traveller, CancellationToken ct);

    Task SaveAsync(TravelProfile profile, CancellationToken ct);

    /// <summary>Deleting a profile that isn't there is not an error.</summary>
    Task DeleteAsync(TravellerId traveller, CancellationToken ct);
}

/// <summary>The traveller's sign-in account (Firebase Authentication).</summary>
public interface IAccountDeleter
{
    /// <summary>Deleting an account that is already gone is not an error.</summary>
    Task DeleteAsync(TravellerId traveller, CancellationToken ct);
}

/// <summary>Accounts deleted in the last hour, whose tokens may still be valid (research R-5).</summary>
public interface IDeletedTravellers
{
    Task AddAsync(TravellerId traveller, CancellationToken ct);

    Task<bool> ContainsAsync(TravellerId traveller, CancellationToken ct);
}

/// <summary>A store or service the request needs can't be reached; the API answers 503 and a retry may work.</summary>
public sealed class ServiceUnavailableException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
