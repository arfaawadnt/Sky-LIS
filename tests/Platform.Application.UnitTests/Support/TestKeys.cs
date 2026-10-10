using System.Security.Cryptography;
using Platform.Application.Identity;
using Platform.Infrastructure.Security;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.UnitTests.Support;

/// <summary>Fresh, valid security options for tests; nothing here is a real secret.</summary>
internal static class TestKeys
{
    public static string NewSigningKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }

    public static string NewKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static SecurityOptions ValidOptions(string? previousSubjectKey = null) => new()
    {
        Issuer = "https://tests.lis.local",
        Audience = "platform-api",
        SigningKeyPem = NewSigningKeyPem(),
        SubjectEncryptionKey = NewKeyBase64(),
        PreviousSubjectEncryptionKeys = previousSubjectKey is null ? [] : [previousSubjectKey],
        MasterKey = NewKeyBase64(),
    };

    public static SessionPolicy DefaultPolicy() => new();
}

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public static readonly DateTimeOffset Instant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
