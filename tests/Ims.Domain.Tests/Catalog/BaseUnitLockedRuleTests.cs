namespace Ims.Domain.Tests.Catalog;

public class BaseUnitLockedRuleTests
{
    [Theory]
    [InlineData(1, 2, true, true)]    // different unit, history exists -> locked
    [InlineData(1, 2, false, false)]  // different unit, no history
    [InlineData(1, 1, true, false)]   // same unit
    [InlineData(1, 1, false, false)]
    public void IsBroken_OnlyWhenTheUnitChangesAfterMovements(int current, int proposed, bool hasMovements, bool broken)
    {
        var rule = new BaseUnitLockedRule(current, proposed, hasMovements);

        Assert.Equal(broken, rule.IsBroken());
        Assert.Equal(Messages.BaseUomLocked, rule.Message);
    }
}
