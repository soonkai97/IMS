using System.Reflection;
using FluentValidation;
using Ims.Application.Features.Audit;
using Ims.Application.Features.Settings;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ims.Application;

/// <summary>
/// Composition of the Application layer. Everything is registered by convention so adding a feature means adding classes,
/// not editing this file:
///   command/query handlers  -> registered against IRequestHandler&lt;TRequest, TResult&gt;
///   helpers (IAppService)   -> registered against the interfaces they implement
///   validators              -> FluentValidation assembly scan
///   posting strategies      -> IPostingStrategy, resolved as a set by the posting engine
/// All are scoped: they share the request's unit of work.
/// </summary>
public static class DependencyInjection
{
    private static readonly Assembly ApplicationAssembly = typeof(DependencyInjection).Assembly;

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddRequestHandlers()
                .AddAppServices()
                .AddValidatorsFromAssembly(ApplicationAssembly, ServiceLifetime.Scoped, IsRequestValidator, includeInternalTypes: true);

        services.AddMessaging();

        // Ports implemented inside Application itself.
        services.TryAddScoped<IAuditTrail, AuditTrail>();
        services.TryAddScoped<ISystemSettings, DbSystemSettings>();

        // Domain business services: stateless and pure, so singletons. Open/closed: a new document type adds one strategy here
        // and touches no existing posting code.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostingStrategy, ReceiptPostingStrategy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostingStrategy, IssuePostingStrategy>());
        services.TryAddSingleton<IPostingEngine, PostingEngine>();
        return services;
    }

    /// <summary>Only validators of commands/queries are resolved by the pipeline; DTO-level validators are composed inside them with SetValidator.</summary>
    private static bool IsRequestValidator(AssemblyScanner.AssemblyScanResult r) =>
        r.InterfaceType.GetGenericArguments()[0].GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

    private static IServiceCollection AddRequestHandlers(this IServiceCollection services)
    {
        foreach (var type in ConcreteTypes())
            foreach (var i in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                services.AddScoped(i, type);
        return services;
    }

    /// <summary>Registers each <see cref="IAppService"/> under the interfaces it declares (falling back to itself).</summary>
    private static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        foreach (var type in ConcreteTypes().Where(t => typeof(IAppService).IsAssignableFrom(t)))
        {
            var contracts = type.GetInterfaces().Where(i => i != typeof(IAppService)).ToList();
            if (contracts.Count == 0) services.AddScoped(type);
            foreach (var contract in contracts) services.AddScoped(contract, type);
        }
        return services;
    }

    /// <summary>The dispatcher and its pipeline. Order matters: logging wraps validation wraps the handler.</summary>
    private static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IPipelineBehavior, LoggingBehavior>();
        services.AddScoped<IPipelineBehavior, ValidationBehavior>();
        return services;
    }

    private static IEnumerable<Type> ConcreteTypes() =>
        ApplicationAssembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });
}
