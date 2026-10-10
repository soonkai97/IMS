using System.Net;
using System.Text;
using System.Text.Json;

namespace Ims.Api.Tests.Harness;

/// <summary>A fully read HTTP response with assertion helpers whose failure messages include the response body.</summary>
public sealed class ApiResponse
{
    private ApiResponse(HttpResponseMessage message, byte[] bytes)
    {
        Message = message;
        Bytes = bytes;
    }

    public HttpResponseMessage Message { get; }
    public byte[] Bytes { get; }
    public HttpStatusCode Status => Message.StatusCode;
    public int StatusCode => (int)Message.StatusCode;
    public string Body => Encoding.UTF8.GetString(Bytes);
    public string? ContentType => Message.Content.Headers.ContentType?.MediaType;
    public string? ContentDisposition => Message.Content.Headers.ContentDisposition?.ToString();
    public string? CorrelationHeader => Message.Headers.TryGetValues("X-Correlation-Id", out var v) ? v.FirstOrDefault() : null;
    public string? Header(string name) =>
        Message.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : Message.Content.Headers.TryGetValues(name, out var c) ? c.FirstOrDefault() : null;

    public bool IsSuccess => Message.IsSuccessStatusCode;

    public static async Task<ApiResponse> FromAsync(HttpResponseMessage message) =>
        new(message, await message.Content.ReadAsByteArrayAsync());

    public ApiResponse ShouldBe(HttpStatusCode expected)
    {
        if (Status != expected) throw new Xunit.Sdk.XunitException($"Expected HTTP {(int)expected} {expected} but got {StatusCode} {Status}. Body: {Truncated()}");
        return this;
    }

    public ApiResponse ShouldBeOk() => ShouldBe(HttpStatusCode.OK);

    /// <summary>Asserts the status and that the body is an IMS problem body with the standard parts; returns it for more checks.</summary>
    public ProblemBody ShouldBeProblem(HttpStatusCode expected, string? title = null)
    {
        ShouldBe(expected);
        var problem = Problem;
        Assert.True(problem.Status == (int)expected, $"Problem.status was {problem.Status}. Body: {Truncated()}");
        if (title is not null) Assert.Equal(title, problem.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.CorrelationId), $"Problem body has no correlationId. Body: {Truncated()}");
        return problem;
    }

    /// <summary>Asserts 200 and reads the body as <typeparamref name="T"/>.</summary>
    public T Data<T>()
    {
        ShouldBeOk();
        return Read<T>();
    }

    public T Read<T>()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(Bytes, JsonDefaults.Options)
                   ?? throw new Xunit.Sdk.XunitException($"Body was empty or null, expected {typeof(T).Name}.");
        }
        catch (JsonException e)
        {
            throw new Xunit.Sdk.XunitException($"Body is not a valid {typeof(T).Name}: {e.Message}. Body: {Truncated()}");
        }
    }

    public ProblemBody Problem
    {
        get
        {
            Assert.True(Bytes.Length > 0, $"Expected a problem body but the response body was empty (HTTP {StatusCode}).");
            return Read<ProblemBody>();
        }
    }

    public JsonDocument Json() => JsonDocument.Parse(Bytes);

    private string Truncated() => Body.Length <= 800 ? Body : Body[..800] + "...";
}
