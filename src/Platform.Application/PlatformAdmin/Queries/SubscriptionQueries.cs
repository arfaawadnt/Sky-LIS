using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// Anonymous lookups of the subscription wizard (CP-02): active countries and governorates, username availability, and the
// early checks of LIS-CR-014 (step-1 mobiles before the code is sent, the username of a verified request).

[AllowUnauthenticated]
public sealed record ListPublicCountriesQuery : IQuery<IReadOnlyList<PublicCountry>>;

public sealed class ListPublicCountriesHandler(ICountryRepository countries) : IQueryHandler<ListPublicCountriesQuery, IReadOnlyList<PublicCountry>>
{
    public Task<IReadOnlyList<PublicCountry>> Handle(ListPublicCountriesQuery query, CancellationToken cancellationToken) => countries.ListPublicAsync(cancellationToken);
}

/// <summary>Active governorates of one country, or of every country when <paramref name="CountryId"/> is null (the patient and branch forms of a laboratory read the whole list once).</summary>
[AllowUnauthenticated]
public sealed record ListPublicGovernoratesQuery(Guid? CountryId) : IQuery<IReadOnlyList<PublicGovernorate>>;

public sealed class ListPublicGovernoratesValidator : AbstractValidator<ListPublicGovernoratesQuery>
{
    public ListPublicGovernoratesValidator() => RuleFor(query => query.CountryId).NotEqual(Guid.Empty).When(query => query.CountryId.HasValue);
}

public sealed class ListPublicGovernoratesHandler(IGovernorateRepository governorates) : IQueryHandler<ListPublicGovernoratesQuery, IReadOnlyList<PublicGovernorate>>
{
    public Task<IReadOnlyList<PublicGovernorate>> Handle(ListPublicGovernoratesQuery query, CancellationToken cancellationToken) => governorates.ListPublicAsync(query.CountryId, cancellationToken);
}

/// <summary>CP-02 step 3: is the username acceptable and free? Rate-limited at the edge (it tells whether an account exists).</summary>
[AllowUnauthenticated]
public sealed record CheckUsernameQuery(string Username) : IQuery<UsernameAvailability>;

public sealed class CheckUsernameValidator : AbstractValidator<CheckUsernameQuery>
{
    public CheckUsernameValidator() => RuleFor(query => query.Username).NotEmpty().MaximumLength(PlatformUser.MaxUsernameLength);
}

public sealed class CheckUsernameHandler(IPlatformUserRepository users) : IQueryHandler<CheckUsernameQuery, UsernameAvailability>
{
    public async Task<UsernameAvailability> Handle(CheckUsernameQuery query, CancellationToken cancellationToken)
    {
        string normalised;
        try
        {
            normalised = PlatformUser.NormaliseUsername(query.Username);
        }
        catch (DomainException)
        {
            return new UsernameAvailability(query.Username.Trim(), Valid: false, Available: false);
        }

        return new UsernameAvailability(normalised, Valid: true, Available: !await users.UsernameExistsAsync(normalised, cancellationToken));
    }
}

/// <summary>
/// LIS-CR-014 (S1): the early check of step 1 — each mobile against the country's rule when the country is given (GEO-010 /
/// GEO-014), and the manager's mobile against the platform's accounts (SUB-011), so the wizard marks the field before the code is
/// sent. Anonymous and rate-limited like start (it tells whether an account holds a number, as start itself must).
/// </summary>
[AllowUnauthenticated]
public sealed record CheckSubscriptionDetailsQuery(Guid? CountryId, string? Mobile, string? ManagerMobile) : IQuery<SubscriptionCheckResult>;

public sealed class CheckSubscriptionDetailsValidator : AbstractValidator<CheckSubscriptionDetailsQuery>
{
    public CheckSubscriptionDetailsValidator()
    {
        RuleFor(query => query.CountryId).NotEqual(Guid.Empty).When(query => query.CountryId.HasValue);
        RuleFor(query => query.Mobile).MaximumLength(Tenant.MaxMobileLength + 10);
        RuleFor(query => query.ManagerMobile).MaximumLength(Tenant.MaxMobileLength + 10);
    }
}

public sealed class CheckSubscriptionDetailsHandler(ICountryRepository countries, IUserMobileNumbers mobiles) : IQueryHandler<CheckSubscriptionDetailsQuery, SubscriptionCheckResult>
{
    public async Task<SubscriptionCheckResult> Handle(CheckSubscriptionDetailsQuery query, CancellationToken cancellationToken)
    {
        var country = query.CountryId is { } countryId ? await countries.GetForUpdateAsync(countryId, cancellationToken) : null;
        if (query.CountryId is not null && country is null)
        {
            return new SubscriptionCheckResult([new SubscriptionFieldIssue(SubscriptionFields.CountryId, PlatformAdminCodes.CountryNotFound)]);
        }

        var issues = new List<SubscriptionFieldIssue>(2);
        foreach (var (field, value, code, label, unique) in new[]
        {
            (SubscriptionFields.Mobile, query.Mobile, PlatformAdminCodes.MobileInvalidForCountry, "The laboratory mobile", false),
            (SubscriptionFields.ManagerMobile, query.ManagerMobile, PlatformAdminCodes.ManagerMobileInvalidForCountry, "The manager's mobile", true),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string number;
            try
            {
                number = country is null ? value.Trim() : country.NormaliseMobile(value, code, label);
            }
            catch (DomainException refusal)
            {
                issues.Add(new SubscriptionFieldIssue(field, refusal.Code));
                continue;
            }

            if (unique && await SubscriptionChecks.ManagerMobileInUseAsync(mobiles, number, cancellationToken))
            {
                issues.Add(new SubscriptionFieldIssue(field, PlatformAdminCodes.MobileInUse));
            }
        }

        return new SubscriptionCheckResult(issues);
    }
}

/// <summary>
/// LIS-CR-014 (S2): step 3's availability check of the administrator username, for a request whose mobile is verified (SUB-003
/// otherwise, SUB-001 when expired) — so only someone who proved a mobile can probe usernames. Issues: CLI-042 (shape), SUB-013 (taken).
/// </summary>
[AllowUnauthenticated]
public sealed record CheckSubscriptionUsernameQuery(Guid SubscriptionId, string Username) : IQuery<SubscriptionCheckResult>;

public sealed class CheckSubscriptionUsernameValidator : AbstractValidator<CheckSubscriptionUsernameQuery>
{
    public CheckSubscriptionUsernameValidator()
    {
        RuleFor(query => query.SubscriptionId).NotEmpty();
        RuleFor(query => query.Username).NotEmpty().MaximumLength(PlatformUser.MaxUsernameLength);
    }
}

public sealed class CheckSubscriptionUsernameHandler(ISubscriptionRequestRepository requests, IPlatformUserRepository users, IClock clock)
    : IQueryHandler<CheckSubscriptionUsernameQuery, SubscriptionCheckResult>
{
    public async Task<SubscriptionCheckResult> Handle(CheckSubscriptionUsernameQuery query, CancellationToken cancellationToken)
    {
        var request = await requests.GetForUpdateAsync(query.SubscriptionId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.SubscriptionNotFound, "Subscription request not found.", DomainFault.NotFound);
        request.EnsureCanComplete(clock.UtcNow);

        string normalised;
        try
        {
            normalised = PlatformUser.NormaliseUsername(query.Username);
        }
        catch (DomainException)
        {
            return new SubscriptionCheckResult([new SubscriptionFieldIssue(SubscriptionFields.Username, PlatformAdminCodes.UsernameInvalid)]);
        }

        return await users.UsernameExistsAsync(normalised, cancellationToken)
            ? new SubscriptionCheckResult([new SubscriptionFieldIssue(SubscriptionFields.Username, PlatformAdminCodes.UsernameInUse)])
            : new SubscriptionCheckResult([]);
    }
}
