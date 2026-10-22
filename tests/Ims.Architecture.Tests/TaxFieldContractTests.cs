using System.Reflection;
using System.Text.Json.Serialization;
using Ims.Application;

namespace Ims.Architecture.Tests;

/// <summary>
/// The tax-ready fields belong to the accounting module (ADR modules-and-tracking-dimensions 4.3): a response leaves them out while the module is off (a null property is not written),
/// and a request may leave them out (an absent key keeps the stored value). So every contract that carries one says so in its shape, and a new contract that starts carrying one is a deliberate decision, not an accident.
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
        // A response carries a nullable string; a request carries it as Optional<string> (absent / null / value), which is a nullable string on the wire.
        Verify.NoOffenders(Carriers().Where(c => IsRequest(c.Type) ? c.Property.PropertyType != typeof(Optional<string>) : c.Property.PropertyType != typeof(string) || !IsNullable(c.Property))
                .Select(c => $"{c.Type.Name}.{c.Property.Name}"),
            "A tax-ready field of a response is a nullable string, of a request an Optional<string>:");
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
        // The constructor the JSON binder uses has no parameter for the field (a parameter would be required in the OpenAPI document), and the field can be set on its own, so a body without the key is valid.
        var offenders = Carriers().Where(c => IsRequest(c.Type))
            .Where(c => c.Type.GetConstructors().Single(k => k.GetCustomAttribute<JsonConstructorAttribute>() is not null).GetParameters().Any(p => p.Name == c.Property.Name)
                        || c.Property.SetMethod is null)
            .Select(c => $"{c.Type.Name}.{c.Property.Name}");

        Verify.NoOffenders(offenders, "A request field of the accounting module is an init property outside the JSON constructor, so a client that does not know the module can still save:");
    }

    [Fact]
    public void EveryTaxReadyFieldOfARequest_IsLeftOutOfTheJsonWhenAbsent()
    {
        Verify.NoOffenders(Carriers().Where(c => IsRequest(c.Type))
                .Where(c => c.Property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.WhenWritingDefault)
                .Select(c => $"{c.Type.Name}.{c.Property.Name}"),
            "A request property of the accounting module must be [JsonIgnore(Condition = WhenWritingDefault)], so an absent field is not sent as a null (which would clear the stored value):");
    }

    private static bool IsRequest(Type type) => type.Name.EndsWith("Request", StringComparison.Ordinal);

    private static bool IsNullable(PropertyInfo property) =>
        new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;
}
