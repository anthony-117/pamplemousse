namespace Pamplemousse.Api.ViewModels;

public sealed record PingViewModel(string Name, string Version, string Environment, DateTimeOffset UtcNow);
