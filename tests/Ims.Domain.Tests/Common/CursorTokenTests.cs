using Ims.Application;

namespace Ims.Domain.Tests.Common;

/// <summary>The cursor of a seek-paged list is an opaque, versioned, URL-safe token; anything the server did not make is refused with a 400, never an exception that becomes a 500.</summary>
public class CursorTokenTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(255L)]
    [InlineData(4_294_967_296L)]
    [InlineData(long.MaxValue)]
    public void A_position_survives_the_round_trip(long position)
    {
        var token = CursorToken.Encode(position);

        Assert.True(CursorToken.TryDecode(token, out var decoded));
        Assert.Equal(position, decoded);
        Assert.Equal(position, CursorToken.Decode(token));
    }

    [Fact]
    public void A_token_is_url_safe_short_and_does_not_show_the_key()
    {
        var token = CursorToken.Encode(1_234_567_890L);

        Assert.Matches("^[A-Za-z0-9_-]+$", token);   // no '+', '/' or '=' that a query string would need escaped
        Assert.True(token.Length <= Limits.CursorMaxLength);
        Assert.DoesNotContain("1234567890", token);
    }

    [Fact]
    public void The_wire_format_is_pinned_because_the_load_test_builds_cursors_the_same_way()
    {
        Assert.Equal("AQAAAAAAAAAB", CursorToken.Encode(1));            // version 1, then the position as 8 bytes, big endian
        Assert.Equal("AQAAAABJlgLS", CursorToken.Encode(1_234_567_890));
    }

    [Fact]
    public void No_cursor_means_the_first_page()
    {
        Assert.Null(CursorToken.Decode(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("garbage")]
    [InlineData("not a cursor!")]
    [InlineData("AQAAAAAAAAAB+A")]   // base64 with a character base64url does not have
    [InlineData("AQAAAAAAAAAB==")]   // padded
    public void Garbage_is_refused(string token)
    {
        Assert.False(CursorToken.TryDecode(token, out _));
        var refused = Assert.Throws<ValidationException>(() => CursorToken.Decode(token));
        Assert.Equal([Messages.CursorInvalid], refused.Errors["cursor"]);
    }

    [Fact]
    public void A_truncated_or_extended_token_is_refused()
    {
        var token = CursorToken.Encode(42);

        Assert.False(CursorToken.TryDecode(token[..^1], out _));
        Assert.False(CursorToken.TryDecode(token[..4], out _));
        Assert.False(CursorToken.TryDecode(token + "AA", out _));
    }

    [Fact]
    public void Another_version_is_refused()
    {
        var token = CursorToken.Encode(42);
        var otherVersion = (token[0] == 'A' ? 'B' : 'A') + token[1..];   // changes the first character, which holds the version byte

        Assert.False(CursorToken.TryDecode(otherVersion, out _));
    }

    [Fact]
    public void A_position_below_one_is_refused()
    {
        Assert.False(CursorToken.TryDecode(CursorToken.Encode(0), out _));
        Assert.False(CursorToken.TryDecode(CursorToken.Encode(-7), out _));
    }

    [Fact]
    public void A_huge_input_is_refused_without_being_decoded()
    {
        var huge = new string('A', 1_000_000);

        Assert.False(CursorToken.TryDecode(huge, out _));
        Assert.Throws<ValidationException>(() => CursorToken.Decode(huge));
    }
}
