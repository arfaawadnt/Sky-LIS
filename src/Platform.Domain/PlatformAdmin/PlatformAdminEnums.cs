namespace Platform.Domain.PlatformAdmin;

/// <summary>Life cycle of a client (SA-04, intake §6). Trial and Active may sign in (Trial until its end date); Inactive may not.</summary>
public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    Inactive = 2,
}

/// <summary>
/// What a row of <c>saas.tenants</c> is (database team review 2026-10-26, comments 3–4: the table is the registry of every tenant,
/// so every <c>tenant_id</c> has a foreign key to it). <see cref="Laboratory"/> is a client (SA-04) and the only kind the
/// application reads: the EF model filters the table on it. <see cref="Platform"/> is the one row of
/// <c>TenantIds.Platform</c> (National Technology's own tenant), written by migration <c>DbReviewTenantKeys</c> and by
/// <c>PlatformTenantRecord</c>, never by the domain.
/// </summary>
public enum TenantKind
{
    Laboratory = 0,
    Platform = 1,
}

/// <summary>How the client was created (SA-04 "source").</summary>
public enum TenantSource
{
    Subscription = 0,
    Manual = 1,
}

/// <summary>Wallet movements (SA-05): a top-up credits; the monthly subscription and metered usage debit.</summary>
public enum WalletTransactionKind
{
    TopUp = 0,
    Subscription = 1,
    Usage = 2,
}

/// <summary>A laboratory's request for credit (CP-66) until the platform tops up (Completed) or declines it.</summary>
public enum TopUpRequestStatus
{
    Pending = 0,
    Completed = 1,
    Declined = 2,
}

/// <summary>The channel a notification template is written for (SA-20).</summary>
public enum TemplateChannel
{
    Sms = 0,
    Email = 1,
}

/// <summary>Communication protocol of an analyser model (SA-15).</summary>
public enum AnalyserProtocol
{
    Astm = 0,
    Hl7 = 1,
    Proprietary = 2,
}

/// <summary>Physical link of an analyser model (SA-15).</summary>
public enum PhysicalLink
{
    Serial = 0,
    Tcp = 1,
    File = 2,
}

/// <summary>Standard code systems of the library (SA-17).</summary>
public enum CodeSystemName
{
    Loinc = 0,
    Cpt = 1,
    Icd10 = 2,
    Snomed = 3,
}

/// <summary>The pending state of the subscription wizard (CP-02): one hour from Pending to Completed.</summary>
public enum SubscriptionRequestStatus
{
    Pending = 0,
    Verified = 1,
    Completed = 2,
}
