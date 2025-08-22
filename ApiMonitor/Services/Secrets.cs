using ApiMonitor.Options;
using Azure.Security.KeyVault.Secrets;
using System.Collections.Concurrent;

namespace ApiMonitor.Services
{
    public interface ISecretResolver
    {
        Task<string?> ResolveAsync(string? value, CancellationToken ct);
        Task<AuthConfig> ResolveAsync(AuthConfig cfg, CancellationToken ct);
    }

    public class KeyVaultSecretResolver : ISecretResolver
    {
        private readonly SecretClient? _kv;
        private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

        public KeyVaultSecretResolver(SecretClient? kv = null) => _kv = kv;

        public async Task<string?> ResolveAsync(string? value, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            // env:SECRET_NAME -> pick from environment
            if (value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                var name = value.Substring(4);
                return Environment.GetEnvironmentVariable(name);
            }

            // kv:SecretName or kv:SecretName#version
            if (value.StartsWith("kv:", StringComparison.OrdinalIgnoreCase))
            {
                if (_kv is null) return null; // no KV configured
                var id = value.Substring(3);
                if (_cache.TryGetValue(id, out var cached)) return cached;

                var parts = id.Split('#', 2);
                var name = parts[0];
                var version = parts.Length == 2 ? parts[1] : null;

                var resp = version is null
                    ? await _kv.GetSecretAsync(name, cancellationToken: ct)
                    : await _kv.GetSecretAsync(name, version, cancellationToken: ct);

                var secretVal = resp.Value.Value; // Response<KeyVaultSecret> -> KeyVaultSecret -> string
                _cache[id] = secretVal;
                return secretVal;
            }


            // literal value (no indirection)
            return value;
        }

        public async Task<AuthConfig> ResolveAsync(AuthConfig cfg, CancellationToken ct)
        {
            // clone with resolved fields (only ones that might contain secrets/placeholders)
            return new AuthConfig
            {
                Type = cfg.Type,
                Header = cfg.Header,
                Value = await ResolveAsync(cfg.Value, ct),
                TokenUrl = cfg.TokenUrl,
                ClientId = await ResolveAsync(cfg.ClientId, ct),
                ClientSecret = await ResolveAsync(cfg.ClientSecret, ct),
                Scope = cfg.Scope,
                Resource = cfg.Resource
            };
        }
    }
}
