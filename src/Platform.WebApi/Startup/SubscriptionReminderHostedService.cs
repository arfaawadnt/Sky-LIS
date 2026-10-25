using Platform.Infrastructure.Jobs;

namespace Platform.WebApi.Startup;

/// <summary>
/// Hosts the subscription reminders (LIS-CR-012, C-16) for the lifetime of the process, like the wallet charge. The job lives in
/// Infrastructure and reads <c>Lis:Jobs:SubscriptionReminders:Enabled</c> through its options, so a test host turns it off through
/// configuration added at <c>Build()</c>.
/// </summary>
public sealed class SubscriptionReminderHostedService(SubscriptionReminderJob job) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => job.RunAsync(stoppingToken);
}
