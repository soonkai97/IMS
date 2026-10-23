namespace Ims.Domain;

/// <summary>
/// The kind of a document, and the subject an <see cref="ApprovalRule"/> is written for. <see cref="PurchaseOrder"/> and <see cref="SalesOrder"/> are not stock documents (an order is its own aggregate and
/// moves no stock, plans PL2 D2 and PL3 D1): it is here only so that the approval rules and their two tiers are reused for it. <see cref="StockDocument.Create"/> refuses it and <see cref="StockDirection"/>
/// has no direction for it.
/// </summary>
public enum DocumentType { Receipt = 1, Issue = 2, Transfer = 3, Adjust = 4, PurchaseOrder = 5, SalesOrder = 6 }
