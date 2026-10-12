using System.Security.Cryptography;
using Ims.Application;
using Microsoft.AspNetCore.DataProtection;
namespace Ims.Api;

/// <summary>
/// ASP.NET Core Data Protection (authenticated encryption) with one purpose for all cursors and one sub-purpose per list. The key ring is the host's: a deployment
/// with several instances must persist and share it, and a lost key only makes outstanding cursors fail with a 400.
/// </summary>
public sealed class DataProtectionCursorProtector(IDataProtectionProvider provider) : ICursorProtector
{
    public const string Purpose = "Ims.Cursor.v1";

    public byte[] Protect(string list, byte[] payload) => For(list).Protect(payload);

    public bool TryUnprotect(string list, byte[] data, out byte[] payload)
    {
        try
        {
            payload = For(list).Unprotect(data);
            return true;
        }
        catch (CryptographicException)
        {
            payload = [];
            return false;
        }
    }

    private IDataProtector For(string list) => provider.CreateProtector(Purpose, list);
}
