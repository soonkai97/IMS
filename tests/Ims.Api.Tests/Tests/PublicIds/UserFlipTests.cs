using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Users;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// GUID plan PR-5: users are addressed by their public id wherever the API names them (the user routes, the session, who created, submitted or returned a
/// document). The integer id stays the identity inside: the access token, the ownership rules and the self-approval rule never see a public id.
/// </summary>
[Collection(ApiCollection.Name)]
public class UserFlipTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private async Task<Guid> StoredPublicIdAsync(string username) =>
        await fixture.Database.ScalarAsync<Guid>("SELECT PublicId FROM [User] WHERE Username = @v", ("@v", username));

    [Fact]
    public async Task A_created_and_listed_user_and_every_session_answer_with_the_public_id_stored_for_the_user()
    {
        var accounts = new AccountFactory(fixture);
        var admin = await fixture.AsAsync(Roles.Admin);

        var account = await accounts.CreateAsync(Roles.Clerk);
        var (client, login) = await accounts.SignInAsync(account);
        var stored = await StoredPublicIdAsync(account.Username);

        Assert.Equal(stored, account.Id);
        var listed = Assert.Single((await admin.GetAsync($"/api/users?search={account.Username}")).Data<PagedResult<UserDto>>().Items);
        Assert.Equal(stored, listed.Id);
        Assert.Equal(stored, login.User.Id);
        Assert.Equal(stored, (await client.Auth.MeAsync()).Data<UserInfo>().Id);
        var refreshed = (await client.Auth.RefreshAsync(login.RefreshToken)).Data<AuthResult>();
        Assert.Equal(stored, refreshed.User.Id);
        Assert.Equal(JsonValueKind.String, (await client.Auth.MeAsync()).Json().RootElement.GetProperty("id").ValueKind); // a string on the wire, never a number
    }

    [Fact]
    public async Task The_access_token_keeps_the_integer_id_and_never_carries_the_public_id()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk);
        var (_, login) = await accounts.SignInAsync(account);
        var internalId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = @v", ("@v", account.Username));

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken).Claims.ToList();

        Assert.Contains(claims, c => c.Value == internalId.ToString());
        Assert.DoesNotContain(claims, c => Guid.TryParse(c.Value, out _) && c.Type != "jti");
        Assert.DoesNotContain(claims, c => c.Value.Contains(account.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_user_route_takes_the_public_id_only_and_an_unknown_or_integer_id_is_404()
    {
        var accounts = new AccountFactory(fixture);
        var admin = await fixture.AsAsync(Roles.Admin);
        var account = await accounts.CreateAsync(Roles.Clerk);
        var internalId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = @v", ("@v", account.Username));
        var body = new UserSaveRequest(account.Username, account.Email, "Renamed", Roles.Clerk, true, null, account.WarehouseIds.ToList());

        (await admin.Users.UpdateAsync(Guid.NewGuid(), body)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await admin.PutAsync($"/api/users/{internalId}", body)).ShouldBe(HttpStatusCode.NotFound); // the integer route is gone, and the row it named is untouched
        var updated = (await admin.Users.UpdateAsync(account.Id, body)).Data<UserDto>();

        Assert.Equal((account.Id, "Renamed"), (updated.Id, updated.DisplayName));
    }

    [Fact]
    public async Task Naming_another_users_public_id_gives_no_rights_over_their_documents()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var accounts = new AccountFactory(fixture);
        var alice = await accounts.CreateAsync(Roles.Clerk);
        var (aliceClient, _) = await accounts.SignInAsync(alice);
        var bob = await accounts.CreateAsync(Roles.Clerk);
        var (bobClient, _) = await accounts.SignInAsync(bob);
        var product = await s.NewProductAsync();
        var aliceDraft = await s.DraftReceiptAsync(aliceClient, Warehouse, new Line(product.Id, 1, 1m));

        // Bob can read Alice's public id from the document, and it is not his own: the ownership rule compares the integer identity of the token.
        var seenByBob = (await bobClient.Documents.GetAsync(aliceDraft.Id)).Data<DocumentDto>();
        Assert.Equal(alice.Id, seenByBob.CreatedById);
        Assert.NotEqual(bob.Id, seenByBob.CreatedById);
        Assert.DoesNotContain(DocumentActions.Edit, seenByBob.AllowedActions);
        Assert.DoesNotContain(DocumentActions.Submit, seenByBob.AllowedActions);
        var edit = new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.SupplierId).WithLines(new Line(product.Id, 7, 1m)).Build();
        (await bobClient.Documents.UpdateAsync(aliceDraft.Id, edit)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await bobClient.Documents.SubmitAsync(aliceDraft.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await bobClient.Documents.CancelAsync(aliceDraft.Id, "Not mine")).ShouldBeProblem(HttpStatusCode.Forbidden);

        var mine = (await bobClient.Documents.ListAsync(mine: true)).Data<PagedResult<DocumentListItem>>();
        Assert.DoesNotContain(mine.Items, i => i.Id == aliceDraft.Id);
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(aliceDraft.Id)).Status);
    }

    [Fact]
    public async Task The_document_projections_carry_the_integer_keys_of_the_creator_and_the_submitter_in_the_right_places()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var accounts = new AccountFactory(fixture);
        var creator = await accounts.CreateAsync(Roles.Clerk);
        var (creatorClient, _) = await accounts.SignInAsync(creator);
        var submitter = await accounts.CreateAsync(Roles.Supervisor);
        var (submitterClient, _) = await accounts.SignInAsync(submitter);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(creatorClient, Warehouse, new Line(product.Id, 1, 1m));
        var submitted = await s.SubmitAsync(submitterClient, draft);
        var creatorKey = await fixture.Database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = @v", ("@v", creator.Username));
        var submitterKey = await fixture.Database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = @v", ("@v", submitter.Username));
        Assert.NotEqual(creatorKey, submitterKey);

        // The keys are not part of the JSON, so they are read from the projections themselves (the specifications the handlers run).
        using var scope = fixture.Host.Services.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IReadRepository<StockDocument>>();
        var dto = (await documents.FirstOrDefaultAsync(new DocumentDtoSpec(submitted.Id, null)))!;
        var listFilter = new DocumentFilter(null, null, null, null, null, null);
        var item = (await documents.ListAsync(new DocumentsPageSpec(listFilter, null, null, 0, null, null))).Single(i => i.Id == submitted.Id);

        Assert.Equal((creatorKey, (int?)submitterKey, creator.Id, (Guid?)submitter.Id), (dto.CreatedByKey, dto.SubmittedByKey, dto.CreatedById, dto.SubmittedById));
        Assert.Equal((creatorKey, (int?)submitterKey, creator.Id, (Guid?)submitter.Id), (item.CreatedByKey, item.SubmittedByKey, item.CreatedById, item.SubmittedById));
        Assert.Equal(await fixture.Database.ScalarAsync<int>("SELECT Id FROM StockDocument WHERE PublicId = @p", ("@p", submitted.Id)), item.Key); // the cursor position, in its place after the two user keys
    }

    [Fact]
    public async Task The_audit_log_names_users_by_public_id_for_their_events_and_rows_and_a_row_written_before_the_flip_keeps_its_integer()
    {
        var accounts = new AccountFactory(fixture);
        var admin = await fixture.AsAsync(Roles.Admin);
        var legacyId = Random.Shared.Next(100_000_000, 999_999_999).ToString();
        await fixture.Database.ExecuteAsync(
            "INSERT INTO AuditLogs (Actor, Action, EntityType, EntityId, [At]) VALUES (N'legacy', N'user.updated', N'User', @id, SYSUTCDATETIME())", ("@id", legacyId));

        var account = await accounts.CreateAsync(Roles.Clerk);
        var (client, login) = await accounts.SignInAsync(account);
        await client.Auth.LogoutAsync(login.RefreshToken);
        var promote = new UserSaveRequest(account.Username, account.Email, account.Username, Roles.Supervisor, true, null, account.WarehouseIds.ToList());
        (await admin.Users.UpdateAsync(account.Id, promote)).ShouldBeOk();

        var own = (await admin.Audit.QueryAsync(entityType: nameof(User), entityId: account.Id.ToString())).Data<AuditPage>().Items;
        Assert.All(own, e => Assert.Equal(account.Id.ToString(), e.EntityId));
        foreach (var action in new[] { AuditActions.UserCreated, AuditActions.LoginSuccess, AuditActions.Logout, AuditActions.UserUpdated, AuditActions.RowCreated, AuditActions.RowUpdated })
            Assert.Contains(own, e => e.Action == action);
        var old = Assert.Single((await admin.Audit.QueryAsync(entityType: nameof(User), entityId: legacyId)).Data<AuditPage>().Items);
        Assert.Equal(legacyId, old.EntityId); // the history mixes both ids; the page and its entity-id filter are plain text and show and match both
    }
}
