using System.Security.Cryptography;
using System.Text;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Infrastructure.Accounts;

/// <summary>
/// Deletes Firebase accounts with the Admin SDK (ADR 0019, research R-4), using the service account Firestore uses. The
/// Firebase app is made on first use, so a bad key file can't stop the API from starting.
/// </summary>
public sealed class FirebaseAccountDeleter(string projectId, string credentialsPath) : IAccountDeleter, IDisposable
{
    private readonly Lazy<FirebaseApp> _app = new(() => FirebaseApp.Create(
        new AppOptions
        {
            ProjectId = projectId,
            Credential = credentialsPath.Length > 0
                ? CredentialFactory.FromFile<ServiceAccountCredential>(credentialsPath).ToGoogleCredential()
                : GoogleCredential.GetApplicationDefault(),
        },
        "pamplemousse-accounts"));

    public async Task DeleteAsync(TravellerId traveller, CancellationToken ct)
    {
        try
        {
            await FirebaseAuth.GetAuth(_app.Value).DeleteUserAsync(traveller.Value, ct);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            // Already gone: a retry after a failure part-way, or deleted in the console.
        }
        catch (Exception ex) when (ex is FirebaseException or HttpRequestException or InvalidOperationException or IOException)
        {
            throw new ServiceUnavailableException("The sign-in service can't be reached.", ex);
        }
    }

    public void Dispose()
    {
        if (_app.IsValueCreated)
        {
            _app.Value.Delete();
        }
    }
}

/// <summary>Used when no Firebase project is set. No request gets this far then (sign-in answers 503), but it fails safe.</summary>
public sealed class NotConfiguredAccountDeleter : IAccountDeleter
{
    public Task DeleteAsync(TravellerId traveller, CancellationToken ct) =>
        throw new ServiceUnavailableException("Sign-in is not configured: set FIREBASE_PROJECT_ID.");
}

/// <summary>
/// Accounts deleted in the last hour, in the search cache (Redis, or memory), so their still-valid tokens are refused (R-5).
/// The key holds a hash of the id, so a Redis listing shows no ids.
/// </summary>
public sealed class DeletedTravellers(ISearchCache cache) : IDeletedTravellers
{
    /// <summary>The longest a Firebase ID token lives.</summary>
    public static readonly TimeSpan Remembered = TimeSpan.FromHours(1);

    public Task AddAsync(TravellerId traveller, CancellationToken ct) => cache.SetAsync(Key(traveller), "1", Remembered, ct);

    public async Task<bool> ContainsAsync(TravellerId traveller, CancellationToken ct) => await cache.GetAsync<string>(Key(traveller), ct) is not null;

    private static string Key(TravellerId traveller) =>
        "deleted-traveller:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(traveller.Value)));
}
