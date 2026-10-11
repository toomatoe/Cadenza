using System.Net;
using System.Text;

namespace Cadenza.Tests.Fixtures;

internal sealed class FakeHandler : HttpMessageHandler
{
    public readonly Queue<HttpResponseMessage> Responses = new();
    public readonly List<string> Requests = [];
    public readonly List<string?> Bearers = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!.AbsoluteUri); Bearers.Add(request.Headers.Authorization?.Parameter);
        return Task.FromResult(Responses.Dequeue());
    }
}
