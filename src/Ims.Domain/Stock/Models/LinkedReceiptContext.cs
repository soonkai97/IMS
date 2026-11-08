namespace Ims.Domain;

/// <summary>What <see cref="LinkedReceiptPolicy"/> needs to know about a document created from an order (pass none for any other document): a receipt from a purchase order, or a delivery from a sales order.</summary>
/// <param name="ModuleOn">The module that owns the order (Purchasing, or Sales for a delivery) is on.</param>
/// <param name="Kind">Which order the document was created from; decides which read permission the caller needs (<c>po.read</c> or <c>so.read</c>).</param>
public readonly record struct LinkedReceiptContext(bool ModuleOn, DocumentLinkKind Kind);
