namespace Ims.Domain;

/// <summary>
/// The codes of the modules (ADR modules-and-tracking-dimensions 4.1); <see cref="ModuleCatalog"/> lists them. A code is stable once released, because it is part of a
/// setting key (<c>Module.&lt;code&gt;</c>) and of the API. The core of the system has no code and is always on.
/// </summary>
public static class ModuleCodes
{
    /// <summary>Tax-ready fields on products and partners and the accounting export file.</summary>
    public const string Accounting = "accounting";

    /// <summary>Purchase orders, receiving against them, and a reorder list (pilot slice 2).</summary>
    public const string Purchasing = "purchasing";
}
