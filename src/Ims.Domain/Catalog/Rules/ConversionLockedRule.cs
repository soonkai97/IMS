namespace Ims.Domain;

/// <summary>A unit conversion cannot change or go away once stock movements or purchase orders used it: their quantities were converted with the old factor. The message names the unit, never the documents.</summary>
public sealed class ConversionLockedRule(IEnumerable<int> changedUnitIds, IReadOnlyDictionary<int, string>? unitsInUse) : IBusinessRule
{
    private string? LockedUnit => unitsInUse is null ? null : changedUnitIds.Where(unitsInUse.ContainsKey).Select(id => unitsInUse[id]).FirstOrDefault();

    public string Message => Messages.ConversionLocked(LockedUnit ?? "");
    public bool IsBroken() => LockedUnit is not null;
}
