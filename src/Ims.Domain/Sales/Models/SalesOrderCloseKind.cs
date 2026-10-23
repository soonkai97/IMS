namespace Ims.Domain;

/// <summary>How a sales order was closed: every line was delivered (by posting), or a person closed it short with a reason.</summary>
public enum SalesOrderCloseKind { Delivered = 1, Short = 2 }
