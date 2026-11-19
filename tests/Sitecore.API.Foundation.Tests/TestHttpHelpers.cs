using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sitecore.API.Foundation.Tests;

internal static class TestHttpResponseHelper
{
    public static HttpResponseMessage CreateTokenResponse(string accessToken = "test-token", int expiresIn = 3600)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { access_token = accessToken, expires_in = expiresIn }),
                Encoding.UTF8,
                "application/json")
        };
}

internal sealed class DelegateHttpMessageHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder)
    : HttpMessageHandler
{
    private int _callCount;

    public DelegateHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
        : this((request, attempt, _) => Task.FromResult(responder(request, attempt)))
    {
    }

    public DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _, _) => Task.FromResult(responder(request)))
    {
    }

    public int CallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _callCount);
        return responder(request, attempt, cancellationToken);
    }
}

internal sealed class TrackingHttpResponseMessage(HttpStatusCode statusCode) : HttpResponseMessage(statusCode)
{
    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            IsDisposed = true;
        }
        base.Dispose(disposing);
    }
}
