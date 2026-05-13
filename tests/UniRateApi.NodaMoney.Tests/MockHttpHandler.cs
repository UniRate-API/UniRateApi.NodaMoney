using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace UniRateApi.NodaMoney.Tests;

internal sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public HttpRequestMessage LastRequest
        => Requests.Count == 0
            ? throw new InvalidOperationException("No requests captured yet")
            : Requests[^1];

    public MockHttpHandler Enqueue(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    public MockHttpHandler EnqueueOk(string body) => Enqueue(HttpStatusCode.OK, body);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException("No queued responses left for MockHttpHandler");
        var (status, body) = _responses.Dequeue();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        };
        return Task.FromResult(response);
    }
}
