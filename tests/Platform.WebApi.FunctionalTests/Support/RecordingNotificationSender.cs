using System.Collections.Concurrent;
using Platform.Application.Abstractions.Notifications;

namespace Platform.WebApi.FunctionalTests.Support;

/// <summary>
/// Replaces the logging sender in the test host: every message is kept in memory so a test can read the one-time
/// code the way a person reads it from their phone. Nothing leaves the process.
/// </summary>
public sealed class RecordingNotificationSender : INotificationSender
{
    private readonly ConcurrentQueue<NotificationMessage> _messages = new();

    public IReadOnlyList<NotificationMessage> Messages => _messages.ToList();

    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The newest message to a recipient, or <see langword="null"/>.</summary>
    public NotificationMessage? LastTo(string recipient) =>
        _messages.LastOrDefault(message => string.Equals(message.To, recipient, StringComparison.OrdinalIgnoreCase));

    public void Clear() => _messages.Clear();
}
