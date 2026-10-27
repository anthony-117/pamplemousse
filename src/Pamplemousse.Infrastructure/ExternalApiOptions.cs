namespace Pamplemousse.Infrastructure;

/// <summary>API keys for registered providers. Filled from Env by the API at startup; empty = not configured.</summary>
public sealed class ExternalApiOptions
{
    public string OpenTripMapApiKey { get; set; } = "";

    public string TicketmasterApiKey { get; set; } = "";
}
