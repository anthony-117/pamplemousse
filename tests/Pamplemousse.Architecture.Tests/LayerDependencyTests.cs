using System.Reflection;
using Pamplemousse.Api.Controllers;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Infrastructure;

namespace Pamplemousse.Architecture.Tests;

/// <summary>Keeps the layers honest (ADR 0012): Api → Application → Domain, and Infrastructure implements Application's interfaces.</summary>
public class LayerDependencyTests
{
    private static readonly Assembly DomainLayer = typeof(DateRange).Assembly;
    private static readonly Assembly ApplicationLayer = typeof(IQueryHandler<,>).Assembly;
    private static readonly Assembly InfrastructureLayer = typeof(ExternalApiOptions).Assembly;
    private static readonly Assembly ApiLayer = typeof(SystemController).Assembly;

    private static string[] References(Assembly assembly) => [.. assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_library()
    {
        Assert.All(References(DomainLayer), name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || string.Equals(name, "netstandard", StringComparison.Ordinal),
            $"Domain references {name}"));
    }

    [Fact]
    public void Application_depends_only_on_domain_and_abstractions()
    {
        // Constitution I: Domain plus Microsoft.Extensions.*.Abstractions packages (DI, logging, AI).
        Assert.All(References(ApplicationLayer), name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal)
                || string.Equals(name, "Pamplemousse.Domain", StringComparison.Ordinal)
                || (name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) && name.EndsWith(".Abstractions", StringComparison.Ordinal)),
            $"Application references {name}"));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        Assert.DoesNotContain("Pamplemousse.Api", References(InfrastructureLayer), StringComparer.Ordinal);
    }

    [Fact]
    public void Controllers_only_talk_to_handlers()
    {
        // The Api references Infrastructure only to register it in Program.cs.
        var controllers = ApiLayer.GetTypes().Where(t => string.Equals(t.Namespace, "Pamplemousse.Api.Controllers", StringComparison.Ordinal));
        var dependencies = controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType.Assembly);

        Assert.NotEmpty(dependencies);
        Assert.DoesNotContain(InfrastructureLayer, dependencies);
    }

    [Fact]
    public void Handlers_are_named_after_their_use_case()
    {
        var handlers = ApplicationLayer.GetTypes().Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.Name.StartsWith("IQueryHandler", StringComparison.Ordinal)));

        Assert.NotEmpty(handlers);
        Assert.All(handlers, h => Assert.EndsWith("Handler", h.Name, StringComparison.Ordinal));
    }
}
