namespace Pamplemousse.Api;

/// <summary>
/// Every environment variable the API reads, in one place.
/// Values come from the process environment; at startup <see cref="LoadDotEnv"/> fills in any
/// variable that is not already set from the nearest <c>.env</c> file (see <c>.env.example</c>).
/// Unset or empty variables fall back to the defaults given here.
/// </summary>
public static class Env
{
    // External data providers. Empty means "not configured": the matching section returns a warning instead of data.
    public static string GooglePlacesApiKey => Get("GOOGLE_PLACES_API_KEY", "");
    public static string OpenTripMapApiKey => Get("OPENTRIPMAP_API_KEY", "");
    public static string TicketmasterApiKey => Get("TICKETMASTER_API_KEY", "");

    // Comma-separated browser origins allowed to call the API (e.g. the Firebase Hosting domain).
    public static IReadOnlyList<string> CorsAllowedOrigins =>
        Get("CORS_ALLOWED_ORIGINS", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Returns the environment variable <paramref name="name"/> when it is set and not blank, otherwise <paramref name="defaultValue"/>.</summary>
    public static string Get(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    /// <summary>Loads the first <c>.env</c> found in the current directory or its parents.</summary>
    public static void LoadDotEnv()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (File.Exists(path))
            {
                LoadFile(path);
                return;
            }
        }
    }

    /// <summary>Reads KEY=VALUE lines (blank lines and # comments ignored); never overrides a variable that is already set.</summary>
    public static void LoadFile(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
