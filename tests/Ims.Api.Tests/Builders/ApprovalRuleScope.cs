using Ims.Api.Tests.Clients;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Builders;

/// <summary>Adds an approval rule for the duration of a <c>await using</c> block and deactivates it afterwards (rules are never deleted).</summary>
public sealed class ApprovalRuleScope : IAsyncDisposable
{
    private readonly ApiClient _admin;
    private readonly ApprovalRuleDto _rule;

    private ApprovalRuleScope(ApiClient admin, ApprovalRuleDto rule)
    {
        _admin = admin;
        _rule = rule;
    }

    public Guid RuleId => _rule.Id;

    public static async Task<ApprovalRuleScope> AddAsync(ApiClient admin, DocumentType type, decimal? amountThreshold, decimal? qtyThreshold, string role)
    {
        var request = new ApprovalRuleSaveRequest(type, amountThreshold, qtyThreshold, IsActive: true, ApproverTier: TierOf(role));
        var rule = (await admin.Settings.CreateRuleAsync(request)).Data<ApprovalRuleDto>();
        return new ApprovalRuleScope(admin, rule);
    }

    /// <summary>The tier the named system role holds by default: tests say "a rule above a Supervisor" or "above an Owner" by role, the API takes the tier.</summary>
    private static ApproverTier TierOf(string role) => role is Roles.Owner or Roles.Admin ? ApproverTier.Large : ApproverTier.Standard;

    public async ValueTask DisposeAsync()
    {
        var off = new ApprovalRuleSaveRequest(_rule.DocType, _rule.AmountThreshold, _rule.QtyThreshold, IsActive: false, ApproverTier: _rule.ApproverTier);
        (await _admin.Settings.UpdateRuleAsync(_rule.Id, off)).ShouldBeOk();
    }
}
