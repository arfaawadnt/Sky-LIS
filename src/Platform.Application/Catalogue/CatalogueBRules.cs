using FluentValidation;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue;

/// <summary>
/// The handler-side checks of Catalogue B (intake LIS-INT-CTB-001 §7–§8): uniqueness and reference refusals with
/// their codes, enum parsing after the validator has accepted the name, and optional bilingual texts.
/// </summary>
internal static class CatalogueBRules
{
    public static DomainException NotFound(string code, string what) => new(code, $"{what} not found.", DomainFault.NotFound);

    public static DomainException Unknown(string code, string message) => new(code, message, DomainFault.Validation);

    public static DomainException CodeExists(string code) => new(CatalogueBCodes.CodeExists, $"A record with the code '{code}' already exists in this list.", DomainFault.Conflict);

    public static DomainException NameExists(string name) => new(CatalogueBCodes.NameExists, $"A record named '{name}' already exists in this list.", DomainFault.Conflict);

    /// <summary>An enum value whose name the validator has already accepted (case-insensitive).</summary>
    public static TEnum Parse<TEnum>(string value)
        where TEnum : struct, Enum => Enum.Parse<TEnum>(value, ignoreCase: true);

    /// <summary>An optional enum filter: empty = none.</summary>
    public static TEnum? ParseOptional<TEnum>(string? value)
        where TEnum : struct, Enum => string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<TEnum>(value, ignoreCase: true);

    /// <summary>A header or footer: both languages or nothing (the validator refuses one language alone).</summary>
    public static LocalizedText? OptionalText(string? en, string? ar) =>
        string.IsNullOrWhiteSpace(en) && string.IsNullOrWhiteSpace(ar) ? null : new LocalizedText(en!, ar!);

    /// <summary>INV-CTB-009: every linked service is a service of the tenant (CAT-203).</summary>
    public static async Task RequireServicesAsync(ICatalogueBReferences references, IEnumerable<Guid>? serviceIds, CancellationToken cancellationToken)
    {
        var ids = (serviceIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var missing = await references.MissingServicesAsync(ids, cancellationToken).ConfigureAwait(false);
        if (missing.Count > 0)
        {
            throw new DomainException(CatalogueBCodes.ServiceUnknown, $"{missing.Count} linked service(s) do not exist in this laboratory's catalogue.", DomainFault.Validation);
        }
    }

    /// <summary>INV-CTB-009: an optional clinical group of the tenant (CAT-204).</summary>
    public static async Task RequireClinicalGroupAsync(ICatalogueBReferences references, Guid? clinicalGroupId, CancellationToken cancellationToken)
    {
        if (clinicalGroupId is not { } id || id == Guid.Empty)
        {
            return;
        }

        if (!await references.ClinicalGroupExistsAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new DomainException(CatalogueBCodes.ClinicalGroupUnknown, "The clinical group does not exist.", DomainFault.Validation);
        }
    }

    /// <summary>INV-CTB-009: every branch is a branch of the tenant (CAT-205).</summary>
    public static async Task RequireBranchesAsync(ICatalogueBReferences references, IEnumerable<Guid>? branchIds, CancellationToken cancellationToken)
    {
        var ids = (branchIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var missing = await references.MissingBranchesAsync(ids, cancellationToken).ConfigureAwait(false);
        if (missing.Count > 0)
        {
            throw new DomainException(CatalogueBCodes.BranchUnknown, $"{missing.Count} branch(es) do not exist.", DomainFault.Validation);
        }
    }

    /// <summary>INV-CTB-008/009: the payer exists (CAT-206) and the tier is one of its tiers (CAT-207).</summary>
    public static async Task RequirePayerAndTierAsync(ICatalogueBReferences references, Guid payerId, Guid contractTierId, CancellationToken cancellationToken)
    {
        if (!await references.PayerExistsAsync(payerId, cancellationToken).ConfigureAwait(false))
        {
            throw new DomainException(CatalogueBCodes.PayerUnknown, "The payer does not exist.", DomainFault.Validation);
        }

        if (!await references.ContractTierOfPayerExistsAsync(contractTierId, payerId, cancellationToken).ConfigureAwait(false))
        {
            throw new DomainException(CatalogueBCodes.ContractTierUnknown, "The contract tier does not belong to the payer.", DomainFault.Validation);
        }
    }
}

/// <summary>Shape rules shared by the validators of Catalogue B (FluentValidation; rules of the domain stay in the aggregates).</summary>
internal static class CatalogueBValidation
{
    public static IRuleBuilderOptions<T, string> RequiredText<T>(this IRuleBuilder<T, string> rule, int maxLength) =>
        rule.NotEmpty().MaximumLength(maxLength);

    public static IRuleBuilderOptions<T, string> EnumName<T, TEnum>(this IRuleBuilder<T, string> rule)
        where TEnum : struct, Enum => rule.NotEmpty().IsEnumName(typeof(TEnum), caseSensitive: false);

    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>?> IdSet<T>(this IRuleBuilder<T, IReadOnlyList<Guid>?> rule) =>
        rule.Must(ids => ids is null || (ids.Count <= CatalogueBGuard.MaxLinks && ids.All(id => id != Guid.Empty)))
            .WithMessage($"At most {CatalogueBGuard.MaxLinks} distinct, non-empty ids.");

    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>> RequiredIdSet<T>(this IRuleBuilder<T, IReadOnlyList<Guid>> rule) =>
        rule.NotNull().Must(ids => ids is not null && ids.Count is >= 1 and <= CatalogueBGuard.MaxLinks && ids.All(id => id != Guid.Empty))
            .WithMessage($"Between 1 and {CatalogueBGuard.MaxLinks} non-empty ids.");

    public static IRuleBuilderOptions<T, int?> ValidPage<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(page => page is null || page > 0).WithMessage("The page must be 1 or more.");

    public static IRuleBuilderOptions<T, int?> ValidPageSize<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(size => size is null || size is >= 1 and <= Contracts.Common.PageRequest.MaxPageSize).WithMessage($"The page size must be between 1 and {Contracts.Common.PageRequest.MaxPageSize}.");

    public static IRuleBuilderOptions<T, string?> OptionalEnumName<T, TEnum>(this IRuleBuilder<T, string?> rule)
        where TEnum : struct, Enum => rule.Must(value => string.IsNullOrEmpty(value) || Enum.GetNames<TEnum>().Contains(value, StringComparer.OrdinalIgnoreCase)).WithMessage($"Unknown value; expected one of {string.Join(", ", Enum.GetNames<TEnum>())}.");

    public static IRuleBuilderOptions<T, string?> SearchText<T>(this IRuleBuilder<T, string?> rule) => rule.MaximumLength(100);
}
