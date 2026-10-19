using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Pricing;
using Platform.Domain.Authorization;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Pricing.Commands;

// Price list tools of the editor (CR 2026-10-19 C21): the Excel import (L8) and the bulk adjustment (L7). Both are
// pricing.edit like the inline grid; neither re-prices a contract (BR-07: Apply stays explicit).

/// <summary>
/// L8 Import (<c>pricing.edit</c>): every row is checked (PRC-041 no code, PRC-015 unknown code, PRC-006 price missing or
/// invalid, PRC-016 code repeated) and the answer lists the refused rows with their codes; with any error nothing is written
/// (all-or-nothing). Without errors and <paramref name="DryRun"/> false the rows are upserted through the aggregate in one
/// save: existing lines keep their active flag, new lines are active. The SPA calls a dry run first and shows the errors.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record ImportPriceLinesCommand(Guid ListId, IReadOnlyList<ImportPriceRowRequest> Rows, bool DryRun) : ICommand<ImportPriceLinesResult>;

public sealed class ImportPriceLinesValidator : AbstractValidator<ImportPriceLinesCommand>
{
    public const int MaxRows = BasePriceList.MaxLinesPerUpsert;

    public ImportPriceLinesValidator()
    {
        RuleFor(command => command.ListId).NotEmpty();
        RuleFor(command => command.Rows).NotNull().Must(rows => rows is { Count: >= 1 and <= MaxRows }).WithMessage($"Send between 1 and {MaxRows} rows.");
        RuleForEach(command => command.Rows).ChildRules(row =>
        {
            row.RuleFor(item => item.Row).GreaterThan(0);
            row.RuleFor(item => item.ServiceCode).MaximumLength(64);
        });
    }
}

public sealed class ImportPriceLinesHandler(IBasePriceListRepository lists, IPricingServiceCatalog catalog, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ImportPriceLinesCommand, ImportPriceLinesResult>
{
    public async Task<ImportPriceLinesResult> Handle(ImportPriceLinesCommand command, CancellationToken cancellationToken)
    {
        var list = await lists.GetForUpdateAsync(command.ListId, cancellationToken)
            ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);

        var rows = command.Rows.Select(row => (row.Row, Code: CleanCode(row.ServiceCode), row.Price)).ToList();
        var known = await catalog.ServiceIdsByCodeAsync(rows.Where(row => row.Code is not null).Select(row => row.Code!).Distinct().ToList(), cancellationToken);

        var errors = new List<ImportRowError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<PriceLineInput>();
        var held = list.Lines.ToDictionary(line => line.ServiceId);
        var (added, changed) = (0, 0);
        foreach (var (number, code, price) in rows)
        {
            if (code is null)
            {
                errors.Add(new ImportRowError(number, null, PricingCodes.ImportCodeMissing, "The row has no service code."));
                continue;
            }

            if (!known.TryGetValue(code, out var serviceId))
            {
                errors.Add(new ImportRowError(number, code, PricingCodes.ServiceUnknown, $"No service of the catalogue has the code {code}."));
                continue;
            }

            if (price is null || !PriceMath.IsValidAmount(price.Value))
            {
                errors.Add(new ImportRowError(number, code, PricingCodes.PriceInvalid, $"The price must be a number between 0 and {PriceMath.MaxAmount} with at most two decimals."));
                continue;
            }

            if (!seen.Add(code))
            {
                errors.Add(new ImportRowError(number, code, PricingCodes.DuplicateServiceLine, $"The code {code} appears more than once."));
                continue;
            }

            var existing = held.GetValueOrDefault(serviceId);
            accepted.Add(new PriceLineInput(serviceId, price.Value, existing?.IsActive ?? true));
            if (existing is null)
            {
                added++;
            }
            else if (existing.Price != price.Value)
            {
                changed++;
            }
        }

        if (errors.Count > 0 || command.DryRun)
        {
            return new ImportPriceLinesResult(accepted.Count, added, changed, Applied: false, errors);
        }

        foreach (var chunk in accepted.Chunk(BasePriceList.MaxLinesPerUpsert))
        {
            list.UpsertLines(chunk, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ImportPriceLinesResult(accepted.Count, added, changed, Applied: true, errors);
    }

    /// <summary>Codes compare as the catalogue stores them: trimmed, upper-case; blank is "missing".</summary>
    private static string? CleanCode(string? code)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return cleaned.Length == 0 ? null : cleaned;
    }
}

/// <summary>
/// L7 Bulk adjust (<c>pricing.edit</c>): every line of the list, or the lines of <paramref name="ServiceIds"/>, + amount or
/// × (1 + percent) with the chosen rounding (PriceMath.Adjust: negative results clamp to 0 and are reported). <paramref name="Preview"/>
/// answers the rows without writing; otherwise the changed rows are upserted in one save.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record AdjustBasePriceListCommand(Guid ListId, string Mode, decimal Value, string Rounding, IReadOnlyList<Guid>? ServiceIds, bool Preview) : ICommand<AdjustBasePriceListResult>;

public sealed class AdjustBasePriceListValidator : AbstractValidator<AdjustBasePriceListCommand>
{
    public const int MaxScope = 10_000;

    public AdjustBasePriceListValidator()
    {
        RuleFor(command => command.ListId).NotEmpty();
        RuleFor(command => command.Mode).NotEmpty().IsEnumName(typeof(PriceAdjustMode), caseSensitive: false);
        RuleFor(command => command.Rounding).NotEmpty().IsEnumName(typeof(PriceRounding), caseSensitive: false);
        RuleFor(command => command.Value).InclusiveBetween(-PriceMath.MaxAmount, PriceMath.MaxAmount).PrecisionScale(12, PriceMath.AmountScale, ignoreTrailingZeros: true);
        RuleFor(command => command.ServiceIds).Must(ids => ids is { Count: >= 1 and <= MaxScope }).When(command => command.ServiceIds is not null)
            .WithMessage($"Select between 1 and {MaxScope} services, or none for every line.");
        RuleForEach(command => command.ServiceIds).NotEmpty().When(command => command.ServiceIds is not null);
    }
}

public sealed class AdjustBasePriceListHandler(IBasePriceListRepository lists, IPricingServiceCatalog catalog, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<AdjustBasePriceListCommand, AdjustBasePriceListResult>
{
    public async Task<AdjustBasePriceListResult> Handle(AdjustBasePriceListCommand command, CancellationToken cancellationToken)
    {
        var list = await lists.GetForUpdateAsync(command.ListId, cancellationToken)
            ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);

        var scope = command.ServiceIds is null ? null : command.ServiceIds.ToHashSet();
        var rows = list.PreviewAdjustment(Enum.Parse<PriceAdjustMode>(command.Mode, ignoreCase: true), command.Value, Enum.Parse<PriceRounding>(command.Rounding, ignoreCase: true), scope);
        var labels = await catalog.ServiceLabelsAsync(rows.Select(row => row.ServiceId).ToList(), cancellationToken);

        var applied = false;
        if (!command.Preview && rows.Any(row => row.Changed))
        {
            list.ApplyAdjustment(rows, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            applied = true;
        }

        return new AdjustBasePriceListResult(
            rows.Select(row =>
            {
                var label = labels.GetValueOrDefault(row.ServiceId);
                return new PriceAdjustmentRow(row.ServiceId, label?.Code, label?.NameEn, label?.NameAr, row.OldPrice, row.NewPrice, row.Clamped);
            }).ToList(),
            rows.Count(row => row.Changed),
            rows.Count(row => row.Clamped),
            applied);
    }
}
