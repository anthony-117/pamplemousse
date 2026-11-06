using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Application.Travellers;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Tests.Travellers;

public class ProfileHandlerTests
{
    private static readonly SignedInTraveller Fares = new(new TravellerId("uid-fares"), "fares@example.com", "Fares Haddad");

    private readonly ProfileStore _store = new();
    private readonly Fakes _data = new();
    private readonly FixedClock _clock = new();

    private Profiles Profiles => new(_store, _data, _clock);

    private static ProfileChanges Changes(string passport = "IT", string currency = "eur", string? city = "3169070") =>
        new("Fares", passport, currency, city, 90, 90, 50, 50, 20, 50);

    [Fact]
    public async Task The_first_read_makes_and_saves_the_profile()
    {
        var response = await new GetProfileHandler(Profiles).HandleAsync(new GetProfileQuery(Fares), CancellationToken.None);

        Assert.Equal(("Fares Haddad", "LB", "USD"), (response.Profile.DisplayName, response.Profile.Passport, response.Profile.HomeCurrency));
        Assert.Null(response.HomeCity);
        Assert.Equal(1, _store.Saves);
        Assert.Equal(_clock.GetUtcNow(), response.Profile.CreatedAt);
    }

    [Fact]
    public async Task A_later_read_returns_the_stored_profile_without_saving()
    {
        var handler = new GetProfileHandler(Profiles);
        await handler.HandleAsync(new GetProfileQuery(Fares), CancellationToken.None);

        await handler.HandleAsync(new GetProfileQuery(Fares with { Name = "Someone else" }), CancellationToken.None);

        Assert.Equal(1, _store.Saves);
        Assert.Equal("Fares Haddad", (await _store.GetAsync(Fares.Id, CancellationToken.None))?.DisplayName);
    }

    [Fact]
    public async Task An_update_checks_the_reference_data_and_shows_the_home_city()
    {
        var response = await new UpdateProfileHandler(Profiles, _store, _data, _clock)
            .HandleAsync(new UpdateProfileCommand(Fares, Changes()), CancellationToken.None);

        Assert.Equal(("IT", "EUR", "Rome"), (response.Profile.Passport, response.Profile.HomeCurrency, response.HomeCity?.Name));
        Assert.Equal(new Weights(90, 90, 50, 50, 20, 50), (await _store.GetAsync(Fares.Id, CancellationToken.None))?.Preferences);
    }

    [Theory]
    [InlineData("LB", "EUR", "3169070", "passport")]
    [InlineData("IT", "GBP", "3169070", "homeCurrency")]
    [InlineData("IT", "EUR", "1", "homeCityId")]
    public async Task An_unknown_code_is_refused_and_nothing_changes(string passport, string currency, string city, string field)
    {
        var handler = new UpdateProfileHandler(Profiles, _store, _data, _clock);

        var error = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(new UpdateProfileCommand(Fares, Changes(passport, currency, city)), CancellationToken.None));

        Assert.Equal(field, error.Field);
        Assert.Equal("USD", (await _store.GetAsync(Fares.Id, CancellationToken.None))?.HomeCurrency);
    }

    [Fact]
    public async Task Deleting_removes_the_profile_then_the_account_then_remembers_it()
    {
        var accounts = new Accounts(_store);
        var deleted = new Deleted(accounts);
        await Profiles.GetOrCreateAsync(Fares, CancellationToken.None);

        await new DeleteAccountHandler(_store, accounts, deleted).HandleAsync(new DeleteAccountCommand(Fares), CancellationToken.None);

        Assert.Null(await _store.GetAsync(Fares.Id, CancellationToken.None));
        Assert.Equal(["profile gone, account deleted", "remembered"], deleted.Log);
    }

    [Fact]
    public async Task A_failed_account_delete_is_not_remembered_and_a_retry_finishes()
    {
        var accounts = new Accounts(_store) { Fail = true };
        var deleted = new Deleted(accounts);
        var handler = new DeleteAccountHandler(_store, accounts, deleted);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => handler.HandleAsync(new DeleteAccountCommand(Fares), CancellationToken.None));
        Assert.Empty(deleted.Log);

        accounts.Fail = false;
        await handler.HandleAsync(new DeleteAccountCommand(Fares), CancellationToken.None);
        Assert.True(await deleted.ContainsAsync(Fares.Id, CancellationToken.None));
    }

    private sealed class ProfileStore : ITravelProfileStore
    {
        private readonly Dictionary<string, TravelProfile> _profiles = new(StringComparer.Ordinal);

        public int Saves { get; private set; }

        public Task<TravelProfile?> GetAsync(TravellerId traveller, CancellationToken ct) => Task.FromResult(_profiles.GetValueOrDefault(traveller.Value));

        public Task SaveAsync(TravelProfile profile, CancellationToken ct)
        {
            Saves++;
            _profiles[profile.Traveller.Value] = profile;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(TravellerId traveller, CancellationToken ct)
        {
            _profiles.Remove(traveller.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class Accounts(ProfileStore store) : IAccountDeleter
    {
        public bool Fail { get; set; }

        public string? Last { get; private set; }

        public async Task DeleteAsync(TravellerId traveller, CancellationToken ct)
        {
            if (Fail)
            {
                throw new ServiceUnavailableException("down");
            }

            Last = await store.GetAsync(traveller, ct) is null ? "profile gone, account deleted" : "profile still there";
        }
    }

    private sealed class Deleted(Accounts accounts) : IDeletedTravellers
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        public List<string> Log { get; } = [];

        public Task AddAsync(TravellerId traveller, CancellationToken ct)
        {
            Log.Add(accounts.Last ?? "account not deleted");
            Log.Add("remembered");
            _ids.Add(traveller.Value);
            return Task.CompletedTask;
        }

        public Task<bool> ContainsAsync(TravellerId traveller, CancellationToken ct) => Task.FromResult(_ids.Contains(traveller.Value));
    }
}
