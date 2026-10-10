namespace Ims.Application.Features.Catalog;

/// <summary>Sort keys the products list accepts. Only these are allowed, so a client cannot sort on an unindexed column.</summary>
public enum ProductSort { Sku = 0, Name = 1 }
