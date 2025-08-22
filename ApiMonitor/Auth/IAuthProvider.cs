using ApiMonitor.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ApiMonitor.Auth
{
    public interface IAuthProvider
    {
        Task ApplyAsync(HttpRequestMessage req, AuthConfig cfg, CancellationToken ct);
    }

    public class CompositeAuthProvider : IAuthProvider
    {
        private readonly ITokenCache _cache;
        public CompositeAuthProvider(ITokenCache cache) => _cache = cache;

        public async Task ApplyAsync(HttpRequestMessage req, AuthConfig cfg, CancellationToken ct)
        {
            var type = (cfg.Type ?? "None").Trim().ToLowerInvariant();

            switch (type)
            {
                case "apikey":
                case "api_key":
                case "api-key":
                    var header = string.IsNullOrWhiteSpace(cfg.Header) ? "X-API-Key" : cfg.Header!;
                    if (!string.IsNullOrWhiteSpace(cfg.Value))
                        req.Headers.TryAddWithoutValidation(header, cfg.Value);
                    break;

                case "oauth2":
                case "oauth":
                case "aad":
                case "bearer":
                    var token = await _cache.GetTokenAsync(cfg, ct);
                    if (!string.IsNullOrWhiteSpace(token))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    break;

                case "none":
                default:
                    // no auth
                    break;
            }
        }
    }

    public interface ITokenCache
    {
        Task<string> GetTokenAsync(AuthConfig cfg, CancellationToken ct);
    }

    public class InMemoryTokenCache : ITokenCache
    {
        private readonly HttpClient _http = new();
        private readonly Dictionary<string, (string token, DateTimeOffset exp)> _cache = new();

        public async Task<string> GetTokenAsync(AuthConfig cfg, CancellationToken ct)
        {
            var key = $"{cfg.TokenUrl}|{cfg.ClientId}|{cfg.Scope}|{cfg.Resource}";
            if (_cache.TryGetValue(key, out var entry) && entry.exp > DateTimeOffset.UtcNow.AddMinutes(2))
                return entry.token;

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = cfg.ClientId ?? string.Empty,
                ["client_secret"] = cfg.ClientSecret ?? string.Empty
            };
            if (!string.IsNullOrWhiteSpace(cfg.Scope))
                form["scope"] = cfg.Scope!;
            else if (!string.IsNullOrWhiteSpace(cfg.Resource))
                form["resource"] = cfg.Resource!;

            using var resp = await _http.PostAsync(cfg.TokenUrl!, new FormUrlEncodedContent(form), ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Token request failed: {(int)resp.StatusCode} {text}");

            using var doc = JsonDocument.Parse(text);
            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3600;
            _cache[key] = (token, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
            return token;
        }

    }

}
