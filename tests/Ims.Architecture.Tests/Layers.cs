using System.Reflection;
using Ims.Application;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>The assemblies under test, located through one anchor type each.</summary>
public static class Layers
{
    public static Assembly Domain { get; } = typeof(Entity).Assembly;

    public static Assembly Application { get; } = typeof(IRequest<>).Assembly;

    public static Assembly Infrastructure { get; } = typeof(Ims.Infrastructure.DependencyInjection).Assembly;

    /// <summary>Types that can be loaded; a type that fails to load is reported by its loader exception, never silently skipped.</summary>
    public static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException(
                $"Cannot load types of {assembly.GetName().Name}: {string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message))}", ex);
        }
    }
}
