using static Ims.Domain.Tests.Attachments.TestImages;

namespace Ims.Domain.Tests.Attachments;

public class ImageMetadataStripperTests
{
    // ---------- JPEG ----------

    [Fact]
    public void Jpeg_loses_exif_xmp_iptc_comments_and_trailing_bytes_but_keeps_what_decoding_needs()
    {
        var input = Jpeg(exif: true, comment: true, trailing: true, icc: true, adobe: true);

        var output = ImageMetadataStripper.Strip(FileKind.Jpeg, input)!;

        Assert.NotNull(output);
        Assert.False(Contains(output, GpsMarker), "GPS/EXIF survived");
        Assert.False(Contains(output, "Exif"));
        Assert.False(Contains(output, "xmpmeta"));
        Assert.False(Contains(output, "Photoshop"));
        Assert.False(Contains(output, CommentMarker));
        Assert.False(Contains(output, TrailingMarker), "bytes after the end-of-image marker survived");
        Assert.True(Contains(output, "JFIF"));
        Assert.True(Contains(output, "ICC_PROFILE"));
        Assert.True(Contains(output, "Adobe"));
        Assert.Equal([0xFF, 0xD8], output[..2]);
        Assert.Equal([0xFF, 0xD9], output[^2..]);
        Assert.Equal(AttachmentRules.Detect(output), FileKind.Jpeg);
    }

    [Fact]
    public void Jpeg_without_metadata_is_returned_byte_for_byte()
    {
        var input = Jpeg(icc: true);

        Assert.Equal(input, ImageMetadataStripper.Strip(FileKind.Jpeg, input));
    }

    [Fact]
    public void Jpeg_stripping_is_idempotent()
    {
        var once = ImageMetadataStripper.Strip(FileKind.Jpeg, Jpeg(exif: true, comment: true, trailing: true))!;

        Assert.Equal(once, ImageMetadataStripper.Strip(FileKind.Jpeg, once));
    }

    [Fact]
    public void Jpeg_entropy_data_with_stuffed_bytes_and_restart_markers_is_copied_intact()
    {
        var output = ImageMetadataStripper.Strip(FileKind.Jpeg, Jpeg(exif: true))!;

        Assert.True(output.AsSpan().IndexOf(new byte[] { 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56 }) >= 0);
    }

    [Theory]
    [InlineData("magic bytes only", new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData("soi and eoi but no frame or scan", new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 })]
    [InlineData("segment longer than the file", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x7F, 0xFF, 0x00 })]
    [InlineData("segment length below the minimum", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0x00 })]
    public void Broken_jpegs_are_refused(string _, byte[] input)
    {
        Assert.Null(ImageMetadataStripper.Strip(FileKind.Jpeg, input));
    }

    [Fact]
    public void A_truncated_jpeg_without_end_marker_is_refused()
    {
        var input = Jpeg();

        Assert.Null(ImageMetadataStripper.Strip(FileKind.Jpeg, input[..^2]));
        Assert.Null(ImageMetadataStripper.Strip(FileKind.Jpeg, input[..(input.Length / 2)]));
    }

    [Fact]
    public void A_scan_before_the_frame_header_is_refused()
    {
        byte[] input = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0, 0x12, 0xFF, 0xD9];

        Assert.Null(ImageMetadataStripper.Strip(FileKind.Jpeg, input));
    }

    // ---------- PNG ----------

    [Fact]
    public void Png_loses_text_exif_time_and_trailing_bytes_but_keeps_pixels_and_colour_chunks()
    {
        var input = Png(text: true, exif: true, time: true, trailing: true);

        var output = ImageMetadataStripper.Strip(FileKind.Png, input)!;

        Assert.NotNull(output);
        Assert.False(Contains(output, "tEXt"));
        Assert.False(Contains(output, "eXIf"));
        Assert.False(Contains(output, "tIME"));
        Assert.False(Contains(output, CommentMarker));
        Assert.False(Contains(output, GpsMarker));
        Assert.False(Contains(output, TrailingMarker));
        Assert.True(Contains(output, "IHDR") && Contains(output, "pHYs") && Contains(output, "IDAT") && Contains(output, "IEND"));
        Assert.Equal(Png(), output); // exactly the same file as one that never had metadata
    }

    [Fact]
    public void Png_without_metadata_is_returned_byte_for_byte()
    {
        var input = Png();

        Assert.Equal(input, ImageMetadataStripper.Strip(FileKind.Png, input));
    }

    [Fact]
    public void The_one_pixel_png_used_by_the_smoke_test_is_returned_byte_for_byte()
    {
        var smokePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        Assert.Equal(smokePng, ImageMetadataStripper.Strip(FileKind.Png, smokePng));
    }

    [Fact]
    public void Png_without_ihdr_first_or_without_iend_or_truncated_is_refused()
    {
        var valid = Png();
        var signature = valid[..8];
        var idatOnly = valid[(8 + 25)..]; // drop IHDR (12 + 13 bytes)

        Assert.Null(ImageMetadataStripper.Strip(FileKind.Png, [.. signature, .. idatOnly]));
        Assert.Null(ImageMetadataStripper.Strip(FileKind.Png, valid[..^12])); // no IEND
        Assert.Null(ImageMetadataStripper.Strip(FileKind.Png, valid[..(valid.Length - 5)]));
        Assert.Null(ImageMetadataStripper.Strip(FileKind.Png, signature));
    }

    [Fact]
    public void A_png_chunk_that_claims_more_bytes_than_exist_is_refused()
    {
        byte[] input = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x7F, 0xFF, 0xFF, 0xFF, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 0];

        Assert.Null(ImageMetadataStripper.Strip(FileKind.Png, input));
    }

    // ---------- WebP ----------

    [Fact]
    public void Webp_loses_exif_and_xmp_chunks_clears_their_flags_and_fixes_the_riff_size()
    {
        var input = Webp(exif: true, xmp: true, trailing: true);

        var output = ImageMetadataStripper.Strip(FileKind.Webp, input)!;

        Assert.NotNull(output);
        Assert.False(Contains(output, "EXIF"));
        Assert.False(Contains(output, "XMP "));
        Assert.False(Contains(output, GpsMarker));
        Assert.False(Contains(output, TrailingMarker));
        Assert.True(Contains(output, "VP8X") && Contains(output, "VP8 "));
        Assert.Equal(0, output[20] & 0x0C); // VP8X flags byte: EXIF (0x08) and XMP (0x04) cleared
        Assert.Equal((uint)(output.Length - 8), BitConverter.ToUInt32(output, 4));
        Assert.Equal(Webp(), output);
        Assert.Equal(FileKind.Webp, AttachmentRules.Detect(output));
    }

    [Fact]
    public void Webp_without_metadata_is_returned_byte_for_byte()
    {
        var input = Webp();

        Assert.Equal(input, ImageMetadataStripper.Strip(FileKind.Webp, input));
    }

    [Fact]
    public void Truncated_or_imageless_webp_is_refused()
    {
        var valid = Webp();

        Assert.Null(ImageMetadataStripper.Strip(FileKind.Webp, valid[..^3]));
        Assert.Null(ImageMetadataStripper.Strip(FileKind.Webp, valid[..12]));
    }

    // ---------- other ----------

    [Fact]
    public void A_pdf_is_not_an_image_and_is_not_handled_here()
    {
        Assert.Null(ImageMetadataStripper.Strip(FileKind.Pdf, "%PDF-1.4"u8));
    }
}
