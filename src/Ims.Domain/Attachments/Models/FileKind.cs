namespace Ims.Domain;

/// <summary>File kinds the system accepts, identified from the file's own bytes (never from the client's name or content type).</summary>
public enum FileKind { Jpeg = 1, Png = 2, Webp = 3, Pdf = 4 }
