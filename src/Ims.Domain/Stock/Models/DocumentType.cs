namespace Ims.Domain;

/// <summary>
/// The kind of a document, and the subject an <see cref="ApprovalRule"/> is written for. <see cref="PurchaseOrder"/> is not a stock document (a purchase order is its own aggregate and
/// moves no stock, plan D2): it is here only so that the approval rules and their two tiers are reused for it. <see cref="StockDocument.Create"/> refuses it and <see cref="StockDirection"/>
/// has no direction for it.
/// </summary>
public enum DocumentType { Receipt = 1, Issue = 2, Transfer = 3, Adjust = 4, PurchaseOrder = 5 }
