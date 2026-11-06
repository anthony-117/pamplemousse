using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Travellers;
using Pamplemousse.Infrastructure.Accounts;

namespace Pamplemousse.Infrastructure.Tests.Accounts;

/// <summary>The paths that need no call to Google; deleting a real account is checked live (quickstart 11).</summary>
public class AccountDeleterTests
{
    private static readonly TravellerId Fares = new("uid-fares");

    [Fact]
    public async Task Without_a_project_deleting_fails_naming_the_variable()
    {
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => new NotConfiguredAccountDeleter().DeleteAsync(Fares, CancellationToken.None));

        Assert.Contains("FIREBASE_PROJECT_ID", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_key_file_is_a_503_not_a_crash()
    {
        using var deleter = new FirebaseAccountDeleter("travelgenie-test", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => deleter.DeleteAsync(Fares, CancellationToken.None));
    }

    [Fact]
    public void Disposing_before_any_use_is_fine()
    {
        var deleter = new FirebaseAccountDeleter("travelgenie-test", "");

        deleter.Dispose();

        Assert.NotNull(deleter);
    }
}
