namespace Ims.Application.Features.Import;

/// <summary>What an import row does (or would do on commit): create something new, update something that exists, or nothing because it has errors.</summary>
public enum ImportRowStatus
{
    New,
    Update,
    Error
}
