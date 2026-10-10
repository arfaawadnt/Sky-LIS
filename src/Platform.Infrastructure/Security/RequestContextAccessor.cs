using System.Security.Cryptography;
using System.Text;
using Platform.Application.Abstractions.Observability;

namespace Platform.Infrastructure.Security;

/// <summary>Scoped holder of the request facts audit rows record (01 §5.2). The host fills it per request; the outbox processor per event (trace parent only).</summary>
public sealed class RequestContextAccessor : IRequestContext
{
    public string? CorrelationId { get; private set; }

    public string? ClientIp { get; private set; }

    public byte[]? UserAgentHash { get; private set; }

    public void Set(string? correlationId, string? clientIp, string? userAgent)
    {
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId;
        ClientIp = string.IsNullOrWhiteSpace(clientIp) ? null : clientIp;
        UserAgentHash = string.IsNullOrEmpty(userAgent) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(userAgent));
    }

    public void Clear()
    {
        CorrelationId = null;
        ClientIp = null;
        UserAgentHash = null;
    }
}
