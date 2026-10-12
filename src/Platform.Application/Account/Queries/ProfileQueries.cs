using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Contracts.Account;
using Platform.Domain.Account;
using Platform.Domain.Settings;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account.Queries;

/// <summary>
/// CP-65 / SA-21: the signed-in user's own account (user, membership of the session's tenant, the password rule that
/// applies to them) and, for a laboratory, its client record. Self-service: any authenticated actor, own data only.
/// </summary>
[RequireAuthenticatedActor]
public sealed record GetMyAccountProfileQuery : IQuery<MyAccountProfile>;

public sealed class GetMyAccountProfileHandler(
    IAccountDirectory directory,
    ILaboratoryProfileLookup laboratories,
    ILaboratoryLogoRepository logos,
    ITenantSettings settings,
    SessionPolicy policy,
    ICurrentUser user,
    ICurrentTenant tenant) : IQueryHandler<GetMyAccountProfileQuery, MyAccountProfile>
{
    public async Task<MyAccountProfile> Handle(GetMyAccountProfileQuery query, CancellationToken cancellationToken)
    {
        var userId = AccountActor.Require(user);
        var account = await directory.GetOwnAsync(userId, cancellationToken)
            ?? throw new DomainException(AccountCodes.MembershipNotFound, "The account was not found.", DomainFault.NotFound);

        var tenantId = tenant.TenantId;
        var isPlatform = tenantId is { } id && TenantIds.IsPlatform(id);
        var laboratory = tenantId is { } labId && !isPlatform ? await laboratories.FindAsync(labId, cancellationToken) ?? Placeholder(labId) : null;
        if (laboratory is not null)
        {
            // the top bar reloads the image when this changes; null = no logo (the initials are shown)
            laboratory = laboratory with { LogoVersion = (await logos.FindActiveAsync(cancellationToken))?.Version };
        }

        var minimum = Math.Max(policy.PasswordMinLength, await settings.GetAsync(SettingKeys.PasswordMinLength, cancellationToken));

        return new MyAccountProfile(
            account.UserId,
            account.Username,
            account.DisplayName,
            account.Email,
            account.Mobile,
            account.PreferredLanguage ?? "en",
            tenantId,
            isPlatform,
            account.FullNameEn,
            account.FullNameAr,
            account.EmployeeCode,
            account.Designation,
            account.RoleNameEn,
            account.RoleNameAr,
            account.MustChangePassword,
            account.PasswordChangedAtUtc,
            account.PasswordExpiresAtUtc,
            new PasswordPolicyView(minimum, PasswordRule.MaxLength),
            laboratory);
    }

    /// <summary>Intake §16 Q11: no client record (a tenant seeded without one, or the platform tables not migrated) → the tenant id only.</summary>
    private static LaboratoryProfile Placeholder(Guid tenantId) =>
        new(tenantId, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
