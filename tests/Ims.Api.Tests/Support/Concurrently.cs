namespace Ims.Api.Tests.Support;

/// <summary>Runs work so that every job starts at the same instant, to provoke real races rather than a polite queue.</summary>
public static class Concurrently
{
    public static async Task<IReadOnlyList<T>> RunAsync<T>(IEnumerable<Func<Task<T>>> work)
    {
        var start = new TaskCompletionSource();
        var tasks = work.Select(async job =>
        {
            await start.Task;
            return await job();
        }).ToList();

        // Let every job reach the gate before opening it (jobs are queued on the thread pool).
        await Task.Delay(50);
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
