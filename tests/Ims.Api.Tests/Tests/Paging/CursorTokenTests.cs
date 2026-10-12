using System.Buffers.Binary;
using System.Buffers.Text;
using Ims.Application;
using Ims.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Ims.Api.Tests.Tests.Paging;

/// <summary>
/// The cursor of a seek-paged list is an encrypted, versioned, URL-safe token. It hides the row id, and anything the server did not make for that list is refused
/// (a 400 on the handler side), never an exception that becomes a 500. No database: the protector runs on an in-memory key ring.
/// </summary>
public class CursorTokenTests
{
    private const string Documents = CursorLists.Documents;
    private const string Ledger = CursorLists.Ledger;

    private readonly EphemeralDataProtectionProvider keys = new();
    private readonly DataProtectionCursorProtector protector;

    public CursorTokenTests() => protector = new DataProtectionCursorProtector(keys);

    private static byte[] Payload(byte version, long position)
    {
        var bytes = new byte[9];
        bytes[0] = version;
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(1), position);
        return bytes;
    }

    private bool Decodes(string? token, out long position, string list = Documents) => CursorToken.TryDecode(protector, list, token, out position);

    [Theory]
    [InlineData(1L)]
    [InlineData(255L)]
    [InlineData(4_294_967_296L)]
    [InlineData(long.MaxValue)]
    public void A_position_survives_the_round_trip(long position)
    {
        var token = CursorToken.Encode(protector, Documents, position);

        Assert.True(Decodes(token, out var decoded));
        Assert.Equal(position, decoded);
        Assert.Equal(position, CursorToken.Decode(protector, Documents, token));
    }

    [Fact]
    public void A_token_is_url_safe_and_fits_the_length_limit()
    {
        var token = CursorToken.Encode(protector, Documents, 1_234_567_890L);

        Assert.Matches("^[A-Za-z0-9_-]+$", token);   // no '+', '/' or '=' that a query string would need escaped
        Assert.InRange(token.Length, 1, Limits.CursorMaxLength);
    }

    [Fact]
    public void A_token_does_not_reveal_the_id_or_a_relation_between_two_ids()
    {
        var plain = Base64Url.EncodeToString(Payload(1, 1_000));     // what the token would be without the protector
        var next = Base64Url.EncodeToString(Payload(1, 1_001));
        var a = CursorToken.Encode(protector, Documents, 1_000);
        var b = CursorToken.Encode(protector, Documents, 1_001);

        Assert.DoesNotContain(plain, a);
        Assert.DoesNotContain("1000", a);
        Assert.NotEqual(a, CursorToken.Encode(protector, Documents, 1_000));   // fresh IV each time: the same row has no fixed token
        Assert.NotEqual(a[^40..], b[^40..]);                                  // neighbouring ids share no readable tail
        Assert.DoesNotContain(next, b);
    }

    [Fact]
    public void A_token_only_opens_with_the_key_that_made_it()
    {
        var token = CursorToken.Encode(protector, Documents, 42);
        var otherKeys = new DataProtectionCursorProtector(new EphemeralDataProtectionProvider());

        Assert.False(CursorToken.TryDecode(otherKeys, Documents, token, out _));
    }

    [Fact]
    public void No_cursor_means_the_first_page()
    {
        Assert.Null(CursorToken.Decode(protector, Documents, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("garbage")]
    [InlineData("not a cursor!")]
    [InlineData("AQAAAAAAAAAB")]    // the unencrypted shape of an earlier design
    [InlineData("AQAAAAAAAAAB==")]
    public void Garbage_is_refused(string token)
    {
        Assert.False(Decodes(token, out _));
        var refused = Assert.Throws<ValidationException>(() => CursorToken.Decode(protector, Documents, token));
        Assert.Equal([Messages.CursorInvalid], refused.Errors["cursor"]);
    }

    [Fact]
    public void A_changed_character_is_refused()
    {
        var token = CursorToken.Encode(protector, Documents, 42);

        for (var i = 0; i < token.Length; i++)
        {
            var other = token[i] == 'A' ? 'B' : 'A';
            Assert.False(Decodes(token[..i] + other + token[(i + 1)..], out _), $"changing character {i} was accepted");
        }
    }

    [Fact]
    public void A_truncated_or_extended_token_is_refused()
    {
        var token = CursorToken.Encode(protector, Documents, 42);

        Assert.False(Decodes(token[..^1], out _));
        Assert.False(Decodes(token[..^4], out _));
        Assert.False(Decodes(token[..4], out _));
        Assert.False(Decodes(token + "AAAA", out _));
    }

    [Fact]
    public void Another_spelling_of_a_valid_token_is_refused()
    {
        var token = CursorToken.Encode(protector, Documents, 42);

        Assert.True(Decodes(token, out _));
        Assert.False(Decodes(" " + token, out _));
        Assert.False(Decodes(token + "\n", out _));
        Assert.False(Decodes(token[..10] + " " + token[10..], out _));
        Assert.False(Decodes(token + "=", out _));

        var standardBase64 = token.Replace('-', '+').Replace('_', '/');
        Assert.True(standardBase64 == token || !Decodes(standardBase64, out _));
    }

    [Fact]
    public void A_cursor_of_another_list_is_refused()
    {
        var token = CursorToken.Encode(protector, Ledger, 42);

        Assert.True(Decodes(token, out _, Ledger));
        Assert.False(Decodes(token, out _, Documents));
        Assert.False(Decodes(token, out _, CursorLists.Audit));
    }

    [Fact]
    public void A_token_protected_for_another_purpose_is_refused()
    {
        var otherPurpose = keys.CreateProtector("Some.Other.Purpose", Documents).Protect(Payload(1, 42));
        var rightPurpose = keys.CreateProtector(DataProtectionCursorProtector.Purpose, Documents).Protect(Payload(1, 42));

        Assert.False(Decodes(Base64Url.EncodeToString(otherPurpose), out _));
        Assert.True(Decodes(Base64Url.EncodeToString(rightPurpose), out var position));   // the control: same bytes, the right purpose
        Assert.Equal(42, position);
    }

    [Fact]
    public void Another_version_or_a_position_below_one_is_refused()
    {
        var purpose = keys.CreateProtector(DataProtectionCursorProtector.Purpose, Documents);

        Assert.False(Decodes(Base64Url.EncodeToString(purpose.Protect(Payload(2, 42))), out _));
        Assert.False(Decodes(Base64Url.EncodeToString(purpose.Protect(Payload(1, 0))), out _));
        Assert.False(Decodes(Base64Url.EncodeToString(purpose.Protect(Payload(1, -7))), out _));
        Assert.False(Decodes(Base64Url.EncodeToString(purpose.Protect(new byte[] { 1, 2, 3 })), out _));   // right key, wrong length
    }

    [Fact]
    public void A_huge_input_is_refused_before_it_is_decoded()
    {
        var huge = new string('A', 1_000_000);

        Assert.False(Decodes(huge, out _));
        Assert.Throws<ValidationException>(() => CursorToken.Decode(protector, Documents, huge));
    }
}
