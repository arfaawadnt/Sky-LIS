using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;

namespace Platform.Application.Catalogue.Commands;

// Set-up lists of Catalogue B (CP-47 comment library, CP-48 doctors, CP-50 sending labs, CP-53 reasons, CP-54 report
// templates), T1/T2 of intake LIS-INT-CTB-001. Each handler: check, load or create, call the aggregate, commit once.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateCommentTemplateCommand(string ShortName, string TextEn, string TextAr, Guid? ClinicalGroupId) : ICommand<CreatedResource>;

public sealed class CreateCommentTemplateValidator : AbstractValidator<CreateCommentTemplateCommand>
{
    public CreateCommentTemplateValidator()
    {
        RuleFor(command => command.ShortName).RequiredText(CommentTemplate.MaxShortNameLength);
        RuleFor(command => command.TextEn).RequiredText(CatalogueBGuard.MaxLongTextLength);
        RuleFor(command => command.TextAr).RequiredText(CatalogueBGuard.MaxLongTextLength);
    }
}

public sealed class CreateCommentTemplateHandler(ICommentTemplateRepository templates, ICatalogueBReferences references, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateCommentTemplateCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateCommentTemplateCommand command, CancellationToken cancellationToken)
    {
        var shortName = CatalogueBGuard.Code(command.ShortName, CommentTemplate.MaxShortNameLength);
        if (await templates.ShortNameExistsAsync(shortName, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(shortName);
        }

        await CatalogueBRules.RequireClinicalGroupAsync(references, command.ClinicalGroupId, cancellationToken);
        var template = CommentTemplate.Create(shortName, new LocalizedText(command.TextEn, command.TextAr), command.ClinicalGroupId, clock.UtcNow);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(template.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateCommentTemplateCommand(Guid CommentTemplateId, string TextEn, string TextAr, Guid? ClinicalGroupId) : ICommand;

public sealed class UpdateCommentTemplateValidator : AbstractValidator<UpdateCommentTemplateCommand>
{
    public UpdateCommentTemplateValidator()
    {
        RuleFor(command => command.CommentTemplateId).NotEmpty();
        RuleFor(command => command.TextEn).RequiredText(CatalogueBGuard.MaxLongTextLength);
        RuleFor(command => command.TextAr).RequiredText(CatalogueBGuard.MaxLongTextLength);
    }
}

public sealed class UpdateCommentTemplateHandler(ICommentTemplateRepository templates, ICatalogueBReferences references, IUnitOfWork unitOfWork) : ICommandHandler<UpdateCommentTemplateCommand>
{
    public async Task Handle(UpdateCommentTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await templates.GetForUpdateAsync(command.CommentTemplateId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.CommentTemplateNotFound, "Comment");

        await CatalogueBRules.RequireClinicalGroupAsync(references, command.ClinicalGroupId, cancellationToken);
        template.Update(new LocalizedText(command.TextEn, command.TextAr), command.ClinicalGroupId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateDoctorCommand(string Code, string NameEn, string NameAr, string? Specialty, string? Occupation, IReadOnlyList<Guid>? BranchIds, decimal SharePercent, string? Phone) : ICommand<CreatedResource>;

public sealed class CreateDoctorValidator : AbstractValidator<CreateDoctorCommand>
{
    public CreateDoctorValidator()
    {
        RuleFor(command => command.Code).RequiredText(CatalogueBGuard.MaxCodeLength);
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Specialty).MaximumLength(CatalogueBGuard.MaxFreeTextLength);
        RuleFor(command => command.Occupation).MaximumLength(CatalogueBGuard.MaxFreeTextLength);
        RuleFor(command => command.Phone).MaximumLength(CatalogueBGuard.MaxPhoneLength);
        RuleFor(command => command.SharePercent).InclusiveBetween(0m, 100m);
        RuleFor(command => command.BranchIds).IdSet();
    }
}

public sealed class CreateDoctorHandler(IDoctorRepository doctors, ICatalogueBReferences references, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateDoctorCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateDoctorCommand command, CancellationToken cancellationToken)
    {
        var code = CatalogueBGuard.Code(command.Code);
        if (await doctors.CodeExistsAsync(code, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(code);
        }

        await CatalogueBRules.RequireBranchesAsync(references, command.BranchIds, cancellationToken);
        var doctor = Doctor.Create(code, new LocalizedText(command.NameEn, command.NameAr), command.Specialty, command.Occupation, command.BranchIds, command.SharePercent, command.Phone, clock.UtcNow);
        doctors.Add(doctor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(doctor.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateDoctorCommand(Guid DoctorId, string NameEn, string NameAr, string? Specialty, string? Occupation, IReadOnlyList<Guid>? BranchIds, decimal SharePercent, string? Phone) : ICommand;

public sealed class UpdateDoctorValidator : AbstractValidator<UpdateDoctorCommand>
{
    public UpdateDoctorValidator()
    {
        RuleFor(command => command.DoctorId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Specialty).MaximumLength(CatalogueBGuard.MaxFreeTextLength);
        RuleFor(command => command.Occupation).MaximumLength(CatalogueBGuard.MaxFreeTextLength);
        RuleFor(command => command.Phone).MaximumLength(CatalogueBGuard.MaxPhoneLength);
        RuleFor(command => command.SharePercent).InclusiveBetween(0m, 100m);
        RuleFor(command => command.BranchIds).IdSet();
    }
}

public sealed class UpdateDoctorHandler(IDoctorRepository doctors, ICatalogueBReferences references, IUnitOfWork unitOfWork) : ICommandHandler<UpdateDoctorCommand>
{
    public async Task Handle(UpdateDoctorCommand command, CancellationToken cancellationToken)
    {
        var doctor = await doctors.GetForUpdateAsync(command.DoctorId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.DoctorNotFound, "Doctor");

        await CatalogueBRules.RequireBranchesAsync(references, command.BranchIds, cancellationToken);
        doctor.Update(new LocalizedText(command.NameEn, command.NameAr), command.Specialty, command.Occupation, command.BranchIds, command.SharePercent, command.Phone);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateSendingLabCommand(string NameEn, string NameAr, Guid PayerId, Guid ContractTierId, string CourierEn, string CourierAr, string? Phone, Guid? DoctorId) : ICommand<CreatedResource>;

public sealed class CreateSendingLabValidator : AbstractValidator<CreateSendingLabCommand>
{
    public CreateSendingLabValidator()
    {
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.ContractTierId).NotEmpty();
        RuleFor(command => command.CourierEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.CourierAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Phone).MaximumLength(CatalogueBGuard.MaxPhoneLength);
    }
}

public sealed class CreateSendingLabHandler(ISendingLabRepository labs, IDoctorRepository doctors, ICatalogueBReferences references, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateSendingLabCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateSendingLabCommand command, CancellationToken cancellationToken)
    {
        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await labs.NameExistsAsync(name.En, null, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        await CatalogueBRules.RequirePayerAndTierAsync(references, command.PayerId, command.ContractTierId, cancellationToken);
        var doctor = command.DoctorId is { } doctorId && doctorId != Guid.Empty
            ? await doctors.GetForUpdateAsync(doctorId, cancellationToken) ?? throw CatalogueBRules.Unknown(CatalogueBCodes.DoctorUnknown, "The doctor does not exist.")
            : null;

        var lab = SendingLab.Create(name, command.PayerId, command.ContractTierId, new LocalizedText(command.CourierEn, command.CourierAr), command.Phone, doctor, clock.UtcNow);
        labs.Add(lab);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(lab.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateSendingLabCommand(Guid SendingLabId, string NameEn, string NameAr, Guid PayerId, Guid ContractTierId, string CourierEn, string CourierAr, string? Phone, Guid? DoctorId) : ICommand;

public sealed class UpdateSendingLabValidator : AbstractValidator<UpdateSendingLabCommand>
{
    public UpdateSendingLabValidator()
    {
        RuleFor(command => command.SendingLabId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.ContractTierId).NotEmpty();
        RuleFor(command => command.CourierEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.CourierAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Phone).MaximumLength(CatalogueBGuard.MaxPhoneLength);
    }
}

public sealed class UpdateSendingLabHandler(ISendingLabRepository labs, IDoctorRepository doctors, ICatalogueBReferences references, IUnitOfWork unitOfWork) : ICommandHandler<UpdateSendingLabCommand>
{
    public async Task Handle(UpdateSendingLabCommand command, CancellationToken cancellationToken)
    {
        var lab = await labs.GetForUpdateAsync(command.SendingLabId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.SendingLabNotFound, "Sending lab");

        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await labs.NameExistsAsync(name.En, lab.Id, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        await CatalogueBRules.RequirePayerAndTierAsync(references, command.PayerId, command.ContractTierId, cancellationToken);
        var doctor = command.DoctorId is { } doctorId && doctorId != Guid.Empty
            ? await doctors.GetForUpdateAsync(doctorId, cancellationToken) ?? throw CatalogueBRules.Unknown(CatalogueBCodes.DoctorUnknown, "The doctor does not exist.")
            : null;

        lab.Update(name, command.PayerId, command.ContractTierId, new LocalizedText(command.CourierEn, command.CourierAr), command.Phone, doctor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateReasonCommand(string Kind, string NameEn, string NameAr) : ICommand<CreatedResource>;

public sealed class CreateReasonValidator : AbstractValidator<CreateReasonCommand>
{
    public CreateReasonValidator()
    {
        RuleFor(command => command.Kind).EnumName<CreateReasonCommand, ReasonKind>();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class CreateReasonHandler(IReasonRepository reasons, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateReasonCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateReasonCommand command, CancellationToken cancellationToken)
    {
        var kind = CatalogueBRules.Parse<ReasonKind>(command.Kind);
        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await reasons.NameExistsAsync(kind, name.En, null, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        var reason = Reason.Create(kind, name, clock.UtcNow);
        reasons.Add(reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(reason.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateReasonCommand(Guid ReasonId, string NameEn, string NameAr) : ICommand;

public sealed class UpdateReasonValidator : AbstractValidator<UpdateReasonCommand>
{
    public UpdateReasonValidator()
    {
        RuleFor(command => command.ReasonId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class UpdateReasonHandler(IReasonRepository reasons, IUnitOfWork unitOfWork) : ICommandHandler<UpdateReasonCommand>
{
    public async Task Handle(UpdateReasonCommand command, CancellationToken cancellationToken)
    {
        var reason = await reasons.GetForUpdateAsync(command.ReasonId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ReasonNotFound, "Reason");

        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await reasons.NameExistsAsync(reason.Kind, name.En, reason.Id, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        reason.Rename(name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateReportTemplateCommand(
    string NameEn,
    string NameAr,
    string Type,
    Guid? LogoFileId,
    Guid? SignatureFileId,
    bool ShowQr,
    string Language,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr) : ICommand<CreatedResource>;

public sealed class CreateReportTemplateValidator : AbstractValidator<CreateReportTemplateCommand>
{
    public CreateReportTemplateValidator()
    {
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Type).EnumName<CreateReportTemplateCommand, ReportTemplateType>();
        RuleFor(command => command.Language).EnumName<CreateReportTemplateCommand, ReportTemplateLanguage>();
        RuleFor(command => command.HeaderTextEn).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.HeaderTextAr));
        RuleFor(command => command.HeaderTextAr).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.HeaderTextEn));
        RuleFor(command => command.FooterTextEn).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.FooterTextAr));
        RuleFor(command => command.FooterTextAr).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.FooterTextEn));
    }
}

public sealed class CreateReportTemplateHandler(IReportTemplateRepository templates, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateReportTemplateCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateReportTemplateCommand command, CancellationToken cancellationToken)
    {
        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await templates.NameExistsAsync(name.En, null, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        var template = ReportTemplate.Create(
            name,
            CatalogueBRules.Parse<ReportTemplateType>(command.Type),
            command.LogoFileId,
            command.SignatureFileId,
            command.ShowQr,
            CatalogueBRules.Parse<ReportTemplateLanguage>(command.Language),
            CatalogueBRules.OptionalText(command.HeaderTextEn, command.HeaderTextAr),
            CatalogueBRules.OptionalText(command.FooterTextEn, command.FooterTextAr),
            clock.UtcNow);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(template.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateReportTemplateCommand(
    Guid ReportTemplateId,
    string NameEn,
    string NameAr,
    string Type,
    Guid? LogoFileId,
    Guid? SignatureFileId,
    bool ShowQr,
    string Language,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr) : ICommand;

public sealed class UpdateReportTemplateValidator : AbstractValidator<UpdateReportTemplateCommand>
{
    public UpdateReportTemplateValidator()
    {
        RuleFor(command => command.ReportTemplateId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Type).EnumName<UpdateReportTemplateCommand, ReportTemplateType>();
        RuleFor(command => command.Language).EnumName<UpdateReportTemplateCommand, ReportTemplateLanguage>();
        RuleFor(command => command.HeaderTextEn).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.HeaderTextAr));
        RuleFor(command => command.HeaderTextAr).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.HeaderTextEn));
        RuleFor(command => command.FooterTextEn).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.FooterTextAr));
        RuleFor(command => command.FooterTextAr).MaximumLength(CatalogueBGuard.MaxLongTextLength).NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.FooterTextEn));
    }
}

public sealed class UpdateReportTemplateHandler(IReportTemplateRepository templates, IUnitOfWork unitOfWork) : ICommandHandler<UpdateReportTemplateCommand>
{
    public async Task Handle(UpdateReportTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await templates.GetForUpdateAsync(command.ReportTemplateId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ReportTemplateNotFound, "Report template");

        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await templates.NameExistsAsync(name.En, template.Id, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        template.Update(
            name,
            CatalogueBRules.Parse<ReportTemplateType>(command.Type),
            command.LogoFileId,
            command.SignatureFileId,
            command.ShowQr,
            CatalogueBRules.Parse<ReportTemplateLanguage>(command.Language),
            CatalogueBRules.OptionalText(command.HeaderTextEn, command.HeaderTextAr),
            CatalogueBRules.OptionalText(command.FooterTextEn, command.FooterTextAr));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
