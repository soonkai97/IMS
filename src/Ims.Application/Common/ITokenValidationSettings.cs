namespace Ims.Application;

/// <summary>What the API needs to validate bearer tokens. Implemented by the same configuration that signs them, so the two cannot drift apart.</summary>
public interface ITokenValidationSettings
{
    string Issuer { get; }
    string Audience { get; }
    byte[] SigningKey { get; }
}
