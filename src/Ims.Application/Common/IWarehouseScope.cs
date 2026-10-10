namespace Ims.Application;

/// <summary>Which warehouses the caller may read. Null means all of them.</summary>
public interface IWarehouseScope
{
    IReadOnlyCollection<int>? Ids();
}
