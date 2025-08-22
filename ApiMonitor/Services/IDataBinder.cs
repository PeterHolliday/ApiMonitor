using ApiMonitor.Options;
using System.Net.Http.Headers;

namespace ApiMonitor.Services
{
    public interface IDataBinder
    {
        Task<(string url, HttpContent? body, Action<HttpRequestHeaders>? headers)>
            BindAsync(ApiEndpoint ep, CancellationToken ct);
    }
}
