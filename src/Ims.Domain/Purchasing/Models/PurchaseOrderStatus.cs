namespace Ims.Domain;

/// <summary>
/// The stored state of a purchase order. Draft is the only state a caller can reach before the approval workflow exists (pilot slice PL2.3); "partly received" and "fully received" are
/// derived labels, never stored. An order is open (it blocks switching the purchasing module off) while it is Draft, Submitted or Approved.
/// </summary>
public enum PurchaseOrderStatus { Draft = 0, Submitted = 1, Approved = 2, Closed = 3, Cancelled = 4 }
