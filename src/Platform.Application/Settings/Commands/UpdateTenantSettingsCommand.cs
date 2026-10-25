using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Settings.Queries;
using Platform.Contracts.Settings;
using Platform.Domain.Authorization;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Settings.Commands;

/// <summary>
/// SRS CP-57 "Toggle; edit": stores one or more parameter values. Every key must be catalogued (<c>SET-001</c>) and
/// every value well-formed for its type and range (<c>SET-002</c>; a time-zone name the host does not know <c>SET-003</c>); the whole request is refused before anything is
/// staged, then committed once. A null value removes the tenant's own row ("Use default", validation F-13): the key reads the
/// inherited value again (the platform's, else the code default); removing a row that does not exist changes nothing.
/// A client flag (<c>hideAnnouncements</c>, LIS-CR-013) is refused with <c>SET-004</c> whatever the tenant: the Super Admin sets it per client on SA-04.
/// Returns the full, effective parameter list.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Settings, PermissionAction.Manage)]
public sealed record UpdateTenantSettingsCommand(IReadOnlyList<SettingValueItem> Settings) : ICommand<TenantSettingsResponse>;

public sealed class UpdateTenantSettingsCommandValidator : AbstractValidator<UpdateTenantSettingsCommand>
{
    public UpdateTenantSettingsCommandValidator()
    {
        RuleFor(command => command.Settings).NotNull().NotEmpty().Must(settings => settings.Count <= 200).WithMessage("At most 200 settings per request.");
        RuleForEach(command => command.Settings).ChildRules(item =>
        {
            item.RuleFor(setting => setting.Key).NotEmpty().MaximumLength(SettingDescriptor.MaxKeyLength);
            item.RuleFor(setting => setting.Value).MaximumLength(SettingDescriptor.MaxValueLength);   // null = remove the override (F-13)
        });
    }
}

public sealed class UpdateTenantSettingsCommandHandler(ITenantSettingsStore store, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant currentTenant, IClock clock) : ICommandHandler<UpdateTenantSettingsCommand, TenantSettingsResponse>
{
    public async Task<TenantSettingsResponse> Handle(UpdateTenantSettingsCommand command, CancellationToken cancellationToken)
    {
        // Validate everything first (the catalogue decides, with its codes), then stage, then one commit.
        var cleaned = command.Settings
            .Select(item => (Key: item.Key.Trim(), Value: item.Value?.Trim()))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        foreach (var (key, value) in cleaned)
        {
            if (SettingKeys.IsClientFlag(key))
            {
                // LIS-CR-013: a client flag (hideAnnouncements) is the Super Admin's, set per client on SA-04 only.
                throw new DomainException(SettingCodes.SetByPlatformOnly, $"'{key}' is set by the platform for each client, not on the Parameters page.", DomainFault.Refused);
            }

            if (value is null)
            {
                _ = SettingKeys.Find(key) ?? throw new DomainException(SettingCodes.UnknownKey, $"Unknown setting '{key}'.", DomainFault.Validation);
            }
            else
            {
                SettingKeys.Validate(key, value);
            }
        }

        var now = clock.UtcNow;
        foreach (var (key, value) in cleaned)
        {
            if (value is null)
            {
                await store.RemoveAsync(key, cancellationToken);
            }
            else
            {
                await store.UpsertAsync(key, value, user.UserId, now, cancellationToken);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetTenantSettingsQueryHandler.DescribeAllAsync(store, currentTenant, cancellationToken);
    }
}
