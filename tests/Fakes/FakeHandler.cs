namespace Cadenza.UnitTests.Fakes;

/// <summary>Returns queued responses and records each request's URL and bearer token.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public Queue<HttpResponseMessage> Responses { get; } = new();

    public List<string> Requests { get; } = [];

    public List<string?> Bearers { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request.RequestUri!.AbsoluteUri);
        Bearers.Add(request.Headers.Authorization?.Parameter);
        return Task.FromResult(Responses.Dequeue());
    }
}
