namespace Ims.Domain.Tests.Common;

public class MessagesTests
{
    [Fact]
    public void InsufficientStock_NamesProductWarehouseAndQuantities()
    {
        var message = Messages.InsufficientStock(7, 2, 3m, 5m);

        Assert.Contains("product 7", message);
        Assert.Contains("warehouse 2", message);
        Assert.Contains("available 3", message);
        Assert.Contains("requested 5", message);
    }

    [Fact]
    public void EveryConstantMessage_IsNonBlankAndEndsLikeASentenceOrTitle()
    {
        var constants = typeof(Messages).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Value: (string)f.GetRawConstantValue()!)).ToList();

        Assert.NotEmpty(constants);
        Assert.All(constants, c => Assert.False(string.IsNullOrWhiteSpace(c.Value), c.Name));
    }

    [Fact]
    public void UserFacingMessages_DoNotLeakInternals()
    {
        var constants = typeof(Messages).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Value: (string)f.GetRawConstantValue()!));

        Assert.All(constants, c =>
        {
            Assert.DoesNotContain("Exception", c.Value);
            Assert.DoesNotContain("SQL", c.Value);
            Assert.DoesNotContain("stack", c.Value, StringComparison.OrdinalIgnoreCase);
        });
    }
}
