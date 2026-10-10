using System.Reflection;
using System.Runtime.CompilerServices;
using Ims.Application;

namespace Ims.Architecture.Tests;

/// <summary>Handlers depend on abstractions (DIP) and every request has exactly one handler.</summary>
public class HandlerDesignTests
{
    private static readonly string[] FrameworkPrefixes = ["System", "Microsoft", "FluentValidation", "Ardalis", "mscorlib", "netstandard"];

    [Fact]
    public void Handlers_AreDiscovered()
    {
        Assert.NotEmpty(RequestTypes.Handlers);
        Assert.NotEmpty(RequestTypes.Requests);
    }

    [Fact]
    public void HandlerConstructorParameters_AreAbstractionsOrValuesOrFrameworkTypes()
    {
        var offenders = RequestTypes.Handlers
            .SelectMany(handler => handler.GetConstructors().SelectMany(c => c.GetParameters().Select(p => (handler, p))))
            .Where(x => !IsAcceptableDependency(x.p.ParameterType))
            .Select(x => $"{x.handler.Name}({x.p.ParameterType.Name} {x.p.Name})");

        Verify.NoOffenders(offenders,
            "Handlers must take interfaces, records/specifications/value types or framework types, never concrete Application or Domain classes:");
    }

    [Fact]
    public void EveryRequest_HasExactlyOneHandler()
    {
        var offenders = RequestTypes.Requests
            .Select(request => (request, count: RequestTypes.Handlers.Count(h => RequestTypes.HandledBy(h).Contains(request))))
            .Where(x => x.count != 1)
            .Select(x => $"{x.request.Name} has {x.count} handlers");

        Verify.NoOffenders(offenders, "Every command/query needs exactly one handler:");
    }

    [Fact]
    public void EveryHandler_HandlesARequestOfTheApplication()
    {
        var offenders = RequestTypes.Handlers
            .Where(h => !RequestTypes.HandledBy(h).Any(r => RequestTypes.Requests.Contains(r)))
            .Select(h => h.Name);

        Verify.NoOffenders(offenders, "Handlers without a matching IRequest implementation:");
    }

    [Fact]
    public void EveryHandler_HandlesExactlyOneRequestType()
    {
        var offenders = RequestTypes.Handlers
            .Where(h => RequestTypes.HandledBy(h).Count() != 1)
            .Select(h => $"{h.Name} handles {RequestTypes.HandledBy(h).Count()} request types");

        Verify.NoOffenders(offenders, "A handler is one use case; it must handle exactly one request type:");
    }

    [Fact]
    public void Commands_AndQueries_AreHandledByTheirMatchingHandlerInterface()
    {
        var offenders = RequestTypes.Handlers.SelectMany(handler => RequestTypes.HandledBy(handler).Select(request => (handler, request)))
            .Where(x => RequestTypes.ImplementsOpenGeneric(x.request, typeof(ICommand<>)) && RequestTypes.ImplementsOpenGeneric(x.handler, typeof(IQueryHandler<,>))
                     || RequestTypes.ImplementsOpenGeneric(x.request, typeof(IQuery<>)) && RequestTypes.ImplementsOpenGeneric(x.handler, typeof(ICommandHandler<,>)))
            .Select(x => $"{x.handler.Name} <- {x.request.Name}");

        Verify.NoOffenders(offenders, "Commands must use ICommandHandler and queries IQueryHandler:");
    }

    private static bool IsAcceptableDependency(Type type)
    {
        if (type.IsInterface || type.IsValueType || type.IsEnum || type.IsArray || typeof(Delegate).IsAssignableFrom(type))
        {
            return true;
        }

        if (IsRecord(type) || IsSpecification(type))
        {
            return true;
        }

        var assemblyName = type.Assembly.GetName().Name ?? "";
        return FrameworkPrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.Ordinal));
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

    private static bool IsSpecification(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition().FullName?.StartsWith("Ardalis.Specification.Specification`", StringComparison.Ordinal) == true)
            {
                return true;
            }
        }

        return false;
    }
}
