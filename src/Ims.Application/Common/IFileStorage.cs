namespace Ims.Application;

/// <summary>
/// Physical file storage for images and documents. Files are stored as files (not as database blobs); the database keeps only metadata.
/// The local-disk adapter lives in Infrastructure; a cloud adapter can replace it without touching any use case.
/// </summary>
public interface IFileStorage
{
    /// <summary>Streams <paramref name="content"/> to a new file with a generated name. Refuses (ValidationException) content larger than <paramref name="maxBytes"/>.</summary>
    Task<StoredFile> SaveAsync(Stream content, string extension, long maxBytes, CancellationToken ct);

    /// <exception cref="NotFoundException">The file does not exist.</exception>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);

    /// <summary>Removes the file if present. Never throws for a missing file.</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct);
}
