namespace Ims.Domain;

/// <summary>The names of the tax-ready fields as clients send and read them (JSON property names), used to name the field in a "not available" answer.</summary>
public static class TaxFieldNames
{
    public const string HsCode = "hsCode";
    public const string SstCode = "sstCode";
    public const string Tin = "tin";
    public const string SstRegNo = "sstRegNo";

    /// <summary>
    /// The code of the module that used to switch the tax-ready fields on. The module is gone from <see cref="ModuleCatalog"/>, and a code that is not listed is never on, so the fields are
    /// refused (a value is a 400) and left out of responses. This constant only keeps those paths compiling until part B of the removal deletes the fields; part B deletes this constant too. It is not a module, never list it.
    /// </summary>
    public const string RemovedModule = "accounting";
}
