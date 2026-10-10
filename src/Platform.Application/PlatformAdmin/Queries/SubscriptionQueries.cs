using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// Anonymous lookups of the subscription wizard (CP-02): active countries and governorates, username availability.

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
