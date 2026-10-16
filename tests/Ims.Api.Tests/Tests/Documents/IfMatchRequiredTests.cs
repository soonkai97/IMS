using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// EA-19 / PS-25 / PS-08: every document command must carry the document's version in If-Match (428 without it), the quotes are optional, and a
/// stale version answers 409 with who changed the document and when, for the UX E4 sentence.
/// </summary>
[Collection(ApiCollection.Name)]
public class IfMatchRequiredTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private async Task<(StockScenario S, DocumentDto Draft, Guid ProductId)> DraftAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var productId = (await s.NewProductAsync()).Id;
        return (s, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(productId, 1, 1m)), productId);
    }

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("POST", "/submit")]
    [InlineData("POST", "/recall")]
    [InlineData("POST", "/approve")]
    [InlineData("POST", "/reject")]
    [InlineData("POST", "/cancel")]
    [InlineData("POST", "/post")]
    [InlineData("POST", "/reverse")]
    public async Task A_document_command_without_If_Match_is_428_and_changes_nothing(string method, string action)
    {
        var (s, draft, productId) = await DraftAsync();
        var body = action switch
        {
            "" => new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, null, draft.Remark, [new DocumentLineInput(productId, 9, null, 1m, null)]),
            "/reject" => (object)new RejectRequest("Please check this"),
            "/cancel" => new CancelRequest("Not needed"),
            "/reverse" => new ReverseRequest("Entered twice"),
            _ => null!
        };

        var response = await s.Admin.Documents.WithoutIfMatchAsync(new HttpMethod(method), $"/api/documents/{draft.Id}{action}", body);

        var problem = response.ShouldBeProblem((HttpStatusCode)428, "Precondition required");
        Assert.Contains("If-Match", problem.Detail);
        Assert.Equal("if_match_required", response.Json().RootElement.GetProperty("code").GetString());
        var after = await s.GetAsync(draft.Id);
        Assert.Equal((DocumentStatus.Draft, draft.ConcurrencyToken), (after.Status, after.ConcurrencyToken));
    }

    /// <summary>A header that names no version (empty quotes, only commas, only blanks) is a missing header, not the "*" opt-out: it must not skip the check.</summary>
    [Theory]
    [InlineData("\"\"")]
    [InlineData("W/\"\"")]
    [InlineData(",")]
    [InlineData(" , ,")]
    [InlineData(",\"\"")]
    [InlineData("\" \"")]
    [InlineData(" ")]
    public async Task An_If_Match_that_names_no_version_is_428_and_changes_nothing(string raw)
    {
        var (s, draft, _) = await DraftAsync();

        var response = await s.Clerk.Documents.WithRawIfMatchAsync(HttpMethod.Post, $"/api/documents/{draft.Id}/submit", raw);

        response.ShouldBeProblem((HttpStatusCode)428, "Precondition required");
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task The_star_is_still_the_explicit_any_version()
    {
        var (s, draft, _) = await DraftAsync();

        var submitted = await s.Clerk.Documents.WithRawIfMatchAsync(HttpMethod.Post, $"/api/documents/{draft.Id}/submit", "*");

        Assert.Equal(DocumentStatus.Submitted, submitted.Data<DocumentDto>().Status);
    }

    [Fact]
    public async Task Creating_a_document_and_reading_need_no_If_Match()
    {
        var (s, draft, _) = await DraftAsync();

        (await s.Clerk.Documents.GetAsync(draft.Id)).ShouldBeOk();
        (await s.Clerk.Documents.ListAsync()).ShouldBeOk();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_current_token_works_with_or_without_quotes_and_as_a_weak_validator(bool quoted)
    {
        var (s, draft, _) = await DraftAsync();
        var token = draft.ConcurrencyToken;

        var submitted = await s.Clerk.Documents.WithRawIfMatchAsync(HttpMethod.Post, $"/api/documents/{draft.Id}/submit", quoted ? $"W/\"{token}\"" : token);

        Assert.Equal(DocumentStatus.Submitted, submitted.Data<DocumentDto>().Status);
    }

    [Fact]
    public async Task A_stale_token_without_quotes_is_refused_not_silently_ignored()
    {
        var (s, draft, productId) = await DraftAsync();
        var stale = draft.ConcurrencyToken;
        (await s.Admin.Documents.UpdateAsync(draft.Id, new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, null, draft.Remark,
            [new DocumentLineInput(productId, 5, null, 1m, null)]))).ShouldBeOk();

        var response = await s.Clerk.Documents.WithRawIfMatchAsync(HttpMethod.Post, $"/api/documents/{draft.Id}/submit", stale);

        response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task A_stale_document_says_who_changed_it_and_when()
    {
        var (s, draft, productId) = await DraftAsync();
        var before = DateTime.UtcNow.AddMinutes(-1);
        (await s.Admin.Documents.UpdateAsync(draft.Id, new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, null, draft.Remark,
            [new DocumentLineInput(productId, 5, null, 1m, null)]))).ShouldBeOk();

        var response = await s.Clerk.Documents.SubmitAsync(draft.Id, draft.ConcurrencyToken);

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains("changed by someone else", problem.Detail);
        var body = response.Json().RootElement;
        Assert.Equal("document_changed", body.GetProperty("code").GetString());
        var adminName = (await s.Admin.Auth.MeAsync()).Data<Ims.Application.Features.Auth.UserInfo>().DisplayName;
        Assert.Equal(adminName, body.GetProperty("changedBy").GetString());
        Assert.True(body.GetProperty("changedAt").GetDateTime() >= before);
        Assert.EndsWith("Z", body.GetProperty("changedAt").GetString());
    }
}
