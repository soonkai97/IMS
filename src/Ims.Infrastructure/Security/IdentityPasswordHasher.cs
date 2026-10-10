using Ims.Application;
using Microsoft.AspNetCore.Identity;
namespace Ims.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (salted, versioned format) behind the Application's own port.</summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _inner = new();
    private static readonly object Subject = new();

    public string Hash(string password) => _inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password) =>
        !string.IsNullOrEmpty(hash) && _inner.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
}
