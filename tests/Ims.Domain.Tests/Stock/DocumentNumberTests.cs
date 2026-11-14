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
    [InlineData(DocumentType.SalesOrder, "SO")]
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

    // ----- The value: Create, ToString, FromStored (DDD-04) -----
    [Theory]
    [InlineData("RCV", "WH1", 2601, 1)]
    [InlineData("ISS", "MAIN", 2612, 42)]
    [InlineData("PO", "WH1", 2701, 9999)]
    [InlineData("SO", "WH1", 2701, 10000)]               // a running number above 9999 is never truncated
    [InlineData("ADJ", "WH1", 1, 7)]                      // January 2000: yyMM keeps its leading zeros
    [InlineData("TRF", "KL-01", 2601, 3)]                 // a warehouse code may contain the separator
    [InlineData("RCV", "A-B-C", 2601, 12345)]
    [InlineData("RCV", "W-2601", 2601, 1)]                // the code itself looks like a yyMM part
    [InlineData("ISS", "-X-", 2601, 5)]                   // the code starts and ends with the separator
    [InlineData("RCV", "WWWWWWWWWWWWWWWWWWWW", 2601, 9999)] // a warehouse code at Limits.CodeMaxLength (20)
    public void RoundTrip_CreateToStringFromStored_KeepsEveryPart(string prefix, string warehouse, int yearMonth, int running)
    {
        var created = DocumentNumber.Create(prefix, warehouse, yearMonth, running);

        var stored = created.ToString();
        var read = DocumentNumber.FromStored(stored);

        Assert.Equal(DocumentNumber.Format(prefix, warehouse, yearMonth, running), stored); // byte-identical to today's printed number
        Assert.Equal(created, read);
        Assert.Equal(stored, read.Value);
        Assert.True(read.IsWellFormed);
        Assert.Equal(prefix, read.Prefix);
        Assert.Equal(warehouse, read.WarehouseCode);
        Assert.Equal(yearMonth, read.YearMonth);
        Assert.Equal(running, read.Running);
    }

    [Fact]
    public void RoundTrip_EveryDocumentType_AtTheMalaysiaMonthBoundary()
    {
        var utc = new DateTime(2026, 12, 31, 16, 30, 0, DateTimeKind.Utc);

        foreach (var type in Enum.GetValues<DocumentType>())
        {
            var no = DocumentNumber.Create(DocumentNumber.PrefixFor(type), "WH1", DocumentNumber.YearMonthOf(utc), 3);

            Assert.Equal(no, DocumentNumber.FromStored(no.ToString()));
            Assert.Equal(DocumentNumber.PrefixFor(type), no.Prefix);
            Assert.Equal(2701, no.YearMonth);
        }
    }

    [Fact]
    public void Create_ValueIsTheSameTextAsFormat()
    {
        Assert.Equal("RCV-WH1-2601-0001", DocumentNumber.Create("RCV", "WH1", 2601, 1).Value);
    }

    [Fact]
    public void Create_AtTheStoredMaxLength_IsAccepted()
    {
        var warehouse = new string('W', Limits.DocumentNoMaxLength - "RCV-".Length - "-2601-0001".Length);

        var no = DocumentNumber.Create("RCV", warehouse, 2601, 1);

        Assert.Equal(Limits.DocumentNoMaxLength, no.Value.Length);
    }

    [Fact]
    public void Create_OverTheStoredMaxLength_IsRefusedWithTheExistingSentence()
    {
        var warehouse = new string('W', Limits.DocumentNoMaxLength - "RCV-".Length - "-2601-0001".Length + 1); // 41 characters in all

        var ex = Assert.Throws<DomainException>(() => DocumentNumber.Create("RCV", warehouse, 2601, 1));

        Assert.Equal(Messages.TextTooLong("Document number", Limits.DocumentNoMaxLength), ex.Message);
    }

    [Theory]
    [InlineData("", "WH1", 2601, 1)]       // no prefix
    [InlineData("R-V", "WH1", 2601, 1)]    // a prefix is read from the front up to the first separator, so it cannot hold one
    [InlineData("RCV", "", 2601, 1)]       // no warehouse
    [InlineData("RCV", "WH1", -1, 1)]
    [InlineData("RCV", "WH1", 2601, -1)]
    public void Create_AnImpossiblePart_IsAProgrammerError(string prefix, string warehouse, int yearMonth, int running)
    {
        Assert.ThrowsAny<ArgumentException>(() => DocumentNumber.Create(prefix, warehouse, yearMonth, running));
    }

    // ----- Unassigned and FromStored never throws -----
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FromStored_Nothing_IsUnassigned(string? stored)
    {
        Assert.Equal(DocumentNumber.Unassigned, DocumentNumber.FromStored(stored));
    }

    [Fact]
    public void Unassigned_IsTheEmptyText_AndTheDefaultValue()
    {
        Assert.Equal("", DocumentNumber.Unassigned.Value);
        Assert.Equal("", DocumentNumber.Unassigned.ToString());
        Assert.False(DocumentNumber.Unassigned.IsAssigned);
        Assert.False(DocumentNumber.Unassigned.IsWellFormed);
        Assert.Equal(default, DocumentNumber.Unassigned);
        Assert.Null(DocumentNumber.Unassigned.Prefix);
        Assert.Null(DocumentNumber.Unassigned.Running);
    }

    [Fact]
    public void AnAssignedNumber_IsAssigned()
    {
        Assert.True(DocumentNumber.Create("RCV", "WH1", 2601, 1).IsAssigned);
    }

    [Fact]
    public void FromStored_FortyOneCharacters_IsAcceptedBecauseAStoredValueMustAlwaysLoad()
    {
        var stored = "RCV-" + new string('W', Limits.DocumentNoMaxLength) + "-2601-0001";

        var no = DocumentNumber.FromStored(stored);

        Assert.True(stored.Length > Limits.DocumentNoMaxLength);
        Assert.Equal(stored, no.Value);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("RCV-WH1")]
    [InlineData("RCV-2601-0001")]            // no warehouse part
    [InlineData("RCV-WH1-26AB-0001")]
    [InlineData("RCV-WH1-2601-00x1")]
    [InlineData("RCV-WH1-2601-")]
    [InlineData("-WH1-2601-0001")]           // no prefix
    [InlineData("RCV-WH1-2601-99999999999")] // a running number beyond int
    [InlineData("  ")]
    [InlineData("---")]
    public void FromStored_AnythingElse_LoadsUnchanged_WithoutPartsAndWithoutThrowing(string stored)
    {
        var no = DocumentNumber.FromStored(stored);

        Assert.Equal(stored, no.Value);
        Assert.Equal(stored, no.ToString());
        Assert.False(no.IsWellFormed);
        Assert.Null(no.Prefix);
        Assert.Null(no.WarehouseCode);
        Assert.Null(no.YearMonth);
        Assert.Null(no.Running);
    }

    [Fact]
    public void Equality_IsByTheText_Ordinally()
    {
        var a = DocumentNumber.Create("RCV", "WH1", 2601, 1);

        Assert.Equal(a, DocumentNumber.FromStored("RCV-WH1-2601-0001"));
        Assert.True(a == DocumentNumber.FromStored("RCV-WH1-2601-0001"));
        Assert.NotEqual(a, DocumentNumber.FromStored("rcv-wh1-2601-0001"));
        Assert.NotEqual(a, DocumentNumber.Create("RCV", "WH1", 2601, 2));
        Assert.Equal(a.GetHashCode(), DocumentNumber.FromStored("RCV-WH1-2601-0001").GetHashCode());
    }

    [Fact]
    public void TheValue_IsAMarkedValueObject()
    {
        Assert.True(typeof(IValueObject).IsAssignableFrom(typeof(DocumentNumber)));
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
