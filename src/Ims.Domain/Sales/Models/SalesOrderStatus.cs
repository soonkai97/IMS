namespace Ims.Domain;

/// <summary>
/// The stored state of a sales order. "Partly delivered" is a derived label, never stored. An order is open (it blocks switching the sales module off) while it is Draft, Submitted or Approved.
/// Only Draft is reachable until the workflow slice (PL3.3); the other values are in place so that slice needs no migration.
/// </summary>
public enum SalesOrderStatus { Draft = 0, Submitted = 1, Approved = 2, Closed = 3, Cancelled = 4 }
