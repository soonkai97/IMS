namespace Ims.Domain;

public class ProductBarcode : Entity
{
    private ProductBarcode() { }

    public int ProductId { get; private set; }
    public string Barcode { get; private set; } = "";

    public static ProductBarcode Create(string barcode) => new() { Barcode = barcode.Trim() };
}
