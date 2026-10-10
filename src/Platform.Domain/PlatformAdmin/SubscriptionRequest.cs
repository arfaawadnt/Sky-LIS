using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// The pending state of the public subscription wizard (CP-02, <c>saas.subscription_requests</c>): the laboratory's
/// details from step 1, proved by the one-time code sent to the manager's mobile (step 2), completed by the
/// administrator credentials (step 3), which provision the tenant. Pending → Verified → Completed within
/// <see cref="Lifetime"/> of the start (SUB-001); a completed request cannot be reused (SUB-002/003).
/// </summary>
public sealed class SubscriptionRequest : AggregateRoot
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>Codes sent for one request (the first and four re-sends); more is SUB-004.</summary>
    public const int MaxCodesSent = 5;

    private SubscriptionRequest()
    {
        NameEn = string.Empty;
        NameAr = string.Empty;
        Address = string.Empty;
        Mobile = string.Empty;
        ManagerName = string.Empty;
        ManagerMobile = string.Empty;
        Language = "en";
    }

    private SubscriptionRequest(Guid id, ClientProfile profile, string language, DateTimeOffset nowUtc)
        : base(id)
    {
        NameEn = profile.Name.En;
        NameAr = profile.Name.Ar;
        CountryId = profile.CountryId;
        GovernorateId = profile.GovernorateId;
        Address = profile.Address;
        Mobile = profile.Mobile;
        LicenceNo = profile.LicenceNo;
        ManagerName = profile.ManagerName;
        ManagerMobile = profile.ManagerMobile;
        Email = profile.Email;
        Language = language;
        Status = SubscriptionRequestStatus.Pending;
        StartedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc + Lifetime;
        CodesSent = 1;
        LastCodeSentAtUtc = nowUtc;
    }

    public string NameEn { get; private set; }

    public string NameAr { get; private set; }

    public Guid CountryId { get; private set; }

    public Guid GovernorateId { get; private set; }

    public string Address { get; private set; }

    public string Mobile { get; private set; }

    public string? LicenceNo { get; private set; }

    public string ManagerName { get; private set; }

    /// <summary>E.164; the target of the one-time code (purpose <c>subscription</c>).</summary>
    public string ManagerMobile { get; private set; }

    public string? Email { get; private set; }

    public string Language { get; private set; }

    public SubscriptionRequestStatus Status { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public int CodesSent { get; private set; }

    public DateTimeOffset LastCodeSentAtUtc { get; private set; }

    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>The tenant this request became.</summary>
    public Guid? TenantId { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>S1 Start (anonymous): the profile was validated against the country (mobile lengths) and the governorate.</summary>
    public static SubscriptionRequest Start(ClientProfile profile, Country country, Governorate governorate, string? language, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(governorate);
        Require(country.Id == profile.CountryId && governorate.Id == profile.GovernorateId, PlatformAdminCodes.ClientDetailsInvalid, "The country and governorate do not match the details.", DomainFault.Validation);
        Require(country.IsActive, PlatformAdminCodes.CountryInactive, "The country is inactive.", DomainFault.Conflict);
        Require(governorate.IsActive, PlatformAdminCodes.GovernorateInactive, "The governorate is inactive.", DomainFault.Conflict);
        Require(governorate.CountryId == country.Id, PlatformAdminCodes.GovernorateNotInCountry, "The governorate does not belong to the country.", DomainFault.Validation);

        var lang = language is not null && language.Trim().StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        var request = new SubscriptionRequest(Guid.CreateVersion7(), profile, lang, nowUtc);
        request.Raise(new SubscriptionRequested(request.Id, country.Id, nowUtc));
        return request;
    }

    /// <summary>S1' Re-send the code: Pending and alive, at most <see cref="MaxCodesSent"/> codes per request (SUB-004).</summary>
    public void RecordCodeSent(DateTimeOffset nowUtc)
    {
        RequireAlive(nowUtc);
        RequireStatus(SubscriptionRequestStatus.Pending, PlatformAdminCodes.SubscriptionNotPending, "resend the code");
        Require(CodesSent < MaxCodesSent, PlatformAdminCodes.TooManyCodes, $"At most {MaxCodesSent} codes are sent for one request; start again.", DomainFault.Conflict);
        CodesSent++;
        LastCodeSentAtUtc = nowUtc;
    }

    /// <summary>Checked before the code is tried, so a dead request never consumes an attempt.</summary>
    public void EnsureCanVerify(DateTimeOffset nowUtc)
    {
        RequireAlive(nowUtc);
        RequireStatus(SubscriptionRequestStatus.Pending, PlatformAdminCodes.SubscriptionNotPending, "verify");
    }

    /// <summary>S2 Verify: Pending and alive; the caller has verified the one-time code (SUB-010 otherwise).</summary>
    public void MarkVerified(DateTimeOffset nowUtc)
    {
        EnsureCanVerify(nowUtc);
        Status = SubscriptionRequestStatus.Verified;
        VerifiedAtUtc = nowUtc;
        Raise(new SubscriptionVerified(Id, nowUtc));
    }

    /// <summary>S3 precondition of Complete: Verified and alive.</summary>
    public void EnsureCanComplete(DateTimeOffset nowUtc)
    {
        RequireAlive(nowUtc);
        RequireStatus(SubscriptionRequestStatus.Verified, PlatformAdminCodes.SubscriptionNotVerified, "complete");
    }

    /// <summary>S3 Complete: records the tenant the request became; the request is frozen afterwards.</summary>
    public void Complete(Guid tenantId, DateTimeOffset nowUtc)
    {
        EnsureCanComplete(nowUtc);
        Status = SubscriptionRequestStatus.Completed;
        CompletedAtUtc = nowUtc;
        TenantId = tenantId;
        Raise(new SubscriptionCompleted(Id, tenantId, nowUtc));
    }

    public bool IsExpiredAt(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>A completed request is refused by its state (SUB-002/003), an open one by its age (SUB-001).</summary>
    private void RequireAlive(DateTimeOffset nowUtc)
    {
        if (Status != SubscriptionRequestStatus.Completed)
        {
            Require(!IsExpiredAt(nowUtc), PlatformAdminCodes.SubscriptionExpired, "The subscription request has expired; start again.", DomainFault.Conflict);
        }
    }

    private void RequireStatus(SubscriptionRequestStatus expected, string code, string action)
    {
        if (Status != expected)
        {
            throw new InvalidStateTransitionException(code, "subscription request", Status.ToString(), action);
        }
    }
}
