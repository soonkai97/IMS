namespace Ims.Application;

/// <summary>
/// Query tags (<c>TagWith</c>) that ask the database for a query hint. The infrastructure turns each tag into its hint (<c>QueryHintInterceptor</c>); an untagged
/// query is never touched. Use one only where a plan was measured to be wrong for some values (docs/dba/README.md 4b and 4d), never as a blanket.
/// </summary>
public static class QueryHint
{
    /// <summary>A plan of its own for every call: the best plan depends on how many rows the value matches (movements of one product).</summary>
    public const string Recompile = "ims:recompile";

    /// <summary>
    /// One plan for every value, compiled from the average statistics instead of the first value that arrives: a list page whose plan flips with the size of the
    /// page (<c>TOP(@p)</c>) must not keep the plan of the one large page that happened to come first.
    /// </summary>
    public const string NoSniffing = "ims:no-sniffing";
}
