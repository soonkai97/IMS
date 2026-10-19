namespace Ims.Application.Features.Modules;

/// <summary>
/// Turn a module on or off. <c>Enabled</c> has no default on purpose: a body that leaves it out is a 400, never a quiet switch off. The reason is kept in the audit log.
/// </summary>
public record ModuleSwitchRequest(bool? Enabled, string Reason);
