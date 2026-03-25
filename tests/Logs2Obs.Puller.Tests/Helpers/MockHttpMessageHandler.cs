namespace Logs2Obs.Puller.Tests.Helpers;

using System.Net;

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;
    private readonly Action<HttpRequestMessage>? _onRequest;

    public MockHttpMessageHandler(IEnumerable<HttpResponseMessage> responses, Action<HttpRequestMessage>? onRequest = null)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
        _onRequest = onRequest;
    }

    public int CallCount { get; private set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        _onRequest?.Invoke(request);

        if (_responses.Count == 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
