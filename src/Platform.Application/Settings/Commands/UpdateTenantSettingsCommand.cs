using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Settings.Queries;
using Platform.Contracts.Settings;
using Platform.Domain.Authorization;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Settings.Commands;

/// <summary>
/// SRS CP-57 "Toggle; edit": stores one or more parameter values. Every key must be catalogued (<c>SET-001</c>) and
/// every value well-formed for its type and range (<c>SET-002</c>); the whole request is refused before anything is
/// staged, then committed once. Returns the full, effective parameter list.
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
            item.RuleFor(setting => setting.Value).NotNull().MaximumLength(SettingDescriptor.MaxValueLength);
        });
    }
}

public sealed class UpdateTenantSettingsCommandHandler(ITenantSettingsStore store, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant currentTenant, IClock clock) : ICommandHandler<UpdateTenantSettingsCommand, TenantSettingsResponse>
{
    public async Task<TenantSettingsResponse> Handle(UpdateTenantSettingsCommand command, CancellationToken cancellationToken)
    {
        // Validate everything first (the catalogue decides, with its codes), then stage, then one commit.
        var cleaned = command.Settings
            .Select(item => (item.Key.Trim(), Value: item.Value.Trim()))
            .GroupBy(item => item.Item1, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        foreach (var (key, value) in cleaned)
        {
            SettingKeys.Validate(key, value);
        }

        var now = clock.UtcNow;
        foreach (var (key, value) in cleaned)
        {
            await store.UpsertAsync(key, value, user.UserId, now, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetTenantSettingsQueryHandler.DescribeAllAsync(store, currentTenant, cancellationToken);
    }
}
