using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Sales;
using static Ims.Domain.Tests.Common.OwnershipTable;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// DDD-09 part B, characterization: the "mine" rule of the three access services as it is on <c>main</c>, where each one reads the persistence column <c>RecRevisor</c> itself. For every status, every
/// combination of submitter, creator and last editor, and every role: a recall is allowed or refused with the sentence of its workflow, exactly as <see cref="OwnershipTable"/> writes it.
/// Green before and after <c>DocumentOwnership</c> takes the rule over: a refusal is a 403 for every role and every owner, so authorization must not move by a hair.
/// </summary>
public class OwnershipAccessTests
{
    public static TheoryData<int> DocumentCases() => new(Enumerable.Range(0, All(StatusesOfDocument).Count()));
    public static TheoryData<int> PurchaseCases() => new(Enumerable.Range(0, All(StatusesOfPurchaseOrder).Count()));
    public static TheoryData<int> SalesCases() => new(Enumerable.Range(0, All(StatusesOfSalesOrder).Count()));

    [Theory]
    [MemberData(nameof(DocumentCases))]
    public void ADocumentIsRecalledOnlyByItsOwner_WhoHoldsDocumentCreate(int index)
    {
        var shape = All(StatusesOfDocument).ElementAt(index);
        foreach (var caller in Callers)
        {
            var thrown = Record.Exception(() => new DocumentAccess(new Caller(caller.Role)).EnsureMayRecall(DocumentOf(shape)));

            Assert.True(Refused(thrown, Messages.OwnRecallOnly) == !(shape.IsMine && caller.DocumentCreate), $"{caller.Role}: {shape}");
        }
    }

    [Theory]
    [MemberData(nameof(PurchaseCases))]
    public void APurchaseOrderIsRecalledOnlyByItsOwner_WhoHoldsPoManage(int index)
    {
        var shape = All(StatusesOfPurchaseOrder).ElementAt(index);
        foreach (var caller in Callers)
        {
            var thrown = Record.Exception(() => new PurchaseOrderAccess(new Caller(caller.Role)).EnsureMayRecall(PurchaseOrderOf(shape)));

            Assert.True(Refused(thrown, Messages.PurchaseOrderOwnRecallOnly) == !(shape.IsMine && caller.PoManage), $"{caller.Role}: {shape}");
        }
    }

    [Theory]
    [MemberData(nameof(SalesCases))]
    public void ASalesOrderIsRecalledOnlyByItsOwner_WhoHoldsSoManage(int index)
    {
        var shape = All(StatusesOfSalesOrder).ElementAt(index);
        foreach (var caller in Callers)
        {
            var thrown = Record.Exception(() => new SalesOrderAccess(new Caller(caller.Role)).EnsureMayRecall(SalesOrderOf(shape)));

            Assert.True(Refused(thrown, Messages.SalesOrderOwnRecallOnly) == !(shape.IsMine && caller.SoManage), $"{caller.Role}: {shape}");
        }
    }

    /// <summary>True for the refusal of the workflow (a 403 with its sentence); null is allowed; anything else is a failure of the test.</summary>
    private static bool Refused(Exception? thrown, string sentence)
    {
        if (thrown is null) return false;
        Assert.Equal(sentence, Assert.IsType<ForbiddenException>(thrown).Message);
        return true;
    }

    /// <summary>A signed-in caller with the default permissions of a role (none for an unknown one).</summary>
    private sealed class Caller(string role) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public int UserId => Me;
        public string Username => "caller";
        public string Role => role;
        public IReadOnlyCollection<int> WarehouseIds => [];
        public bool AllWarehouses => true;
        public bool CanAccessWarehouse(int warehouseId) => true;
        public ActorRights Rights() => RightsOf(role);
    }
}
