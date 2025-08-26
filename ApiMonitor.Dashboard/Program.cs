using APIMonitor.Dashboard.Data;
using APIMonitor.Dashboard.Services;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Radzen;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext());

// Key Vault (optional but recommended)
var vaultUri = builder.Configuration["KeyVault:VaultUri"];
if (!string.IsNullOrWhiteSpace(vaultUri))
{
    builder.Services.AddSingleton(new SecretClient(new Uri(vaultUri), new DefaultAzureCredential()));
}
builder.Services.AddSingleton<ISecretResolver, KeyVaultSecretResolver>();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<DialogService>();
builder.Services.AddScoped<NotificationService>();

builder.Services.AddSingleton<IStatusRepository, StatusRepository>();

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.Run();
