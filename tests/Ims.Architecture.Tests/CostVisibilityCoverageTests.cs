using System.Reflection;
using System.Text.RegularExpressions;
using Ims.Application;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>
/// The cost rule is written once (<see cref="VisibleWithAttribute"/>, applied by <c>CostVisibilityBehavior</c>), so the only way to leak a cost is a
/// value that looks like cost but is not covered. These tests find such values by name in every response type, sort key, export column, entity and
/// audit payload, so a new DTO, endpoint or export is caught when it is added. A name that is not money goes in <see cref="NotMoney"/>, with a reason.
/// </summary>
public partial class CostVisibilityCoverageTests
{
    [GeneratedRegex("cost|amount|price|worth|valu|threshold", RegexOptions.IgnoreCase)]
    private static partial Regex CostLike();

    /// <summary>"Type.Property" that matches <see cref="CostLike"/> without being money.</summary>
    private static readonly Dictionary<string, string> NotMoney = new()
    {
        ["DocumentDto.AmountIsEstimate"] = "a flag that says the amount is an estimate; it carries no value",
        ["DocumentListItem.AmountIsEstimate"] = "a flag that says the amount is an estimate; it carries no value",
        ["ApprovalRuleDto.QtyThreshold"] = "a quantity",
        ["ApprovalRule.QtyThreshold"] = "a quantity",
        ["SystemSetting.Value"] = "the text of a setting (negative stock on or off, a number of hours), not money",
    };

    // ----- responses -----
    [Fact]
    public void EveryCostLikePropertyOfAResponse_IsTaggedVisibleWith()
    {
        var offenders = ResponseProperties()
            .Where(p => CostLike().IsMatch(p.Name) && p.GetCustomAttribute<VisibleWithAttribute>() is null && !NotMoney.ContainsKey($"{p.DeclaringType!.Name}.{p.Name}"))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        Verify.NoOffenders(offenders, "A response property that looks like cost or value must carry [VisibleWith(Permissions.CostView or ValuationView)] (or be listed in NotMoney with a reason):");
    }

    [Fact]
    public void EveryTaggedResponseProperty_CanBeWithheld_ItIsNullableAndSettable()
    {
        var offenders = ResponseProperties()
            .Where(p => p.GetCustomAttribute<VisibleWithAttribute>() is not null)
            .Where(p => !p.CanWrite || !(Nullable.GetUnderlyingType(p.PropertyType) is not null || !p.PropertyType.IsValueType))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name} ({p.PropertyType.Name}, writable: {p.CanWrite})");

        Verify.NoOffenders(offenders, "A tagged property is set to null for callers without the permission, so it must be nullable and have a setter:");
    }

    [Fact]
    public void TheResponseWalk_FindsTheKnownCostCarriers()
    {
        var tagged = ResponseProperties().Where(p => p.GetCustomAttribute<VisibleWithAttribute>() is not null).Select(p => $"{p.DeclaringType!.Name}.{p.Name}").ToHashSet();

        foreach (var expected in new[]
                 {
                     "DocumentDto.TotalAmount", "DocumentLineDto.UnitCost", "DocumentListItem.TotalAmount", "ApprovalTriggerDto.Threshold", "ApprovalTriggerDto.Actual",
                     "StockRow.AvgCost", "StockRow.Value", "LedgerRow.UnitCost", "StockSummary.TotalValue", "MovementRow.UnitCost", "MovementRow.Amount",
                     "ApprovalRuleDto.AmountThreshold"
                 })
            Assert.Contains(expected, tagged);
    }

    [Fact]
    public void TheUnitPrices_AreTheDerivedCostPerBaseUnit_AndEverythingElseTaggedIsMoney()
    {
        var unitPrices = ResponseProperties()
            .Where(p => p.GetCustomAttribute<VisibleWithAttribute>() is { UnitPrice: true })
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}").Order().ToList();

        // Not the entered cost (DocumentLineDto.UnitCost is money, per entered unit), not an amount, a value or a threshold.
        Assert.Equal(["DocumentLineDto.BaseUnitCost", "LedgerRow.UnitCost", "MovementRow.UnitCost", "StockRow.AvgCost"], unitPrices);
    }

    // ----- audit log -----
    [Fact]
    public void TheAuditLogPayloads_AreScrubbed()
    {
        var audit = ResponseTypes().Single(t => t.Name == "AuditLogDto");

        foreach (var name in new[] { "Before", "After" })
            Assert.NotNull(audit.GetProperty(name)!.GetCustomAttribute<ScrubsRestrictedKeysAttribute>());
    }

    [Fact]
    public void EveryEntityPropertyThatLooksLikeCost_IsTaggedSoTheAuditLogCanWithholdIt()
    {
        var offenders = Layers.TypesOf(Layers.Domain)
            .Where(t => t is { IsClass: true, IsEnum: false } && typeof(AuditableEntity).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => CostLike().IsMatch(p.Name) && p.GetCustomAttribute<VisibleWithAttribute>() is null && !NotMoney.ContainsKey($"{p.DeclaringType!.Name}.{p.Name}"))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        Verify.NoOffenders(offenders, "An entity property that looks like cost or value must carry [VisibleWith] (the audit log reads it) or be listed in NotMoney:");
    }

    [Fact]
    public void AnAuditPayloadThatNamesMoney_UsesAuditFields()
    {
        var offenders = SourceFiles.In("Ims.Application")
            .SelectMany(f => AuditRecordCalls().Matches(File.ReadAllText(f.FullPath)).Select(m => (file: f, call: m.Value)))
            .Where(x => CostLike().IsMatch(x.call) && !x.call.Contains(nameof(AuditFields) + "."))
            .Select(x => $"{x.file.RelativePath}: {x.call.ReplaceLineEndings(" ")[..Math.Min(120, x.call.Length)]}");

        Verify.NoOffenders(offenders, "An audit payload with a cost or value must name its key with a constant of AuditFields (tagged [VisibleWith]):");
    }

    [GeneratedRegex(@"audit\.Record\([^;]*;")]
    private static partial Regex AuditRecordCalls();

    // ----- requests -----
    [Fact]
    public void EverySortKeyThatLooksLikeCost_NeedsThePermission()
    {
        var offenders = Layers.TypesOf(Layers.Application)
            .Where(t => t.IsEnum && t.Name.EndsWith("Sort", StringComparison.Ordinal))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => CostLike().IsMatch(f.Name) && f.GetCustomAttribute<VisibleWithAttribute>() is null)
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name}");

        Verify.NoOffenders(offenders, "Ordering by cost or value reveals it: a sort key that looks like cost must carry [VisibleWith]:");
    }

    // ----- Excel -----
    [Fact]
    public void EveryExcelColumnThatLooksLikeCost_NeedsThePermission()
    {
        var offenders = ColumnSets()
            .SelectMany(set => set.Columns.Select(c => (set.Name, Column: c)))
            .Where(x => CostLike().IsMatch(x.Column.Header) && x.Column.VisibleWith is null)
            .Select(x => $"{x.Name}: \"{x.Column.Header}\"");

        Verify.NoOffenders(offenders, "An export or template column that looks like cost or value must name a permission (ReportColumn.VisibleWith):");
    }

    [Fact]
    public void AMoneyColumn_IsTheSameSetOfColumnsAsTheOnesGatedByAPermission()
    {
        // The writer rounds and formats the money columns; the cost rule hides the gated ones. They must be the same columns, or a cost would be formatted as a
        // plain number (or a quantity as money).
        var offenders = ColumnSets()
            .SelectMany(set => set.Columns.Select(c => (set.Name, Column: c)))
            .Where(x => x.Column.IsMoney != (x.Column.VisibleWith is not null))
            .Select(x => $"{x.Name}: \"{x.Column.Header}\" (money: {x.Column.IsMoney}, gated: {x.Column.VisibleWith is not null})");

        Verify.NoOffenders(offenders, "A column that holds money must be gated by cost.view or valuation.view, and every gated column holds money (ReportColumn.MoneyColumn):");
    }

    [Fact]
    public void TheUnitPriceColumns_AreTheDerivedCostPerBaseUnit_AndEveryOtherMoneyColumnHasTwoDecimals()
    {
        var columns = ColumnSets().SelectMany(set => set.Columns).ToList();

        // Only gated columns can be unit prices, and they are exactly the derived cost per base unit: the average and the unit cost of a ledger or movement row.
        // The template's "UnitCost" is what a person types per entered unit: money.
        Assert.All(columns.Where(c => c.UnitPrice), c => { Assert.NotNull(c.VisibleWith); Assert.Equal(4, c.DecimalPlaces); });
        Assert.Equal(["Avg cost", "Unit cost", "Unit cost"], columns.Where(c => c.UnitPrice).Select(c => c.Header).Order());
        Assert.All(columns.Where(c => c.IsMoney && !c.UnitPrice), c => Assert.Equal(2, c.DecimalPlaces));
        Assert.All(columns.Where(c => !c.IsMoney), c => Assert.Null(c.DecimalPlaces));
    }

    [Fact]
    public void TheColumnSets_AreFound()
    {
        var names = ColumnSets().Select(s => s.Name).ToList();

        foreach (var expected in new[] { "StockSummaryColumns", "MovementColumns", "LedgerColumns", "LowStockColumns", "ProductColumns", "OpeningStockColumns" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void OnlyTheReportWriter_UsesTheExcelWriter()
    {
        var offenders = Layers.TypesOf(Layers.Application)
            .Where(t => t.IsClass && t != typeof(ReportWriter))
            .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == typeof(IExcelWriter)))
            .Select(t => t.Name);

        Verify.NoOffenders(offenders, "Spreadsheets are written through IReportWriter, which leaves out the columns the caller may not see:");
    }

    // ----- helpers -----
    private static IEnumerable<(string Name, IReadOnlyList<ReportColumn> Columns)> ColumnSets() =>
        new[] { typeof(ReportHeaders), typeof(ImportTemplates) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(ReportColumn[])))
            .Select(f => (f.Name, (IReadOnlyList<ReportColumn>)(ReportColumn[])f.GetValue(null)!));

    /// <summary>The types a request can answer with, and every type reachable from them through properties, lists and pages.</summary>
    private static IReadOnlyList<Type> ResponseTypes()
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(RequestTypes.Requests.SelectMany(r => r.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)).Select(i => i.GetGenericArguments()[0]));
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) queue.Enqueue(argument);
            if (type.IsArray) queue.Enqueue(type.GetElementType()!);
            if (!IsOurs(type) || !seen.Add(type)) continue;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) queue.Enqueue(property.PropertyType);
        }
        return seen.ToList();
    }

    private static IEnumerable<PropertyInfo> ResponseProperties() => ResponseTypes()
        .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(p => p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is null); // never on the wire

    private static bool IsOurs(Type type) => type is { IsClass: true, IsGenericTypeDefinition: false } && type.Assembly.GetName().Name!.StartsWith("Ims.", StringComparison.Ordinal)
        && !type.IsEnum && type.Namespace?.StartsWith("Ims.", StringComparison.Ordinal) == true;
}
