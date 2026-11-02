namespace Pamplemousse.Application.Abstractions;

/// <summary>Thrown when a provider needs an API key that is not set.</summary>
public sealed class ProviderNotConfiguredException(string provider, string environmentVariable)
    : InvalidOperationException($"{provider} is not configured. Set {environmentVariable} in the environment or .env file.")
{
    public string Provider { get; } = provider;

    public string EnvironmentVariable { get; } = environmentVariable;
}
