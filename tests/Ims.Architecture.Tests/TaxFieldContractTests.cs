using System.Reflection;
using System.Text.Json.Serialization;

namespace Ims.Architecture.Tests;

/// <summary>
/// The tax-ready fields belong to the accounting module (ADR modules-and-tracking-dimensions 4.3): a response leaves them out while the module is off (a null property is not written),
/// and a request may leave them out. So every contract that carries one says so in its shape, and a new contract that starts carrying one is a deliberate decision, not an accident.
/// </summary>
public class TaxFieldContractTests
{
    private static readonly string[] Names = ["HsCode", "SstCode", "Tin", "SstRegNo"];

    private static readonly string[] Allowed = ["ProductDto", "PartnerDto", "ProductSaveRequest", "PartnerSaveRequest"];

    private static IEnumerable<(Type Type, PropertyInfo Property)> Carriers() =>
        Layers.TypesOf(Layers.Application).Where(t => t.IsPublic)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => Names.Contains(p.Name)).Select(p => (t, p)));

    [Fact]
    public void OnlyTheProductAndPartnerContracts_CarryTaxReadyFields()
    {
        Verify.NoOffenders(Carriers().Where(c => !Allowed.Contains(c.Type.Name)).Select(c => $"{c.Type.Name}.{c.Property.Name}"),
            "A contract that carries a tax-ready field must be added to the allowed list here, with its module gate (and, for a response, WhenWritingNull):");
        Assert.Equal(Allowed.Length * 2, Carriers().Count());
    }

    [Fact]
    public void EveryTaxReadyField_IsOptionalText()
    {
        Verify.NoOffenders(Carriers().Where(c => c.Property.PropertyType != typeof(string) || !IsNullable(c.Property)).Select(c => $"{c.Type.Name}.{c.Property.Name}"),
            "A tax-ready field is a nullable string:");
    }

    [Fact]
    public void EveryTaxReadyFieldOfAResponse_IsLeftOutWhenNull()
    {
        var offenders = Carriers().Where(c => c.Type.Name.EndsWith("Dto", StringComparison.Ordinal))
            .Where(c => c.Property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.WhenWritingNull)
            .Select(c => $"{c.Type.Name}.{c.Property.Name}");

        Verify.NoOffenders(offenders, "A response property of the accounting module must be [JsonIgnore(Condition = WhenWritingNull)], so a switched-off module shows nothing, not even a null:");
    }

    [Fact]
    public void EveryTaxReadyFieldOfARequest_MayBeLeftOut()
    {
        var offenders = Carriers().Where(c => c.Type.Name.EndsWith("Request", StringComparison.Ordinal))
            .Where(c => c.Type.GetConstructors().Single().GetParameters().Single(p => p.Name == c.Property.Name).HasDefaultValue is false)
            .Select(c => $"{c.Type.Name}.{c.Property.Name}");

        Verify.NoOffenders(offenders, "A request field of the accounting module needs a default (null), so a client that does not know the module can still save:");
    }

    private static bool IsNullable(PropertyInfo property) =>
        new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;
}
