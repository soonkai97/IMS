using System.Text;

namespace Ims.Domain.Tests.Attachments;

/// <summary>Smallest structurally valid JPEG, PNG and WebP files, with switches for the metadata the stripper must remove.</summary>
internal static class TestImages
{
    public const string GpsMarker = "GPS-LAT-1.3521N";
    public const string CommentMarker = "secret-comment";
    public const string TrailingMarker = "<script>alert(1)</script>";

    // ---------- JPEG ----------

    public static byte[] Jpeg(bool exif = false, bool comment = false, bool trailing = false, bool icc = false, bool adobe = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(JpegSegment(0xE0, "JFIF\0"u8.ToArray().Concat(new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 }).ToArray()));
        if (exif) bytes.AddRange(JpegSegment(0xE1, Encoding.ASCII.GetBytes("Exif\0\0" + GpsMarker)));
        if (exif) bytes.AddRange(JpegSegment(0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta/>")));
        if (exif) bytes.AddRange(JpegSegment(0xED, Encoding.ASCII.GetBytes("Photoshop 3.0\0IPTC")));
        if (icc) bytes.AddRange(JpegSegment(0xE2, Encoding.ASCII.GetBytes("ICC_PROFILE\0\x01\x01profile-bytes")));
        if (adobe) bytes.AddRange(JpegSegment(0xEE, Encoding.ASCII.GetBytes("Adobe\0\x64\x80\x00\x00\x00\x00\x00")));
        if (comment) bytes.AddRange(JpegSegment(0xFE, Encoding.ASCII.GetBytes(CommentMarker)));
        bytes.AddRange(JpegSegment(0xDB, new byte[65]));                                       // quantisation table
        bytes.AddRange(JpegSegment(0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]));                      // frame: 1x1, one component
        bytes.AddRange(JpegSegment(0xC4, new byte[17]));                                       // huffman table
        bytes.AddRange(JpegSegment(0xDA, [1, 1, 0, 0, 0x3F, 0]));                              // scan header
        bytes.AddRange(new byte[] { 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56 });               // entropy data with stuffing and a restart marker
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        if (trailing) bytes.AddRange(Encoding.ASCII.GetBytes(TrailingMarker));
        return [.. bytes];
    }

    private static byte[] JpegSegment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    // ---------- PNG ----------

    public static byte[] Png(bool text = false, bool exif = false, bool time = false, bool trailing = false)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange(PngChunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]));
        bytes.AddRange(PngChunk("pHYs", [0, 0, 0x0B, 0x13, 0, 0, 0x0B, 0x13, 1]));
        if (text) bytes.AddRange(PngChunk("tEXt", Encoding.ASCII.GetBytes("Comment\0" + CommentMarker)));
        if (exif) bytes.AddRange(PngChunk("eXIf", Encoding.ASCII.GetBytes("MM\0*" + GpsMarker)));
        if (time) bytes.AddRange(PngChunk("tIME", [0x07, 0xEA, 1, 2, 3, 4, 5]));
        bytes.AddRange(PngChunk("IDAT", [0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01]));
        bytes.AddRange(PngChunk("IEND", []));
        if (trailing) bytes.AddRange(Encoding.ASCII.GetBytes(TrailingMarker));
        return [.. bytes];
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var crc = Crc32([.. typeBytes, .. data]);
        return [(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length, .. typeBytes, .. data,
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc];
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    // ---------- WebP ----------

    public static byte[] Webp(bool exif = false, bool xmp = false, bool trailing = false)
    {
        var body = new List<byte>();
        var flags = (byte)((exif ? 0x08 : 0) | (xmp ? 0x04 : 0));
        body.AddRange(WebpChunk("VP8X", [flags, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        body.AddRange(WebpChunk("VP8 ", [1, 2, 3, 4, 5])); // odd length: padded
        if (exif) body.AddRange(WebpChunk("EXIF", Encoding.ASCII.GetBytes(GpsMarker)));
        if (xmp) body.AddRange(WebpChunk("XMP ", Encoding.ASCII.GetBytes("<x:xmpmeta/>")));

        var file = new List<byte>();
        file.AddRange("RIFF"u8.ToArray());
        file.AddRange(BitConverter.GetBytes((uint)(body.Count + 4)));
        file.AddRange("WEBP"u8.ToArray());
        file.AddRange(body);
        if (trailing) file.AddRange(Encoding.ASCII.GetBytes(TrailingMarker));
        return [.. file];
    }

    private static byte[] WebpChunk(string type, byte[] data)
    {
        var padded = data.Length % 2 == 0 ? data : [.. data, (byte)0];
        return [.. Encoding.ASCII.GetBytes(type), .. BitConverter.GetBytes((uint)data.Length), .. padded];
    }

    public static bool Contains(byte[] haystack, string needle) => haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;
}
