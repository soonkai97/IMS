using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// BR-12 / UX error E4: a document hands out a concurrency token (also its ETag). Sent back as If-Match, a stale token is refused with 409
/// before anything is changed. The header is required on every document command (428 without it); "*" is the explicit "any version".
/// </summary>
[Collection(ApiCollection.Name)]
public class DocumentConcurrencyTokenTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private static string TokenOf(ApiResponse response)
    {
        using var json = response.Json();
        return json.RootElement.GetProperty("concurrencyToken").GetString()!;
    }

    private async Task<(StockScenario S, DocumentDto Draft, Guid ProductId)> DraftAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var productId = (await s.NewProductAsync()).Id;
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(productId, 1, 1m));
        return (s, draft, productId);
    }

    private static DocumentSaveRequest Edited(DocumentDto draft, Guid productId, decimal qty) =>
        new(draft.WarehouseId, draft.PartnerId, null, draft.Remark, [new DocumentLineInput(productId, qty, null, 1m, null)]);

    [Fact]
    public async Task A_document_carries_its_token_in_the_body_and_as_the_ETag()
    {
        var (s, draft, _) = await DraftAsync();

        var response = await s.Clerk.Documents.GetAsync(draft.Id);

        var token = TokenOf(response);
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal($"\"{token}\"", response.Header("ETag"));
    }

    [Fact]
    public async Task The_token_is_the_same_on_every_read_until_the_document_changes()
    {
        var (s, draft, _) = await DraftAsync();

        var first = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));
        var second = TokenOf(await s.Admin.Documents.GetAsync(draft.Id));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Every_response_that_returns_a_document_also_returns_its_ETag()
    {
        var (s, draft, productId) = await DraftAsync();

        var created = await s.Clerk.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).WithLines(new Line(productId, 1, 1m)).Build());
        var updated = await s.Clerk.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 2));
        var submitted = await s.Clerk.Documents.SubmitAsync(draft.Id);

        foreach (var response in new[] { created, updated, submitted })
            Assert.Equal($"\"{TokenOf(response)}\"", response.ShouldBeOk().Header("ETag"));
    }

    [Fact]
    public async Task Changing_only_the_lines_changes_the_token()
    {
        var (s, draft, productId) = await DraftAsync();
        var before = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));

        var response = await s.Clerk.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 5)); // same header, other quantity

        Assert.NotEqual(before, TokenOf(response.ShouldBeOk()));
        Assert.NotEqual(before, TokenOf(await s.Clerk.Documents.GetAsync(draft.Id)));
    }

    [Fact]
    public async Task A_status_change_changes_the_token()
    {
        var (s, draft, _) = await DraftAsync();
        var before = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));

        var submitted = await s.Clerk.Documents.SubmitAsync(draft.Id);

        Assert.NotEqual(before, TokenOf(submitted.ShouldBeOk()));
    }

    [Fact]
    public async Task The_list_item_has_the_same_token_as_the_full_document()
    {
        var (s, draft, _) = await DraftAsync();

        var item = Assert.Single((await s.Clerk.Documents.ListAsync(search: draft.No)).Data<PagedResult<DocumentListItem>>().Items);
        using var list = (await s.Clerk.Documents.ListAsync(search: draft.No)).Json();

        var listed = list.RootElement.GetProperty("items")[0].GetProperty("concurrencyToken").GetString();
        Assert.Equal(TokenOf(await s.Clerk.Documents.GetAsync(draft.Id)), listed);
        Assert.Equal(draft.Id, item.Id);
    }

    [Fact]
    public async Task A_save_with_the_current_token_succeeds()
    {
        var (s, draft, productId) = await DraftAsync();
        var token = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));

        var response = await s.Clerk.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 3), ifMatch: token);

        Assert.Equal(3m, response.Data<DocumentDto>().Lines.Single().Qty);
    }

    [Fact]
    public async Task A_save_with_a_stale_token_is_refused_with_409_and_changes_nothing()
    {
        var (s, draft, productId) = await DraftAsync();
        var stale = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));
        (await s.Admin.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 7))).ShouldBeOk(); // someone else edits first

        var response = await s.Clerk.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 9), ifMatch: stale);

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict, "Conflict");
        Assert.Contains("changed by someone else", problem.Detail);
        Assert.Contains(draft.No, problem.Detail);
        Assert.Equal(7m, (await s.GetAsync(draft.Id)).Lines.Single().Qty);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("cancel")]
    [InlineData("post")]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("reverse")]
    public async Task Every_action_refuses_a_stale_token(string action)
    {
        var (s, draft, productId) = await DraftAsync();
        var stale = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));
        (await s.Admin.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 7))).ShouldBeOk();

        var response = action switch
        {
            "submit" => await s.Clerk.Documents.SubmitAsync(draft.Id, stale),
            "cancel" => await s.Clerk.Documents.CancelAsync(draft.Id, "no longer needed", stale),
            "post" => await s.Admin.Documents.PostAsync(draft.Id, stale),
            "approve" => await s.Admin.Documents.ApproveAsync(draft.Id, stale),
            "reject" => await s.Admin.Documents.RejectAsync(draft.Id, "not right", stale),
            _ => await s.Admin.Documents.ReverseAsync(draft.Id, "entered twice", stale)
        };

        response.ShouldBeProblem(HttpStatusCode.Conflict, "Conflict");
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task An_action_with_the_current_token_goes_through_and_hands_out_the_next_token()
    {
        var (s, draft, _) = await DraftAsync();
        var token = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));

        var submitted = await s.Clerk.Documents.SubmitAsync(draft.Id, token);

        Assert.Equal(DocumentStatus.Submitted, submitted.Data<DocumentDto>().Status);
        // Sending the old token again now fails: the document moved on.
        (await s.Clerk.Documents.CancelAsync(draft.Id, "oops", token)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task A_wildcard_If_Match_means_any_version_the_explicit_opt_out()
    {
        var (s, draft, productId) = await DraftAsync();
        (await s.Admin.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 7))).ShouldBeOk();

        (await s.Clerk.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 8), ifMatch: "*")).ShouldBeOk();
    }

    [Fact]
    public async Task The_token_of_another_document_does_not_match()
    {
        var (s, draft, productId) = await DraftAsync();
        var other = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(productId, 1, 1m));
        var otherToken = TokenOf(await s.Clerk.Documents.GetAsync(other.Id));

        (await s.Clerk.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 4), ifMatch: otherToken)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_garbage_token_is_just_stale()
    {
        var (s, draft, productId) = await DraftAsync();

        (await s.Clerk.Documents.UpdateAsync(draft.Id, Edited(draft, productId, 4), ifMatch: "not-a-token")).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_ETag_header_is_exposed_to_browsers_through_CORS()
    {
        var (s, draft, _) = await DraftAsync();

        var response = await s.Clerk.SendAsync(HttpMethod.Get, $"/api/documents/{draft.Id}", null, r => r.Headers.Add("Origin", "http://localhost:4200"));

        Assert.Contains("ETag", response.Header("Access-Control-Expose-Headers") ?? "");
    }
}
