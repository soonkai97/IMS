using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Returns;

/// <summary>
/// PL3.16: the lock of the document being returned (<c>ret:{id}</c>) made visible and deterministic, the twin of <c>SalesOrderLockTests</c>. The test holds the lock itself, from its own connection, starts the request,
/// and waits until SQL Server shows the request waiting for it; only then does it change what the request must see after the lock (the original is reversed) and let go. A handler that does not take the lock does
/// not wait, finishes at once with the old picture and fails the first assertion. No sleep decides the outcome. (Mutation-proven: with the lock call removed from the create handler, from the posting step or from
/// the reversal, the waiting test of that step fails on its first assertion.)
/// </summary>
[Collection(ApiCollection.Name)]
public class ReturnsLockTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan RequestEndsWithin = TimeSpan.FromSeconds(30);

    private StockScenario _s = null!;

    public async Task InitializeAsync()
    {
        await Sales.SalesScenario.ResetAsync(fixture);
        foreach (var module in new[] { "purchasing", "sales", "returns" })
            await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'true')", ("@k", $"Module.{module}"));
        _s = await StockScenario.CreateAsync(fixture);
    }

    public Task DisposeAsync() => Sales.SalesScenario.ResetAsync(fixture);

    private async Task<DocumentDto> SoldAsync()
    {
        var product = await _s.NewProductAsync();
        var warehouse = _s.Reference.DefaultWarehouseId;
        await _s.ReceiveAsync(warehouse, new Line(product.Id, 20m, 3m));
        return await _s.IssueAsync(warehouse, new Line(product.Id, 8m));
    }

    private async Task<bool> SomeoneWaitsForTheLockAsync(Task request)
    {
        for (var i = 0; i < 100 && !request.IsCompleted; i++)
        {
            if (await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()") > 0) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private async Task<ApiResponse> RunWhileLockedAsync(Guid originalId, Func<Task<ApiResponse>> request, Func<HeldOrderLock, Task> whileItWaits, string notWaitingMessage)
    {
        var held = await HeldOrderLock.TakeReturnOriginAsync(fixture, originalId);
        Task<ApiResponse>? running = null;
        try
        {
            running = Task.Run(request);
            Assert.True(await SomeoneWaitsForTheLockAsync(running), notWaitingMessage);
            await whileItWaits(held);
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
            if (running is not null)
            {
                try { await running.WaitAsync(RequestEndsWithin); } catch (Exception) { /* judged below, or does not matter when the wait assertion failed */ }
            }
        }
        return await running;
    }

    private Task ReverseBehindTheirBackAsync(HeldOrderLock held, Guid documentId) =>
        held.ExecuteAsync("UPDATE StockDocument SET Status = @s WHERE PublicId = @id", ("@s", (int)DocumentStatus.Reversed), ("@id", documentId));

    [Fact]
    public async Task Creating_a_return_waits_for_the_originals_lock_and_then_sees_an_original_that_was_reversed_while_it_waited()
    {
        var sale = await SoldAsync();

        var response = await RunWhileLockedAsync(sale.Id, () => _s.Supervisor.Returns.CreateAsync(sale.Id), held => ReverseBehindTheirBackAsync(held, sale.Id),
            "the request did not wait for the original's lock (it must take ret:{id} before it reads the document)");

        Assert.Equal(Messages.ReturnOriginalNotPosted(sale.No, DocumentStatus.Reversed), response.ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE ReturnOfDocumentId = (SELECT Id FROM StockDocument WHERE PublicId = @id)", ("@id", sale.Id)));
    }

    [Fact]
    public async Task Posting_a_return_waits_for_the_originals_lock_and_then_sees_an_original_that_was_reversed_while_it_waited()
    {
        var sale = await SoldAsync();
        var draft = (await _s.Supervisor.Returns.CreateAsync(sale.Id)).Data<DocumentDto>();
        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        var approved = (await _s.Admin.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();

        var response = await RunWhileLockedAsync(sale.Id, () => _s.Admin.Documents.PostAsync(approved.Id), held => ReverseBehindTheirBackAsync(held, sale.Id),
            "the post did not wait for the original's lock (it must take ret:{id} before the stock locks)");

        Assert.Equal(Messages.ReturnOriginalNotPosted(sale.No, DocumentStatus.Reversed), response.ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Empty(await _s.LedgerAsync(sourceDocId: approved.Id));
    }

    [Fact]
    public async Task Reversing_a_posted_return_waits_for_the_originals_lock()
    {
        var sale = await SoldAsync();
        var draft = (await _s.Supervisor.Returns.CreateAsync(sale.Id)).Data<DocumentDto>();
        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        var approved = (await _s.Admin.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();
        var posted = (await _s.Admin.Documents.PostAsync(approved.Id)).Data<DocumentDto>();

        var response = await RunWhileLockedAsync(sale.Id, () => _s.Admin.Documents.ReverseAsync(posted.Id, "returned by mistake"), _ => Task.CompletedTask,
            "the reversal did not wait for the original's lock (a reversal of a return takes ret:{id} first, where posting takes it)");

        Assert.Equal(DocumentStatus.Reversed, response.Data<DocumentDto>().Status);
    }
}
