using Ims.Application;

namespace Ims.Architecture.Tests;

/// <summary>Reflection helpers for the request/handler pairs of Ims.Application.</summary>
public static class RequestTypes
{
    public static IReadOnlyList<Type> Requests { get; } = Layers.TypesOf(Layers.Application)
        .Where(t => t is { IsClass: true, IsAbstract: false } && ImplementsOpenGeneric(t, typeof(IRequest<>)))
        .ToList();

    public static IReadOnlyList<Type> Handlers { get; } = Layers.TypesOf(Layers.Application)
        .Where(t => t is { IsClass: true, IsAbstract: false } && ImplementsOpenGeneric(t, typeof(IRequestHandler<,>)))
        .ToList();

    /// <summary>The request types a handler handles (normally exactly one).</summary>
    public static IEnumerable<Type> HandledBy(Type handler) => handler.GetInterfaces()
        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
        .Select(i => i.GetGenericArguments()[0]);

    public static bool ImplementsOpenGeneric(Type type, Type openGeneric) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric);
}
