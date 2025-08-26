using APIMonitor.Dashboard.Data;
using APIMonitor.Dashboard.Models;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace APIMonitor.Dashboard.Components.Pages
{
    public partial class Index : ComponentBase
    {
        [Inject] public IStatusRepository Repo { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public NotificationService Notify { get; set; } = default!;

        protected List<EndpointStatusDto> Items { get; private set; } = new();
        protected List<EndpointStatusDto> Filtered =>
            string.IsNullOrWhiteSpace(SelectedEnv) ? Items : Items.Where(x => x.Environment == SelectedEnv).ToList();

        protected List<string> Environments { get; private set; } = new();
        protected string? SelectedEnv { get; set; }
        protected EndpointStatusDto? Selected { get; private set; }
        protected List<TrendPointDto> TrendForSelected { get; private set; } = new();
        protected int RefreshSeconds => Config.GetValue<int?>("Dashboard:RefreshSeconds") ?? 30;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;

        protected async Task OnInitializedAsync()
        {
            await RefreshAsync();
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(RefreshSeconds));
            _ = RunAutoRefreshAsync(_cts.Token);
        }

        private async Task RunAutoRefreshAsync(CancellationToken ct)
        {
            try
            {
                while (await _timer!.WaitForNextTickAsync(ct))
                {
                    await RefreshAsync();
                    StateHasChanged();
                }
            }
            catch (OperationCanceledException) { }
        }

        protected async Task RefreshAsync()
        {
            try
            {
                var latest = await Repo.GetLatestAsync(CancellationToken.None);
                Items = latest.ToList();

                if (Environments.Count == 0)
                {
                    Environments = (await Repo.GetEnvironmentsAsync(CancellationToken.None)).ToList();
                    if (SelectedEnv is null && Environments.Count > 0) SelectedEnv = Environments[0];
                }

                if (Selected is not null)
                {
                    TrendForSelected = (await Repo.GetTrendAsync(Selected.EndpointName, Selected.Environment, TimeSpan.FromHours(24), CancellationToken.None)).ToList();
                }
            }
            catch (Exception ex)
            {
                Notify.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = "Refresh failed", Detail = ex.Message, Duration = 4000 });
            }
        }

        protected async void OnRowSelect(EndpointStatusDto row)
        {
            Selected = row;
            TrendForSelected = (await Repo.GetTrendAsync(row.EndpointName, row.Environment, TimeSpan.FromHours(24), CancellationToken.None)).ToList();
            StateHasChanged();
        }

        protected void OnEnvChanged() => StateHasChanged();

        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            if (_timer is not null) _timer.Dispose();
            await Task.CompletedTask;
        }
    }
}