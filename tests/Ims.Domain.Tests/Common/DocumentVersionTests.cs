using System.Text;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Domain.Tests;

/// <summary>PID-02: the concurrency token of a document is a keyed hash bound to the document, never the raw (database-wide) row version.</summary>
public class DocumentVersionTests
{
    private sealed class Key(string secret) : ITokenValidationSettings
    {
        public string Issuer => "i";
        public string Audience => "a";
        public byte[] SigningKey { get; } = Encoding.UTF8.GetBytes(secret);
    }

    private static DocumentVersion Versions(string secret = "a-signing-key-of-at-least-32-characters") => new(new Key(secret));

    private static byte[] RowVersion(long counter) => [0, 0, 0, 0, 0, 0, (byte)(counter >> 8), (byte)counter];

    private static StockDocument Document(byte[] rowVersion) => TestData.NewReceipt(1, (TestData.NewProduct(), 1m, 1m)).WithPrivate("RowVersion", rowVersion);

    [Fact]
    public void TheSameDocumentAndVersionAlwaysGiveTheSameToken()
    {
        var id = Guid.NewGuid();

        Assert.Equal(Versions().Token(id, RowVersion(2347)), Versions().Token(id, RowVersion(2347)));
    }

    [Fact]
    public void ANewVersionOrAnotherDocumentOrAnotherKeyGivesAnotherToken()
    {
        var id = Guid.NewGuid();
        var token = Versions().Token(id, RowVersion(2347));

        Assert.NotEqual(token, Versions().Token(id, RowVersion(2348)));
        Assert.NotEqual(token, Versions().Token(Guid.NewGuid(), RowVersion(2347)));
        Assert.NotEqual(token, Versions("another-signing-key-of-at-least-32-chars").Token(id, RowVersion(2347)));
    }

    [Fact]
    public void TheTokenIsNotTheRowVersionAndFollowsNoCounter()
    {
        var id = Guid.NewGuid();
        var tokens = Enumerable.Range(1000, 64).Select(n => Versions().Token(id, RowVersion(n))).ToList();

        Assert.All(tokens, t => Assert.False(long.TryParse(t, out _) || Convert.ToBase64String(RowVersion(1000)) == t, t));
        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]{22}$", t));
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        // No order and no shared prefix: consecutive versions do not look like neighbours.
        Assert.NotEqual(tokens.OrderBy(t => t, StringComparer.Ordinal).ToList(), tokens);
        Assert.True(tokens.Zip(tokens.Skip(1)).All(p => CommonPrefix(p.First, p.Second) <= 3), "consecutive tokens share a long prefix");
    }

    private static int CommonPrefix(string a, string b) => a.Zip(b).TakeWhile(p => p.First == p.Second).Count();

    [Fact]
    public void AnEmptyRowVersionStillGivesAToken()
    {
        Assert.Matches("^[A-Za-z0-9_-]{22}$", Versions().Token(Guid.NewGuid(), null));
    }

    [Fact]
    public void NoTokenIsNoCheck_AndTheCurrentTokenPasses()
    {
        var versions = Versions();
        var doc = Document(RowVersion(5));

        versions.EnsureCurrent(doc, null);
        versions.EnsureCurrent(doc, "");
        versions.EnsureCurrent(doc, versions.Token(doc.PublicId, doc.RowVersion));
    }

    [Fact]
    public void AStaleTokenIsRefused()
    {
        var versions = Versions();
        var doc = Document(RowVersion(5));
        var stale = versions.Token(doc.PublicId, RowVersion(4));

        Assert.Throws<StaleDocumentException>(() => versions.EnsureCurrent(doc, stale));
    }

    [Fact]
    public void TheTokenOfAnotherDocumentWithTheSameVersionIsRefused()
    {
        var versions = Versions();
        var doc = Document(RowVersion(5));

        Assert.Throws<StaleDocumentException>(() => versions.EnsureCurrent(doc, versions.Token(Guid.NewGuid(), doc.RowVersion)));
    }

    [Fact]
    public void ATokenMadeWithAnotherKeyIsRefused()
    {
        var doc = Document(RowVersion(5));

        Assert.Throws<StaleDocumentException>(() => Versions().EnsureCurrent(doc, Versions("another-signing-key-of-at-least-32-chars").Token(doc.PublicId, doc.RowVersion)));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData(" ")]
    [InlineData("AAAAAAAACHw=")]                                   // the old form: the raw row version in base64
    [InlineData("\"quoted\"")]
    [InlineData("%00%00")]
    [InlineData("é中文")]
    [InlineData("*")]
    public void AnythingThatIsNotTheCurrentTokenIsStale_NeverAnotherError(string sent)
    {
        Assert.Throws<StaleDocumentException>(() => Versions().EnsureCurrent(Document(RowVersion(5)), sent));
    }

    [Fact]
    public void ATamperedOrCutOrExtendedTokenIsStale()
    {
        var versions = Versions();
        var doc = Document(RowVersion(5));
        var token = versions.Token(doc.PublicId, doc.RowVersion);
        var flipped = (token[0] == 'A' ? 'B' : 'A') + token[1..];

        foreach (var sent in new[] { flipped, token[..^1], token + "A", token + token, new string('A', 100_000) })
            Assert.Throws<StaleDocumentException>(() => versions.EnsureCurrent(doc, sent));
    }
}
