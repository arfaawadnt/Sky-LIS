using FluentValidation;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Settings;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// SA-22 Announcements (LIS-CR-013, C-17): create, edit, activate / deactivate, image upload / removal — platform.manage — and the
// SA-04 opt-out of one client (tenancy.edit). Every handler checks the platform tenant first (INV-PLT-070) and commits once.

[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.Manage)]
public sealed record CreateAnnouncementCommand(AnnouncementRequest Announcement) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator() => RuleFor(command => command.Announcement).NotNull().SetValidator(new AnnouncementRequestValidator());
}

public sealed class CreateAnnouncementHandler(IAnnouncementRepository announcements, IAnnouncementAudienceDirectory directory, ICurrentTenant currentTenant, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateAnnouncementCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var input = await AnnouncementInput.ReadAsync(command.Announcement, directory, cancellationToken);
        var announcement = Announcement.Create(input.Content, input.Placement, input.Schedule, input.Audience);
        announcements.Add(announcement);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(announcement.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.Manage)]
public sealed record UpdateAnnouncementCommand(Guid AnnouncementId, AnnouncementRequest Announcement) : ICommand, IPlatformAdministrationRequest;

public sealed class UpdateAnnouncementValidator : AbstractValidator<UpdateAnnouncementCommand>
{
    public UpdateAnnouncementValidator()
    {
        RuleFor(command => command.AnnouncementId).NotEmpty();
        RuleFor(command => command.Announcement).NotNull().SetValidator(new AnnouncementRequestValidator());
    }
}

public sealed class UpdateAnnouncementHandler(IAnnouncementRepository announcements, IAnnouncementAudienceDirectory directory, ICurrentTenant currentTenant, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateAnnouncementCommand>
{
    public async Task Handle(UpdateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var announcement = await announcements.GetForUpdateAsync(command.AnnouncementId, cancellationToken) ?? throw AnnouncementInput.NotFound();
        var input = await AnnouncementInput.ReadAsync(command.Announcement, directory, cancellationToken);
        announcement.Update(input.Content, input.Placement, input.Schedule, input.Audience);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Activate (ANN-010 once the schedule has ended) or deactivate; both idempotent.</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.Manage)]
public sealed record SetAnnouncementActiveCommand(Guid AnnouncementId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetAnnouncementActiveValidator : AbstractValidator<SetAnnouncementActiveCommand>
{
    public SetAnnouncementActiveValidator() => RuleFor(command => command.AnnouncementId).NotEmpty();
}

public sealed class SetAnnouncementActiveHandler(IAnnouncementRepository announcements, ICurrentTenant currentTenant, IUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<SetAnnouncementActiveCommand>
{
    public async Task Handle(SetAnnouncementActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var announcement = await announcements.GetForUpdateAsync(command.AnnouncementId, cancellationToken) ?? throw AnnouncementInput.NotFound();
        if (command.Active)
        {
            announcement.Activate(clock.UtcNow);
        }
        else
        {
            announcement.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Uploads the image of an announcement, or replaces it. <paramref name="Content"/> is the uploaded stream (the controller owns
/// and disposes it); the signature decides the type (PNG, JPEG, WebP ≤ 512 KB), never the client's name or content type. The
/// bytes go to the platform tenant's file store, area <c>announcements</c>, below the announcement.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.Manage)]
public sealed record UploadAnnouncementImageCommand(Guid AnnouncementId, Stream Content) : ICommand<AnnouncementImageView>, IPlatformAdministrationRequest;

public sealed class UploadAnnouncementImageValidator : AbstractValidator<UploadAnnouncementImageCommand>
{
    public UploadAnnouncementImageValidator()
    {
        RuleFor(command => command.AnnouncementId).NotEmpty();
        RuleFor(command => command.Content).NotNull();
    }
}

public sealed class UploadAnnouncementImageHandler(IAnnouncementRepository announcements, IFileStore files, ICurrentTenant currentTenant, IUnitOfWork unitOfWork)
    : ICommandHandler<UploadAnnouncementImageCommand, AnnouncementImageView>
{
    /// <summary>The store's sub-folder of announcement images (in the platform tenant).</summary>
    public const string StorageArea = "announcements";

    public async Task<AnnouncementImageView> Handle(UploadAnnouncementImageCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var announcement = await announcements.GetForUpdateAsync(command.AnnouncementId, cancellationToken) ?? throw AnnouncementInput.NotFound();

        // Buffer at most 512 KB + 1 byte: the size and the signature are decided before anything reaches the store.
        await using var buffer = new MemoryStream();
        await BoundedCopy.CopyAsync(command.Content, buffer, Announcement.MaxImageBytes + 1, cancellationToken);
        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, AnnouncementImages.HeaderLength)).ToArray();
        var image = AnnouncementImages.Inspect(buffer.Length, header);

        buffer.Position = 0;
        var stored = await files.SaveAsync(StorageArea, announcement.Id, buffer, Announcement.MaxImageBytes, cancellationToken);
        try
        {
            announcement.AttachImage(image, stored.StorageKey);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new AnnouncementImageView(announcement.ImageVersion, image.ContentType, image.SizeBytes);
        }
        catch
        {
            await files.DeleteAsync(stored.StorageKey, CancellationToken.None);   // compensation: no orphan content without its row
            throw;
        }
    }
}

/// <summary>Removes the image of an announcement (ANN-011 when it has none); the stored file is kept (BR-15).</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.Manage)]
public sealed record RemoveAnnouncementImageCommand(Guid AnnouncementId) : ICommand, IPlatformAdministrationRequest;

public sealed class RemoveAnnouncementImageValidator : AbstractValidator<RemoveAnnouncementImageCommand>
{
    public RemoveAnnouncementImageValidator() => RuleFor(command => command.AnnouncementId).NotEmpty();
}

public sealed class RemoveAnnouncementImageHandler(IAnnouncementRepository announcements, ICurrentTenant currentTenant, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveAnnouncementImageCommand>
{
    public async Task Handle(RemoveAnnouncementImageCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var announcement = await announcements.GetForUpdateAsync(command.AnnouncementId, cancellationToken) ?? throw AnnouncementInput.NotFound();
        announcement.RemoveImage();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// SA-04 "Hide announcements for this client" (decision D-4): writes the client's own <c>hideAnnouncements</c> row in the
/// client's tenant scope (RLS, no elevation; the row is stamped with the client's tenant at commit). CLI-404 when the id names
/// no client record (the platform tenant has none).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Edit)]
public sealed record SetClientAnnouncementsHiddenCommand(Guid ClientId, bool Hidden) : ICommand, IPlatformAdministrationRequest;

public sealed class SetClientAnnouncementsHiddenValidator : AbstractValidator<SetClientAnnouncementsHiddenCommand>
{
    public SetClientAnnouncementsHiddenValidator() => RuleFor(command => command.ClientId).NotEmpty();
}

public sealed class SetClientAnnouncementsHiddenHandler(
    ITenantRepository tenants,
    ITenantSettingsStore settings,
    ICurrentTenantSetter tenantScope,
    ICurrentTenant currentTenant,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SetClientAnnouncementsHiddenCommand>
{
    public async Task Handle(SetClientAnnouncementsHiddenCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        if (TenantIds.IsPlatform(command.ClientId) || await tenants.FindStatusAsync(command.ClientId, cancellationToken) is null)
        {
            throw new DomainException(PlatformAdminCodes.ClientNotFound, "Client not found.", DomainFault.NotFound);
        }

        var value = command.Hidden ? "true" : "false";
        using (tenantScope.ForTenant(command.ClientId))
        {
            await settings.UpsertAsync(SettingKeys.HideAnnouncements.Key, value, user.UserId, clock.UtcNow, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>The shape rules of <see cref="AnnouncementRequest"/> (the aggregate and <see cref="AnnouncementInput"/> decide the rest).</summary>
public sealed class AnnouncementRequestValidator : AbstractValidator<AnnouncementRequest>
{
    public AnnouncementRequestValidator()
    {
        RuleFor(request => request.TitleEn).NotEmpty().MaximumLength(Announcement.MaxTitleLength);
        RuleFor(request => request.TitleAr).NotEmpty().MaximumLength(Announcement.MaxTitleLength);
        RuleFor(request => request.BodyEn).MaximumLength(Announcement.MaxBodyLength);
        RuleFor(request => request.BodyAr).MaximumLength(Announcement.MaxBodyLength);
        RuleFor(request => request.LinkUrl).MaximumLength(Announcement.MaxLinkLength);
        RuleFor(request => request.Placement).NotEmpty().IsEnumName(typeof(AnnouncementPlacement), caseSensitive: false);
        RuleFor(request => request.AudienceStatus).IsEnumName(typeof(AnnouncementAudienceStatus), caseSensitive: false).When(request => !string.IsNullOrWhiteSpace(request.AudienceStatus));
        RuleFor(request => request.AudienceClientIds).Must(ids => ids is null || ids.Count <= Announcement.MaxClients)
            .WithMessage($"At most {Announcement.MaxClients} clients.");
    }
}

/// <summary>Turns a request into the aggregate's value objects and checks that the audience names known records (ANN-012…014).</summary>
internal static class AnnouncementInput
{
    public sealed record Parsed(AnnouncementContent Content, AnnouncementPlacement Placement, AnnouncementSchedule Schedule, AnnouncementAudience Audience);

    public static async Task<Parsed> ReadAsync(AnnouncementRequest request, IAnnouncementAudienceDirectory directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = string.IsNullOrWhiteSpace(request.AudienceStatus)
            ? AnnouncementAudienceStatus.Any
            : Enum.Parse<AnnouncementAudienceStatus>(request.AudienceStatus, ignoreCase: true);
        var clients = (request.AudienceClientIds ?? []).Distinct().ToList();

        if (request.AudienceCountryId is { } countryId && !await directory.CountryExistsAsync(countryId, cancellationToken))
        {
            throw new DomainException(AnnouncementCodes.AudienceCountryNotFound, "The audience country does not exist.", DomainFault.Validation);
        }

        if (request.AudiencePlanId is { } planId && !await directory.PlanExistsAsync(planId, cancellationToken))
        {
            throw new DomainException(AnnouncementCodes.AudiencePlanNotFound, "The audience plan does not exist.", DomainFault.Validation);
        }

        if (clients.Count > 0 && !clients.Contains(Guid.Empty))
        {
            var missing = await directory.MissingClientsAsync(clients, cancellationToken);
            if (missing.Count > 0)
            {
                throw new DomainException(AnnouncementCodes.AudienceClientNotFound, $"{missing.Count} audience client(s) do not exist.", DomainFault.Validation);
            }
        }

        return new Parsed(
            new AnnouncementContent(request.TitleEn, request.TitleAr, request.BodyEn, request.BodyAr, request.LinkUrl),
            Enum.Parse<AnnouncementPlacement>(request.Placement, ignoreCase: true),
            new AnnouncementSchedule(request.FromUtc, request.ToUtc),
            new AnnouncementAudience(request.AudienceCountryId, request.AudiencePlanId, status, clients));
    }

    public static DomainException NotFound() => new(AnnouncementCodes.AnnouncementNotFound, "Announcement not found.", DomainFault.NotFound);
}

/// <summary>Copies at most <c>limit</c> bytes (an upload larger than the limit is detected by its length afterwards).</summary>
internal static class BoundedCopy
{
    public static async Task CopyAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while (total < limit && (read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - total)), cancellationToken)) > 0)
        {
            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
        }
    }
}
