using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Identity;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Identity;
using Platform.Infrastructure.Security.Hashing;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Security.Otp;

/// <summary>
/// One-time codes over <c>identity.otp_challenges</c> (SRS CP-02/CP-03). Issue: close the live challenge of the
/// same purpose and target, store the digests of the new one, send the code through the notification port. Verify:
/// a wrong code counts an attempt and is persisted before the refusal (like the lockout counter, 02 §6.1); the
/// fifth closes the challenge; the first match consumes it. This service commits its own rows: it is not on a
/// unit-of-work path and its facts must survive a refusal (InfrastructureHardeningTests allow-list).
/// </summary>
public sealed partial class OtpService(
    AppDbContext context,
    MasterKeyedHash mac,
    INotificationSender sender,
    SessionPolicy policy,
    SecurityOptions options,
    IClock clock,
    ILogger<OtpService> logger) : IOtpService
{
    private const string TargetPurpose = "otp-target";
    private const string CodePurpose = "otp-code";

    public async Task<Guid> IssueAsync(string purpose, string target, string language, CancellationToken cancellationToken, string templateCode = NotificationTemplateCodes.Otp, NotificationChannel channel = NotificationChannel.Sms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var now = clock.UtcNow;
        var normalisedTarget = Normalise(target);
        var targetHash = mac.Compute(TargetPurpose, purpose + ":" + normalisedTarget);
        var code = NewCode(policy.OtpLength);

        foreach (var live in await context.Set<OtpChallenge>().Where(c => c.Purpose == purpose && c.TargetHash == targetHash && c.ConsumedAtUtc == null).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            live.Consume(now);   // superseded: only the newest code is ever valid
        }

        var challenge = new OtpChallenge(purpose, targetHash, mac.Compute(CodePurpose, purpose + ":" + normalisedTarget + ":" + code), now, policy.OtpLifetime);
        context.Set<OtpChallenge>().Add(challenge);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["minutes"] = ((int)policy.OtpLifetime.TotalMinutes).ToString(CultureInfo.InvariantCulture),
        };
        await sender.SendAsync(new NotificationMessage(channel, target, templateCode, language, placeholders), cancellationToken).ConfigureAwait(false);

        if (options.RevealOtpInDevelopmentLog)
        {
            LogCode(logger, purpose, challenge.Id, code);   // loopback development only (Lis:Security:AllowInsecureLocalDevelopment); never in any other configuration
        }

        LogIssued(logger, purpose, challenge.Id);
        return challenge.Id;
    }

    public async Task<bool> VerifyAsync(string purpose, string target, string code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var now = clock.UtcNow;
        var normalisedTarget = Normalise(target);
        var targetHash = mac.Compute(TargetPurpose, purpose + ":" + normalisedTarget);
        var challenge = await context.Set<OtpChallenge>()
            .Where(c => c.Purpose == purpose && c.TargetHash == targetHash && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.IssuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (challenge is null || !challenge.IsLiveAt(now, policy.OtpMaxAttempts))
        {
            return false;
        }

        var presented = mac.Compute(CodePurpose, purpose + ":" + normalisedTarget + ":" + code.Trim());
        if (CryptographicOperations.FixedTimeEquals(presented, challenge.CodeHash))
        {
            challenge.Consume(now);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        challenge.RecordAttempt();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);   // the attempt is a fact even though the caller refuses
        LogRejected(logger, purpose, challenge.Id, challenge.Attempts);
        return false;
    }

    private static string Normalise(string target) => target.Trim().ToLowerInvariant();

    /// <summary>Uniform random digits from the CSPRNG; a leading zero is as likely as any other digit.</summary>
    private static string NewCode(int length)
    {
        Span<char> digits = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }

        return new string(digits);
    }

    [LoggerMessage(EventId = 7100, Level = LogLevel.Information, Message = "one-time code issued purpose={Purpose} challenge={ChallengeId}")]
    private static partial void LogIssued(ILogger logger, string purpose, Guid challengeId);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "one-time code rejected purpose={Purpose} challenge={ChallengeId} attempts={Attempts}")]
    private static partial void LogRejected(ILogger logger, string purpose, Guid challengeId, int attempts);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Debug, Message = "DEVELOPMENT one-time code purpose={Purpose} challenge={ChallengeId} code={Code}")]
    private static partial void LogCode(ILogger logger, string purpose, Guid challengeId, string code);
}
