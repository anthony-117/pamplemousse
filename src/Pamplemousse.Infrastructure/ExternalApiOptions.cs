namespace Pamplemousse.Infrastructure;

/// <summary>API keys for paid or registered providers. Filled from Env by the API at startup; empty = not configured.</summary>
public sealed class ExternalApiOptions
{
    public string GooglePlacesApiKey { get; set; } = "";

    public string OpenTripMapApiKey { get; set; } = "";

    public string TicketmasterApiKey { get; set; } = "";
}
