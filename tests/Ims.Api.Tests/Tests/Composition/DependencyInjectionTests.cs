using Ims.Api.Tests.Harness;
using Ims.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Composition;

/// <summary>A mis-wired service must fail at start-up, not on the first request that happens to need it.</summary>
[Collection(ApiCollection.Name)]
public class DependencyInjectionTests(ApiFixture fixture)
{
    private static IEnumerable<(Type Request, Type Result, Type Handler)> HandlerTypes() =>
        typeof(IDispatcher).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => (Request: i.GetGenericArguments()[0], Result: i.GetGenericArguments()[1], Handler: t)));

    [Fact]
    public void Every_request_handler_resolves_from_a_request_scope()
    {
        var handlers = HandlerTypes().ToList();
        Assert.True(handlers.Count > 30, $"Only {handlers.Count} handlers found; the scan is probably wrong.");

        using var scope = fixture.Host.Services.CreateScope();
        foreach (var (request, result, handlerType) in handlers)
        {
            var service = typeof(IRequestHandler<,>).MakeGenericType(request, result);
            var resolved = scope.ServiceProvider.GetService(service);
            Assert.True(resolved is not null, $"{service.Name} for {handlerType.Name} is not registered.");
            Assert.IsType(handlerType, resolved);
        }
    }

    [Fact]
    public void Every_request_type_has_exactly_one_handler()
    {
        var requests = typeof(IDispatcher).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToList();
        var handled = HandlerTypes().GroupBy(h => h.Request).ToDictionary(g => g.Key, g => g.Count());

        Assert.NotEmpty(requests);
        Assert.All(requests, r => Assert.True(handled.GetValueOrDefault(r) == 1, $"{r.Name} has {handled.GetValueOrDefault(r)} handlers."));
    }

    [Fact]
    public void The_dispatcher_and_the_pipeline_resolve()
    {
        using var scope = fixture.Host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDispatcher>());
        Assert.Equal(2, scope.ServiceProvider.GetServices<IPipelineBehavior>().Count()); // logging and validation
    }

    [Fact]
    public void The_container_validates_when_built_and_nothing_scoped_leaks_into_a_singleton()
    {
        var registrations = fixture.Host.Registrations ?? throw new Xunit.Sdk.XunitException("The host factory did not capture the registrations.");

        // The same checks Program turns on with ValidateOnBuild/ValidateScopes, run again on the exact registrations the host used.
        using var provider = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider);
    }

    [Fact]
    public void A_request_scope_cannot_be_resolved_from_the_root_provider()
    {
        // Proof that scope validation is on in the running host: a scoped service from the root provider is refused.
        Assert.Throws<InvalidOperationException>(() => fixture.Host.Services.GetRequiredService<IDispatcher>());
    }
}
