using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Ims.Api.Tests.Support;

/// <summary>
/// A Serilog sink that keeps everything the host logs (rendered message, every property, exception text) so a test can look for something that must never be there. The host adds
/// the sinks registered in the container to its logger (<c>ReadFrom.Services</c>), so a test host registers one with <c>services.AddSingleton&lt;ILogEventSink&gt;</c>.
/// </summary>
public sealed class CapturedLogs : ILogEventSink
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => [.. _lines];

    public void Emit(LogEvent logEvent) =>
        _lines.Enqueue($"{logEvent.Level} {logEvent.RenderMessage()} {string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}"))} {logEvent.Exception}");
}
