namespace Ims.Domain.Tests.Stock;

public class DocumentNumberTests
{
    // ----- Prefix -----
    [Theory]
    [InlineData(DocumentType.Receipt, "RCV")]
    [InlineData(DocumentType.Issue, "ISS")]
    [InlineData(DocumentType.Transfer, "TRF")]
    [InlineData(DocumentType.Adjust, "ADJ")]
    [InlineData(DocumentType.PurchaseOrder, "PO")]
    public void PrefixFor_EachDocumentType(DocumentType type, string expected)
    {
        Assert.Equal(expected, DocumentNumber.PrefixFor(type));
    }

    [Fact]
    public void PrefixFor_EveryTypeHasADistinctPrefix()
    {
        var prefixes = Enum.GetValues<DocumentType>().Select(DocumentNumber.PrefixFor).ToList();

        Assert.Equal(prefixes.Count, prefixes.Distinct().Count());
    }

    [Fact]
    public void PrefixFor_FitsTheConfiguredMaxLength()
    {
        foreach (var type in Enum.GetValues<DocumentType>())
            Assert.True(DocumentNumber.PrefixFor(type).Length <= Limits.DocumentPrefixMaxLength);
    }

    // ----- yyMM in Malaysia time (UTC+8) -----
    [Theory]
    [InlineData(2026, 1, 15, 8, 0, 2601)]
    [InlineData(2026, 6, 30, 15, 59, 2606)]  // 23:59 in Malaysia, still June
    [InlineData(2026, 6, 30, 16, 0, 2607)]   // 00:00 in Malaysia, July
    [InlineData(2026, 12, 31, 15, 59, 2612)]
    [InlineData(2026, 12, 31, 16, 0, 2701)]  // year boundary
    [InlineData(2026, 12, 31, 16, 30, 2701)] // 31 Dec 16:30 UTC is 1 Jan 00:30 in Malaysia
    [InlineData(2028, 2, 29, 16, 0, 2803)]   // leap day rolls into March
    [InlineData(2099, 12, 31, 16, 0, 0001)]  // century wraps to 00 + month 01
    public void YearMonthOf_UsesMalaysiaTime(int year, int month, int day, int hour, int minute, int expected)
    {
        var utc = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

        Assert.Equal(expected, DocumentNumber.YearMonthOf(utc));
    }

    // ----- Format -----
    [Fact]
    public void Format_PrefixWarehouseYearMonthRunning()
    {
        Assert.Equal("RCV-WH1-2601-0001", DocumentNumber.Format("RCV", "WH1", 2601, 1));
    }

    [Theory]
    [InlineData(1, "0001")]
    [InlineData(42, "0042")]
    [InlineData(9999, "9999")]
    [InlineData(12345, "12345")] // never truncated
    public void Format_RunningNumberIsZeroPaddedToFourDigits(int running, string expectedSuffix)
    {
        Assert.EndsWith("-" + expectedSuffix, DocumentNumber.Format("ISS", "MAIN", 2601, running));
    }

    [Fact]
    public void Format_JanuaryOfYear2000_KeepsLeadingZeros()
    {
        Assert.Equal("ADJ-WH1-0001-0007", DocumentNumber.Format("ADJ", "WH1", 1, 7));
    }

    [Fact]
    public void Format_ComposesWithYearMonthOfAtTheBoundary()
    {
        var utc = new DateTime(2026, 12, 31, 16, 30, 0, DateTimeKind.Utc);

        var no = DocumentNumber.Format(DocumentNumber.PrefixFor(DocumentType.Receipt), "WH1", DocumentNumber.YearMonthOf(utc), 3);

        Assert.Equal("RCV-WH1-2701-0003", no);
    }

    [Fact]
    public void Format_NumberFitsTheStoredMaxLength()
    {
        var no = DocumentNumber.Format("RCV", new string('W', Limits.CodeMaxLength), 2601, 9999);

        Assert.True(no.Length <= Limits.DocumentNoMaxLength);
    }

    // ----- DocumentSequence -----
    [Fact]
    public void DocumentSequence_StartsAtZeroAndCountsUp()
    {
        var sequence = DocumentSequence.Start("RCV", 2601, warehouseId: 1);

        Assert.Equal(0, sequence.LastNumber);
        Assert.Equal(1, sequence.Next());
        Assert.Equal(2, sequence.Next());
        Assert.Equal(2, sequence.LastNumber);
    }

    [Fact]
    public void DocumentSequence_KeepsItsKey()
    {
        var sequence = DocumentSequence.Start("ISS", 2607, warehouseId: 4);

        Assert.Equal("ISS", sequence.Prefix);
        Assert.Equal(2607, sequence.YearMonth);
        Assert.Equal(4, sequence.WarehouseId);
    }
}
