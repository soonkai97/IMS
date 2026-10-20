using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

/// <remarks>
/// The tax-ready fields are not constructor parameters on the wire: a constructor parameter has a default, and the OpenAPI generator cannot describe the default of a struct. They are set from the body only when
/// the key is there, which is how an absent key stays absent. The longer constructor is for code that builds a request in-process (a value converts to a present <see cref="Optional{T}"/>).
/// </remarks>
[method: JsonConstructor]
public record PartnerSaveRequest(PartnerType Type, string Name, string? Contact, bool IsActive)
{
    public PartnerSaveRequest(PartnerType type, string name, string? contact, bool isActive, Optional<string> tin, Optional<string> sstRegNo = default)
        : this(type, name, contact, isActive)
    {
        Tin = tin;
        SstRegNo = sstRegNo;
    }

    /// <summary>Module accounting. Left out of a save while the module is off (a value is a 400). With the module on: a key that is left out keeps the stored value, an explicit null or blank clears it, a value replaces it. Each of the two fields is decided on its own.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Tin { get; init; }

    /// <summary>Module accounting. Same rule as tin.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SstRegNo { get; init; }
}
