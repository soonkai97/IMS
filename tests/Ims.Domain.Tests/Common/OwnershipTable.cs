using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// DDD-09 part B: whose is a document or an order to take back (recall)? Written from the rule, not from the code: it is theirs when they submitted it; a Draft is theirs as well when they made it or
/// were the last to change it (a recall names no submitter afterwards, so their retry must still count); anyone else, in every status, is refused. The caller must also hold the permission of the
/// workflow (<c>document.create</c>, <c>po.manage</c>, <c>so.manage</c>). One table for the three access services (before the change, and after it) and for <c>DocumentOwnership</c>.
/// </summary>
internal static class OwnershipTable
{
    internal const int Me = 21;
    internal const int Other = 22;

    /// <summary>Who of <see cref="Me"/>, someone else or nobody (null).</summary>
    internal enum Who { Me, Other, Nobody }

    internal sealed record Shape(string Status, Who Submitter, Who Creator, Who Revisor)
    {
        public bool IsDraft => Status == "Draft";

        /// <summary>The rule, as the PRD words it (WF recall): the submitter, or for a Draft the one who made or last changed it.</summary>
        public bool IsMine => Submitter == Who.Me || (IsDraft && (Creator == Who.Me || Revisor == Who.Me));

        public int? SubmittedBy => Id(Submitter);
        public int CreatedBy => Id(Creator) ?? Other;
        public int? RevisedBy => Id(Revisor);

        private static int? Id(Who who) => who switch { Who.Me => Me, Who.Other => Other, _ => null };
        public override string ToString() => $"{Status}, submitted by {Submitter}, made by {Creator}, last changed by {Revisor}";
    }

    /// <summary>Every status of the type, and every combination of who submitted it, who made it and who changed it last (a creator is never nobody).</summary>
    internal static IEnumerable<Shape> All(IEnumerable<string> statuses) =>
        from status in statuses
        from submitter in Enum.GetValues<Who>()
        from creator in new[] { Who.Me, Who.Other }
        from revisor in Enum.GetValues<Who>()
        select new Shape(status, submitter, creator, revisor);

    /// <summary>The five system roles and a caller with no permission, with what each holds by default (the matrix of the PRD, written out here, not read from the code).</summary>
    internal static readonly (string Role, bool DocumentCreate, bool PoManage, bool SoManage)[] Callers =
    [
        (SystemRoleCodes.Admin, true, true, true),
        (SystemRoleCodes.Owner, false, false, true),
        (SystemRoleCodes.Supervisor, true, true, true),
        (SystemRoleCodes.WarehouseStaff, true, false, false),
        (SystemRoleCodes.Clerk, true, true, true),
        ("nobody", false, false, false),
    ];

    internal static ActorRights RightsOf(string role) => SystemRoleDefaults.RightsFor(role, Me);

    // ----- the three aggregates in the shape of a row; the audit columns are stamped by persistence, so a test sets them as persistence does -----
    internal static StockDocument DocumentOf(Shape s)
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));
        return doc.WithPrivate("CreatedBy", s.CreatedBy).WithPrivate("SubmittedBy", s.SubmittedBy).WithPrivate("RecRevisor", s.RevisedBy)
            .WithPrivate("Status", Enum.Parse<DocumentStatus>(s.Status));
    }

    internal static PurchaseOrder PurchaseOrderOf(Shape s)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, Creator, Now);
        return order.WithPrivate("CreatedBy", s.CreatedBy).WithPrivate("SubmittedBy", s.SubmittedBy).WithPrivate("RecRevisor", s.RevisedBy)
            .WithPrivate("Status", Enum.Parse<PurchaseOrderStatus>(s.Status));
    }

    internal static SalesOrder SalesOrderOf(Shape s)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Creator, Now);
        return order.WithPrivate("CreatedBy", s.CreatedBy).WithPrivate("SubmittedBy", s.SubmittedBy).WithPrivate("RecRevisor", s.RevisedBy)
            .WithPrivate("Status", Enum.Parse<SalesOrderStatus>(s.Status));
    }

    internal static IEnumerable<string> StatusesOfDocument => Enum.GetNames<DocumentStatus>();
    internal static IEnumerable<string> StatusesOfPurchaseOrder => Enum.GetNames<PurchaseOrderStatus>();
    internal static IEnumerable<string> StatusesOfSalesOrder => Enum.GetNames<SalesOrderStatus>();
}
