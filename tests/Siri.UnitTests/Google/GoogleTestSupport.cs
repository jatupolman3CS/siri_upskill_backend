using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.UnitTests.Google;

/// <summary>One request as the stub handler saw it (body read eagerly, because the request is disposed after sending).</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? ContentType, string? Body)
{
    public JsonObject JsonBody => (JsonObject)JsonNode.Parse(Body ?? throw new InvalidOperationException("Request had no body."))!;

    public IReadOnlyDictionary<string, string> Form =>
        (Body ?? string.Empty)
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Unescape(parts[0]), parts => Unescape(parts.Length > 1 ? parts[1] : string.Empty));

    private static string Unescape(string value) => System.Uri.UnescapeDataString(value.Replace('+', ' '));
}

/// <summary>
/// Scripted <see cref="HttpMessageHandler"/>: answers requests from a queue (last response repeats) and records everything
/// it was sent, so a test can assert the exact HTTP request shape against Google's documented REST API.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _last;

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpHandler Enqueue(HttpStatusCode status, string? body = null, string contentType = "application/json")
    {
        _responses.Enqueue(_ => Response(status, body, contentType));
        return this;
    }

    public StubHttpHandler EnqueueThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    public static StubHttpHandler Always(HttpStatusCode status, string? body = null) => new StubHttpHandler().Enqueue(status, body);

    private static HttpResponseMessage Response(HttpStatusCode status, string? body, string contentType) =>
        new(status)
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
            request.Content?.Headers.ContentType?.MediaType,
            body));

        if (_responses.Count > 0)
        {
            _last = _responses.Dequeue();
        }

        return (_last ?? throw new InvalidOperationException("No response scripted for this request."))(request);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}

/// <summary>Captures every log line (formatted message + structured values + exception text) so tests can prove no secret was written.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public string AllText => string.Join('\n', _lines);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = new StringBuilder(formatter(state, exception));
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var (key, value) in values)
            {
                text.Append(' ').Append(key).Append('=').Append(value);
            }
        }

        if (exception is not null)
        {
            text.Append(' ').Append(exception);
        }

        _lines.Add(text.ToString());
    }
}
