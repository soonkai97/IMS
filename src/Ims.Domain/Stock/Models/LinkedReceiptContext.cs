namespace Ims.Domain;

/// <summary>What <see cref="LinkedReceiptPolicy"/> needs to know about a receipt created from a purchase order (pass none for any other document).</summary>
/// <param name="PurchasingOn">The Purchasing module is on.</param>
public readonly record struct LinkedReceiptContext(bool PurchasingOn);
