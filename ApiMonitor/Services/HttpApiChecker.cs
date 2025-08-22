using ApiMonitor.Auth;
using ApiMonitor.Options;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ApiMonitor.Services
{
    public class HttpApiChecker : IApiChecker
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IAuthProvider _authProvider;
        private readonly int _defaultTimeoutSeconds;
        private readonly ApiMonitorOptions _allOpts;
        private readonly IDataBinder _binder;
        private readonly ISecretResolver _secrets;

        public HttpApiChecker(IHttpClientFactory f, IAuthProvider auth, IOptions<ApiMonitorOptions> opts, IDataBinder binder, ISecretResolver secrets)
        {
            _httpClientFactory = f;
            _authProvider = auth;
            _defaultTimeoutSeconds = opts.Value.DefaultTimeoutSeconds;
            _allOpts = opts.Value;
            _binder = binder;
            _secrets = secrets;
        }

        public async Task<ApiCheckResult> CheckAsync(ApiEndpoint ep, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient("monitored");
            client.Timeout = TimeSpan.FromSeconds(_defaultTimeoutSeconds);

            var (url, body, headerSetter) = await _binder.BindAsync(ep, ct);

            using var req = new HttpRequestMessage(new HttpMethod(ep.Method), url) { Content = body };
            headerSetter?.Invoke(req.Headers);

            // Apply auth
            // Resolve + materialize auth (profiles -> then KV/env secrets)
            var effectiveAuth = ResolveAuth(ep);
            effectiveAuth = await _secrets.ResolveAsync(effectiveAuth, ct);

            // (optional) quick visibility that secrets resolved
            Serilog.Log.Information(
                "Auth materialized: endpoint='{Name}' ref='{Ref}' type='{Type}' hasClientId={HasId} hasClientSecret={HasSec}",
                ep.Name, ep.AuthRef ?? _allOpts.DefaultAuthRef, effectiveAuth.Type,
                !string.IsNullOrWhiteSpace(effectiveAuth.ClientId),
                !string.IsNullOrWhiteSpace(effectiveAuth.ClientSecret));

            // Apply auth
            await _authProvider.ApplyAsync(req, effectiveAuth, ct);
            var hasBearer = req.Headers.Authorization != null;


            // Log at Warning so it shows up by default
            Log.ForContext<HttpApiChecker>().Warning(
                "Auth check: endpoint='{Name}' ref='{Ref}' type='{Type}' hasBearer={HasBearer}",
                ep.Name, ep.AuthRef ?? _allOpts.DefaultAuthRef, effectiveAuth.Type, hasBearer);

            // Send
            var sw = Stopwatch.StartNew();
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            var elapsed = sw.Elapsed;

            // Status + diagnostics
            if (!ep.Success.StatusCodes.Contains((int)resp.StatusCode))
            {
                var www = resp.Headers.WwwAuthenticate.FirstOrDefault()?.ToString();
                var detail = $"Got {(int)resp.StatusCode}";
                if (!string.IsNullOrWhiteSpace(www)) detail += $" (WWW-Authenticate: {www})";

                // include response body preview — this is where 400 explains what's wrong
                string bodyPreview = "";
                try
                {
                    var text = await resp.Content.ReadAsStringAsync(ct);
                    if (!string.IsNullOrWhiteSpace(text))
                        bodyPreview = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
                }
                catch { /* ignore */ }

                if (!string.IsNullOrWhiteSpace(bodyPreview))
                    detail += $" Body: {bodyPreview}";

                return ApiCheckResult.Fail(ep.Name, "BadStatus", detail, elapsed);
            }


            if (ep.Success.MaxLatencyMs is int maxMs && elapsed.TotalMilliseconds > maxMs)
                return ApiCheckResult.Fail(ep.Name, "Slow", $"{(int)elapsed.TotalMilliseconds}ms > {maxMs}ms", elapsed);

            if (!string.IsNullOrWhiteSpace(ep.Success.JsonPointer))
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(text);
                var preview = text.Length > 400 ? text[..400] + "..." : text;

                if (!TryGetByPointer(doc.RootElement, ep.Success.JsonPointer!, out var value))
                    return ApiCheckResult.Fail(ep.Name, "JsonMismatch",
                        $"Pointer '{ep.Success.JsonPointer}' not found. Body preview: {preview}", elapsed);

                if (!string.IsNullOrEmpty(ep.Success.ExpectedValue))
                {
                    var actual = value?.ToString() ?? "";
                    var cmp = ep.Success.CaseInsensitiveCompare ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (!string.Equals(actual, ep.Success.ExpectedValue, cmp))
                    {
                        var shown = actual.Length > 120 ? actual[..120] + "..." : actual;
                        return ApiCheckResult.Fail(ep.Name, "JsonMismatch",
                            $"Pointer '{ep.Success.JsonPointer}' value: '{shown}' | Expected: '{ep.Success.ExpectedValue}'. Body preview: {preview}",
                            elapsed);
                    }
                }
            }

            return ApiCheckResult.Ok(ep.Name, elapsed);
        }

        private static bool TryGetByPointer(JsonElement root, string pointer, out JsonElement? value)
        {
            value = null;
            if (!pointer.StartsWith("/")) return false;
            var current = root;
            foreach (var rawToken in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var token = rawToken.Replace("~1", "/").Replace("~0", "~"); // JSON Pointer unescape
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(token, out var prop))
                    current = prop;
                else if (current.ValueKind == JsonValueKind.Array && int.TryParse(token, out var idx) && idx < current.GetArrayLength())
                    current = current[idx];
                else
                    return false;
            }
            value = current;
            return true;
        }

        private AuthConfig ResolveAuth(ApiEndpoint ep)
        {
            string? refName = !string.IsNullOrWhiteSpace(ep.AuthRef) ? ep.AuthRef : _allOpts.DefaultAuthRef;

            AuthConfig? prof = null;
            if (!string.IsNullOrWhiteSpace(refName) && _allOpts.AuthProfiles != null)
                _allOpts.AuthProfiles.TryGetValue(refName, out prof);

            static string? Take(string? endpointVal, string? profileVal) =>
                !string.IsNullOrWhiteSpace(endpointVal) ? endpointVal : profileVal;

            var merged = new AuthConfig
            {
                Type = Take(ep.Auth?.Type, prof?.Type),
                Header = Take(ep.Auth?.Header, prof?.Header),
                Value = Take(ep.Auth?.Value, prof?.Value),
                TokenUrl = Take(ep.Auth?.TokenUrl, prof?.TokenUrl),
                ClientId = Take(ep.Auth?.ClientId, prof?.ClientId),
                ClientSecret = Take(ep.Auth?.ClientSecret, prof?.ClientSecret),
                Scope = Take(ep.Auth?.Scope, prof?.Scope),
                Resource = Take(ep.Auth?.Resource, prof?.Resource)
            };

            // final fallback
            if (string.IsNullOrWhiteSpace(merged.Type))
                merged.Type = "None";

            return merged;
        }


        private static Dictionary<string, string> JwtPeek(string jwt)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return map;
                string Base64UrlToString(string s)
                {
                    s = s.Replace('-', '+').Replace('_', '/');
                    switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
                    return Encoding.UTF8.GetString(Convert.FromBase64String(s));
                }

                var payloadJson = Base64UrlToString(parts[1]);
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                string JoinArray(JsonElement e) =>
                    e.ValueKind == JsonValueKind.Array
                        ? string.Join(",", e.EnumerateArray().Select(x => x.ToString()))
                        : e.ToString();

                void Add(string name)
                {
                    if (root.TryGetProperty(name, out var v))
                        map[name] = JoinArray(v);
                }

                Add("aud"); Add("iss"); Add("scp"); Add("roles"); Add("appid"); Add("tid");
            }
            catch { /* swallow – diagnostics only */ }
            return map;
        }
    }
}
