using Microsoft.Extensions.DependencyInjection;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Api.Tests.System;

/// <summary>
/// The developer's .env may name the real Firebase project with real credentials: every store the API uses must be the
/// in-memory one in tests, or the tests would write to the real database.
/// </summary>
public class TestStoresTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Theory]
    [InlineData(typeof(ITravelProfileStore))]
    [InlineData(typeof(IPlanStore))]
    [InlineData(typeof(ISavedStore))]
    [InlineData(typeof(IChatStore))]
    [InlineData(typeof(IHotelStore))]
    [InlineData(typeof(ICarHistory))]
    public void Every_store_is_in_memory(Type store)
    {
        using var scope = factory.Services.CreateScope();

        var implementation = scope.ServiceProvider.GetRequiredService(store).GetType().Name;

        Assert.StartsWith("InMemory", implementation, StringComparison.Ordinal);
    }
}
