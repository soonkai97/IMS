using System.Reflection;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// A copy of the objects a step changes, so that a step which throws leaves the book as the database would after a rolled-back transaction (the handlers load the document
/// and the balances afresh, and a failed run never saves). Reflection is used because the entities have no copy method and private state.
/// </summary>
internal static class OracleClone
{
    private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo LinesField = typeof(StockDocument).GetField("_lines", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static StockBalance Copy(StockBalance balance) => Shallow(balance);

    public static StockDocument Copy(StockDocument document)
    {
        var copy = Shallow(document);
        LinesField.SetValue(copy, document.Lines.Select(Shallow).ToList());
        return copy;
    }

    private static T Shallow<T>(T value) where T : notnull => (T)CloneMethod.Invoke(value, null)!;
}
