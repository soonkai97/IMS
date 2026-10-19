namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>The products the sequences use. All have the piece as base unit; two also have a carton (the conversion factor is what a unit conversion multiplies by).</summary>
internal static class OracleProducts
{
    public const int Plain = 1;
    public const int Carton12 = 2;
    public const int Carton7 = 3;
    public const int PlainB = 4;
    public const int PlainC = 5;

    public static readonly int[] All = [Plain, Carton12, Carton7, PlainB, PlainC];

    /// <summary>Pieces in a carton of the product; null when the product has no carton.</summary>
    public static decimal? CartonFactor(int productId) => productId switch
    {
        Carton12 => 12m,
        Carton7 => 7m,
        _ => null
    };

    public static string Sku(int productId) => $"P{productId}";

    public static Product Create(int productId) => TestData.NewProduct(productId, Sku(productId), cartonFactor: CartonFactor(productId));
}
