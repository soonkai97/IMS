namespace Ims.Domain;

/// <summary>The names of the tax-ready fields as clients send and read them (JSON property names), used to name the field in a "not available" answer.</summary>
public static class TaxFieldNames
{
    public const string HsCode = "hsCode";
    public const string SstCode = "sstCode";
    public const string Tin = "tin";
    public const string SstRegNo = "sstRegNo";
}
