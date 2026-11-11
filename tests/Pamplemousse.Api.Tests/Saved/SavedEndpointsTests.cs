using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pamplemousse.Api.Tests.Saved;

/// <summary>The saved routes of spec 010 (contracts/saved-api.md), with the body the app sends.</summary>
public class SavedEndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private const string Rome = "3169070";
    private readonly string _uid = "uid-" + Guid.NewGuid().ToString("N")[..10];

    private HttpClient As(string? uid = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For(uid ?? _uid));
        return client;
    }

    private static Uri At(string path) => new(path, UriKind.Relative);

    private static Uri Item(string kind, string id) => At($"/api/v1/saved/{kind}/{Uri.EscapeDataString(id)}");

    private static object Body(string name = "Colosseum", object? item = null) => new
    {
        destinationId = Rome,
        name,
        imageUrl = "https://upload.wikimedia.org/colosseum.jpg",
        lat = 41.89,
        lng = 12.49,
        item = item ?? new { id = "wd:Q10285", name, category = "attraction", location = new { lat = 41.89, lng = 12.49 } },
    };

    [Fact]
    public async Task Saved_items_come_back_newest_first_with_their_city_and_item()
    {
        using var client = As();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(Item("hotel", "bk:1"), Body("Hotel Artemide"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(Item("place", "wd:Q10285"), Body())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(Item("place", "wd:Q10285"), Body())).StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/saved");
        var items = list.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal((2, 200), (list.GetProperty("count").GetInt32(), list.GetProperty("max").GetInt32()));
        var place = items[0];
        Assert.Equal(("place", "wd:Q10285", "Rome"), (place.GetProperty("kind").GetString(), place.GetProperty("id").GetString(), place.GetProperty("destinationName").GetString()));
        Assert.Equal("attraction", place.GetProperty("item").GetProperty("category").GetString());

        var ids = await client.GetFromJsonAsync<JsonElement>("/api/v1/saved/ids");
        Assert.Equal("bk:1", ids.GetProperty("hotel")[0].GetString());
        Assert.Equal("wd:Q10285", ids.GetProperty("place")[0].GetString());
        Assert.Equal(0, ids.GetProperty("event").GetArrayLength());

        var hotels = await client.GetFromJsonAsync<JsonElement>("/api/v1/saved?kind=hotel");
        Assert.Equal(1, hotels.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Removing_takes_it_out_and_is_fine_twice()
    {
        using var client = As();
        await client.PutAsJsonAsync(Item("event", "tm:9"), Body("Jazz"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Item("event", "tm:9"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Item("event", "tm:9"))).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/v1/saved")).GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("flight", "kind")]
    [InlineData("car", "kind")]
    public async Task Flights_and_cars_are_a_400_naming_kind(string kind, string field)
    {
        using var client = As();
        var response = await client.PutAsJsonAsync(Item(kind, "x"), Body());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Another_travellers_items_are_never_seen()
    {
        using var fares = As();
        using var maya = As("uid-maya-" + Guid.NewGuid().ToString("N")[..6]);
        await fares.PutAsJsonAsync(Item("hotel", "bk:1"), Body("Hotel Artemide"));

        Assert.Equal(0, (await maya.GetFromJsonAsync<JsonElement>("/api/v1/saved")).GetProperty("count").GetInt32());
        await maya.DeleteAsync(Item("hotel", "bk:1"));
        Assert.Equal(1, (await fares.GetFromJsonAsync<JsonElement>("/api/v1/saved")).GetProperty("count").GetInt32());
    }
}
