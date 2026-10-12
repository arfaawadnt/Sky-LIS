using FluentValidation;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Account;
using Platform.Domain.Account;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account.Commands;

// The laboratory logo (top bar): upload / replace and remove, by a holder of settings.manage of a laboratory.

/// <summary>
/// Uploads the laboratory's logo, or replaces it. <paramref name="Content"/> is the uploaded stream (the controller owns
/// and disposes it); the client's file name and content type are not used: the signature decides (PNG, JPEG, WebP).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Settings, PermissionAction.Manage)]
public sealed record UploadLaboratoryLogoCommand(Stream Content) : ICommand<LaboratoryLogoView>;

public sealed class UploadLaboratoryLogoValidator : AbstractValidator<UploadLaboratoryLogoCommand>
{
    public UploadLaboratoryLogoValidator()
    {
        RuleFor(command => command.Content).NotNull();
    }
}

public sealed class UploadLaboratoryLogoHandler(
    ILaboratoryLogoRepository logos,
    IFileStore files,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    ICurrentTenant tenant,
    IClock clock) : ICommandHandler<UploadLaboratoryLogoCommand, LaboratoryLogoView>
{
    /// <summary>The store's sub-folder of the module.</summary>
    public const string StorageArea = "account";

    public async Task<LaboratoryLogoView> Handle(UploadLaboratoryLogoCommand command, CancellationToken cancellationToken)
    {
        var actor = AccountActor.Require(user);
        var laboratoryId = LaboratoryScope.Require(tenant);

        // Buffer at most 512 KB + 1 byte: the size and the signature are decided before anything reaches the store.
        await using var buffer = new MemoryStream();
        await CopyBoundedAsync(command.Content, buffer, LaboratoryLogoFiles.MaxSizeBytes + 1, cancellationToken);
        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, LaboratoryLogoFiles.HeaderLength)).ToArray();
        var file = LaboratoryLogoFiles.Inspect(buffer.Length, header);

        buffer.Position = 0;
        var stored = await files.SaveAsync(StorageArea, laboratoryId, buffer, LaboratoryLogoFiles.MaxSizeBytes, cancellationToken);
        try
        {
            var logo = await logos.GetCurrentForUpdateAsync(cancellationToken);
            if (logo is null)
            {
                logo = LaboratoryLogo.Upload(file, stored.StorageKey, stored.Sha256, actor, clock.UtcNow);
                logos.Add(logo);
            }
            else
            {
                logo.Replace(file, stored.StorageKey, stored.Sha256, actor, clock.UtcNow);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new LaboratoryLogoView(logo.Version, logo.ContentType, logo.SizeBytes);
        }
        catch
        {
            await files.DeleteAsync(stored.StorageKey, CancellationToken.None);   // compensation: no orphan content without its row
            throw;
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
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

/// <summary>Removes the laboratory's logo (ACC-045 when there is none): the top bar shows the initials again.</summary>
[RequirePermissionPolicy(PermissionCatalog.Settings, PermissionAction.Manage)]
public sealed record RemoveLaboratoryLogoCommand : ICommand;

public sealed class RemoveLaboratoryLogoHandler(
    ILaboratoryLogoRepository logos,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    ICurrentTenant tenant,
    IClock clock) : ICommandHandler<RemoveLaboratoryLogoCommand>
{
    public async Task Handle(RemoveLaboratoryLogoCommand command, CancellationToken cancellationToken)
    {
        var actor = AccountActor.Require(user);
        LaboratoryScope.Require(tenant);
        var logo = await logos.GetCurrentForUpdateAsync(cancellationToken)
            ?? throw new DomainException(AccountCodes.LogoNotFound, "The laboratory has no logo.", DomainFault.NotFound);
        logo.Remove(actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A logo belongs to a laboratory: a resolved tenant that is not the platform tenant (ACC-403).</summary>
public static class LaboratoryScope
{
    public static Guid Require(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.TenantId is { } id && !TenantIds.IsPlatform(id)
            ? id
            : throw new DomainException(AccountCodes.LaboratoryRequired, "Only a laboratory has a logo.", DomainFault.Refused);
    }
}
