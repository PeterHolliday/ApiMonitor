using ApiMonitor.Services;

namespace ApiMonitor.Notifications
{
    public interface INotifier
    {
        Task NotifyFailureAsync(ApiCheckResult result, CancellationToken ct);
    }

    public class EmailNotifier : INotifier
    {
        public Task NotifyFailureAsync(ApiCheckResult result, CancellationToken ct)
        {
            // call your existing email service here (subject & body kept simple)
            // e.g., EmailService.Send("API check failed: " + result.Name, $"{result.Reason}\n{result.Details}");
            return Task.CompletedTask;
        }
    }

}
