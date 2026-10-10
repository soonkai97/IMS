using System.Buffers.Binary;
using System.Text;

namespace Ims.Domain;

/// <summary>
/// Removes everything from an image that is not pixels or colour: EXIF (camera, GPS position of the phone), XMP, IPTC, comments, text
/// chunks, and any bytes appended after the image's end marker (a common place to hide a second file). Works on the container structure
/// only, with no decoding and no third-party library, so the pixels are never touched and the result is byte-identical for a file that
/// carries no metadata. A file whose structure is broken or truncated is refused (the method returns null).
/// </summary>
public static class ImageMetadataStripper
{
    public static byte[]? Strip(FileKind kind, ReadOnlySpan<byte> data) => kind switch
    {
        FileKind.Jpeg => StripJpeg(data),
        FileKind.Png => StripPng(data),
        FileKind.Webp => StripWebp(data),
        _ => null
    };

    // ---------------------------------------------------------------- JPEG

    private const byte Marker = 0xFF;
    private const byte Soi = 0xD8, Eoi = 0xD9, Sos = 0xDA, Tem = 0x01;
    private const byte Rst0 = 0xD0, Rst7 = 0xD7;
    private const byte App0 = 0xE0, App2 = 0xE2, App14 = 0xEE, Com = 0xFE;
    private static readonly byte[] IccSignature = "ICC_PROFILE\0"u8.ToArray();

    private static byte[]? StripJpeg(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[0] != Marker || d[1] != Soi) return null;
        using var output = new MemoryStream(d.Length);
        output.WriteByte(Marker);
        output.WriteByte(Soi);
        var pos = 2;
        var sawFrame = false;
        var sawScan = false;

        while (pos < d.Length)
        {
            if (d[pos] != Marker) return null;
            while (pos < d.Length && d[pos] == Marker) pos++; // fill bytes
            if (pos >= d.Length) return null;
            var marker = d[pos++];

            if (marker == Eoi)
            {
                if (!sawFrame || !sawScan) return null;
                output.WriteByte(Marker);
                output.WriteByte(Eoi);
                return output.ToArray();
            }
            if (marker == 0x00) return null;
            if (marker is Tem or (>= Rst0 and <= Rst7)) continue; // standalone markers outside entropy data carry nothing

            if (pos + 2 > d.Length) return null;
            var length = (d[pos] << 8) | d[pos + 1];
            if (length < 2 || pos + length > d.Length) return null;
            var payload = d.Slice(pos + 2, length - 2);
            if (IsFrameMarker(marker)) sawFrame = true;

            if (marker == Sos)
            {
                if (!sawFrame) return null;
                sawScan = true;
                WriteSegment(output, marker, d.Slice(pos, length));
                pos += length;
                // Entropy-coded data runs until the next real marker (0xFF followed by neither 0x00 stuffing nor a restart marker).
                var start = pos;
                while (pos + 1 < d.Length && !(d[pos] == Marker && d[pos + 1] != 0x00 && d[pos + 1] is not (>= Rst0 and <= Rst7)))
                    pos++;
                if (pos + 1 >= d.Length) return null;
                output.Write(d[start..pos]);
                continue;
            }

            if (KeepsSegment(marker, payload)) WriteSegment(output, marker, d.Slice(pos, length));
            pos += length;
        }
        return null; // ran out of bytes before the end-of-image marker
    }

    private static void WriteSegment(MemoryStream output, byte marker, ReadOnlySpan<byte> lengthAndPayload)
    {
        output.WriteByte(Marker);
        output.WriteByte(marker);
        output.Write(lengthAndPayload);
    }

    private static bool IsFrameMarker(byte m) => m is >= 0xC0 and <= 0xCF && m is not (0xC4 or 0xC8 or 0xCC);

    /// <summary>
    /// Keeps what decoding needs: the JFIF header, an ICC colour profile, the Adobe colour transform, and every segment that is neither
    /// an application segment nor a comment. APP1 (EXIF, XMP), the other APPn (IPTC, vendor data) and COM are dropped.
    /// </summary>
    private static bool KeepsSegment(byte marker, ReadOnlySpan<byte> payload)
    {
        if (marker is App0 or App14) return true;
        if (marker == App2) return payload.StartsWith(IccSignature);
        return marker is not (> App0 and <= 0xEF) and not Com;
    }

    // ---------------------------------------------------------------- PNG

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int PngChunkOverhead = 12; // length(4) + type(4) + crc(4)

    /// <summary>Chunks a decoder needs or that describe colour and animation. Anything else (text, EXIF, time, private chunks) is dropped.</summary>
    private static readonly HashSet<string> KeptPngChunks =
    [
        "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "bKGD", "hIST", "pHYs", "sPLT",
        "acTL", "fcTL", "fdAT", "cICP", "mDCV", "cLLI"
    ];

    private static byte[]? StripPng(ReadOnlySpan<byte> d)
    {
        if (!d.StartsWith(PngSignature)) return null;
        using var output = new MemoryStream(d.Length);
        output.Write(PngSignature);
        var pos = PngSignature.Length;
        var expectHeader = true;
        var sawData = false;

        while (pos + PngChunkOverhead <= d.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(pos, 4));
            if (length > int.MaxValue || pos + PngChunkOverhead + (long)length > d.Length) return null;
            var type = Encoding.ASCII.GetString(d.Slice(pos + 4, 4));
            var total = PngChunkOverhead + (int)length;

            if (expectHeader != (type == "IHDR")) return null; // IHDR comes first, and only once
            expectHeader = false;
            if (type == "IDAT") sawData = true;

            if (KeptPngChunks.Contains(type)) output.Write(d.Slice(pos, total));
            pos += total;
            if (type == "IEND") return sawData ? output.ToArray() : null; // bytes after IEND are not part of the image
        }
        return null;
    }

    // ---------------------------------------------------------------- WebP

    private const int RiffHeaderBytes = 12;
    private const int WebpChunkHeaderBytes = 8;
    private const byte ExifFlag = 0x08, XmpFlag = 0x04;
    private static readonly HashSet<string> KeptWebpChunks = ["VP8 ", "VP8L", "VP8X", "ALPH", "ANIM", "ANMF", "ICCP"];

    private static byte[]? StripWebp(ReadOnlySpan<byte> d)
    {
        if (d.Length < RiffHeaderBytes + WebpChunkHeaderBytes) return null;
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(4, 4));
        if (riffSize < 4 || riffSize + 8L > d.Length) return null; // truncated; bytes beyond the declared size are ignored
        var end = (int)riffSize + 8;

        using var body = new MemoryStream(end);
        var pos = RiffHeaderBytes;
        var sawImage = false;
        var droppedMetadata = false;
        var vp8xFlagsAt = -1;

        while (pos < end)
        {
            if (pos + WebpChunkHeaderBytes > end) return null;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(pos + 4, 4));
            var padded = (long)size + (size & 1);
            if (pos + WebpChunkHeaderBytes + padded > end) return null;
            var type = Encoding.ASCII.GetString(d.Slice(pos, 4));
            var chunk = d.Slice(pos, WebpChunkHeaderBytes + (int)padded);

            if (KeptWebpChunks.Contains(type))
            {
                if (type == "VP8X" && size >= 1) vp8xFlagsAt = (int)body.Length + WebpChunkHeaderBytes;
                if (type is "VP8 " or "VP8L" or "ANMF") sawImage = true;
                body.Write(chunk);
            }
            else if (type is "EXIF" or "XMP ")
                droppedMetadata = true;
            // any other unknown chunk is dropped as well

            pos += chunk.Length;
        }
        if (!sawImage) return null;

        var result = new byte[RiffHeaderBytes + body.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)body.Length + 4);
        "WEBP"u8.CopyTo(result.AsSpan(8));
        body.GetBuffer().AsSpan(0, (int)body.Length).CopyTo(result.AsSpan(RiffHeaderBytes));
        if (vp8xFlagsAt >= 0 && droppedMetadata) result[RiffHeaderBytes + vp8xFlagsAt] &= unchecked((byte)~(ExifFlag | XmpFlag));
        return result;
    }
}
