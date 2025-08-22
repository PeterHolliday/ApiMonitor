using ApiMonitor.Auth;
using ApiMonitor.Notifications;
using ApiMonitor.Options;
using ApiMonitor.Services;
using ApiMonitor.Workers;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Serilog;

Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)            // reads Serilog section if present
        .Enrich.FromLogContext()
        //.WriteTo.Console()                                     // hard-wire console so you see logs now
        .WriteTo.File("logs/monitor-.log", rollingInterval: RollingInterval.Day))
    .ConfigureServices((ctx, services) =>
    {
        // Bind options from "ApiMonitor" section
        services.Configure<ApiMonitorOptions>(ctx.Configuration.GetSection("ApiMonitor"));

        // Key Vault client (single default vault)
        var vaultUri = ctx.Configuration["KeyVault:VaultUri"]; // e.g. https://myvault.vault.azure.net/
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            services.AddSingleton(new SecretClient(new Uri(vaultUri), new DefaultAzureCredential()));
        }

        // Secret resolver (env vars + Key Vault)
        services.AddSingleton<ISecretResolver, KeyVaultSecretResolver>();

        // Quick visibility that config actually bound
        services.PostConfigure<ApiMonitorOptions>(opts =>
            Console.WriteLine($"[Config check] ApiMonitor.Endpoints = {opts.Endpoints?.Count ?? 0}"));

        // HttpClient with Polly retrier
        services.AddHttpClient("monitored")
            .AddTransientHttpErrorPolicy(p =>
                p.OrResult(r => (int)r.StatusCode == 429)
                 .WaitAndRetryAsync(3, retry => TimeSpan.FromMilliseconds(200 * Math.Pow(2, retry))));

        // DB-backed binding services
        services.AddSingleton<IDbQueryService, OracleDbQueryService>();
        services.AddSingleton<IDataBinder, DataBinder>();

        // Auth + checking + notifications
        services.AddSingleton<ITokenCache, InMemoryTokenCache>();
        services.AddSingleton<IAuthProvider, CompositeAuthProvider>();
        services.AddSingleton<IApiChecker, HttpApiChecker>();
        services.AddSingleton<INotifier, EmailNotifier>();

        // Worker
        services.AddHostedService<ApiMonitorWorker>();

        services.PostConfigure<ApiMonitorOptions>(o =>
            Console.WriteLine($"[AuthProfiles] count={o.AuthProfiles?.Count ?? 0}, defaultRef='{o.DefaultAuthRef}'"));

        services.AddSingleton<IResultSink, OracleResultSink>();

    })
    .Build()
    .Run();
