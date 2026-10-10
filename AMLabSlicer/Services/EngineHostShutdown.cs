using System.Net;
using System.Net.Http;
namespace AMLabSlicer.Services;

public static class EngineHostShutdown
{
    public static async Task RequestAsync(HttpClient client, Uri? endpoint = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint ?? new Uri("http://localhost:50051/shutdown"))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
