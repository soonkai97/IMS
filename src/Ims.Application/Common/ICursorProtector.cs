namespace Ims.Application;

/// <summary>
/// Encrypts and authenticates the bytes of a list cursor, so a cursor reveals nothing about the row it points at and cannot be made up by a client.
/// The <c>list</c> is part of the key derivation: a cursor of one list does not open on another.
/// </summary>
public interface ICursorProtector
{
    byte[] Protect(string list, byte[] payload);

    /// <returns>False for anything <see cref="Protect"/> did not make for this list: altered, truncated, another purpose or list, or a key that no longer exists.</returns>
    bool TryUnprotect(string list, byte[] data, out byte[] payload);
}
