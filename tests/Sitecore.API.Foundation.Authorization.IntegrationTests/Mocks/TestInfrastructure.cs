using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Mocks
{
    internal sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    public class TestLogger<T> : ILogger<T>, IDisposable
    {
        public readonly List<LogEntry> Entries = new();
        private readonly ITestOutputHelper? _output;
        public TestLogger(ITestOutputHelper? output = null) => _output = output;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            Entries.Add(new LogEntry { Level = logLevel, Message = msg, Exception = exception });
            _output?.WriteLine($"[{logLevel}] {msg} {exception?.Message}");
        }
        public void Dispose() { }
        public sealed class LogEntry
        {
            public LogLevel Level { get; set; }
            public string Message { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
        }
    }

    public class InMemoryLogger<T> : TestLogger<T>
    {
        public InMemoryLogger(ITestOutputHelper? o = null) : base(o) { }
    }

    public sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _syncResponder;
        private readonly Func<HttpRequestMessage, System.Threading.CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>>? _asyncResponder;
        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        private HttpResponseMessage? _presetResponse;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string? body)
        {
            _syncResponder = _ => new HttpResponseMessage(statusCode)
            {
                Content = body is null ? null : new StringContent(body)
            };
        }
        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _syncResponder = responder;
        public MockHttpMessageHandler(Func<HttpRequestMessage, System.Threading.CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> responder)
        {
            _asyncResponder = responder;
            _syncResponder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
        public void SetResponse(HttpResponseMessage response) => _presetResponse = response;
        protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            if (_presetResponse != null) return _presetResponse;
            if (_asyncResponder != null) return await _asyncResponder(request, cancellationToken).ConfigureAwait(false);
            return _syncResponder(request);
        }
    }
}
