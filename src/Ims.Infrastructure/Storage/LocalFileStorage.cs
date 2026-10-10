using System.Security.Cryptography;
using Ims.Application;
using Ims.Domain;
using Microsoft.Extensions.Options;

namespace Ims.Infrastructure.Storage;

/// <summary>
/// Stores files on local disk as <c>{root}/{yyyy}/{MM}/{guid}{ext}</c>. Names are generated here, so a client can never choose a path.
/// Writes go to a temporary file first and are moved into place, so a half-written upload is never visible. Every path is resolved and
/// checked to stay inside the root (no path traversal).
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, IClock clock) : IFileStorage
{
    private const int BufferSize = 81920;
    private const string TempSuffix = ".tmp";
    private readonly string _root = Path.GetFullPath(options.Value.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public async Task<StoredFile> SaveAsync(Stream content, string extension, long maxBytes, CancellationToken ct)
    {
        if (extension.Length is < 2 or > 6 || extension[0] != '.' || !extension.Skip(1).All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("Invalid extension.", nameof(extension));

        var now = clock.UtcNow;
        var relative = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + TempSuffix;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes) throw new ValidationException(Messages.FileTooLarge((int)maxBytes));
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await file.FlushAsync(ct);
            }
            File.Move(temp, full);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        return new StoredFile(relative, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var full = Resolve(storagePath);
        if (!File.Exists(full)) throw new NotFoundException(Messages.AttachmentNotFound);
        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        TryDelete(Resolve(storagePath));
        return Task.CompletedTask;
    }

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes the storage root.");
        return full;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort: a later cleanup removes leftovers */ }
        catch (UnauthorizedAccessException) { }
    }
}
