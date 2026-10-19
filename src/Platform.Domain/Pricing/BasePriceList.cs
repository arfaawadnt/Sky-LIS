using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Pricing;

/// <summary>One line to upsert into a list: the service, its price and whether it is offered (an inactive line is "not held", intake Q3).</summary>
public sealed record PriceLineInput(Guid ServiceId, decimal Price, bool IsActive);

/// <summary>The outcome of an upsert: lines added and lines whose price or flag changed.</summary>
public sealed record PriceLineChanges(int Added, int Changed);

/// <summary>
/// A named base price list (SRS CP-33, LDM "Main Price List"): one price per service. Lists are created inactive (intake
/// Q4) so that an empty list never becomes the cash default; editing prices never re-prices a contract (BR-07, Apply is
/// explicit). The lines live in <c>app.base_price_lines</c> and change only through this root.
/// </summary>
public sealed class BasePriceList : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const int MaxLinesPerUpsert = 2000;

    private readonly List<PriceLine> _lines = [];

    private BasePriceList()
    {
        Code = string.Empty;
        Name = null!;
    }

    private BasePriceList(Guid id, string code, LocalizedText name, int year, Guid? copiedFromListId)
        : base(id)
    {
        Code = code;
        Name = name;
        Year = year;
        CopiedFromListId = copiedFromListId;
        IsActive = false;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Upper-case code, unique per tenant (INV-PRC-005).</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public int Year { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The list this one was copied from (L4), for the register; no behaviour depends on it.</summary>
    public Guid? CopiedFromListId { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<PriceLine> Lines => _lines.AsReadOnly();

    /// <summary>L1 Create (<c>pricing.create</c>): an empty, inactive list.</summary>
    public static BasePriceList Create(string code, LocalizedText name, int year, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        var list = new BasePriceList(Guid.CreateVersion7(), CleanCode(code), name, RequireYear(year), copiedFromListId: null);
        list.Raise(new BasePriceListCreated(list.Id, list.Code, list.Year, null, 0, nowUtc));
        return list;
    }

    /// <summary>
    /// L4 Copy (<c>pricing.create</c>): a new inactive list whose prices are the source's × factor or + amount, rounded to
    /// the step, with the same active flags (LDM "Copy price list"). Every result must stay a valid amount (PRC-012).
    /// </summary>
    public static BasePriceList CopyFrom(BasePriceList source, string code, LocalizedText name, int year, CopyOperation operation, decimal value, decimal roundingStep, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(name);
        Require(PriceMath.RoundingSteps.Contains(roundingStep), PricingCodes.CopyOperationInvalid, "The rounding step must be 0.01, 0.25, 0.5 or 1.", DomainFault.Validation);
        Require(
            operation switch
            {
                CopyOperation.Multiply => value > 0 && value <= PriceMath.MaxFactor && decimal.Round(value, PriceMath.FactorScale) == value,
                CopyOperation.Add => Math.Abs(value) <= PriceMath.MaxAmount && decimal.Round(value, PriceMath.AmountScale) == value,
                _ => false,
            },
            PricingCodes.CopyOperationInvalid,
            "Multiply by a factor above 0 and up to 10, or add an amount with at most two decimals.",
            DomainFault.Validation);

        var list = new BasePriceList(Guid.CreateVersion7(), CleanCode(code), name, RequireYear(year), source.Id);
        foreach (var line in source._lines)
        {
            var raw = operation == CopyOperation.Multiply ? line.Price * value : line.Price + value;
            var price = PriceMath.RoundToStep(raw, roundingStep);
            Require(PriceMath.IsValidAmount(price), PricingCodes.CopyResultOutOfRange, $"The copy would price a service at {price}; prices must be between 0 and {PriceMath.MaxAmount}.", DomainFault.Validation);
            list._lines.Add(new PriceLine(list.Id, line.ServiceId, price, line.IsActive));
        }

        list.Raise(new BasePriceListCreated(list.Id, list.Code, list.Year, source.Id, list._lines.Count, nowUtc));
        return list;
    }

    /// <summary>L2 Edit (<c>pricing.edit</c>): name and year; the code never changes. Raises <see cref="BasePriceListUpdated"/> when something changed (validation v0.5.0 O-5).</summary>
    public void Update(LocalizedText name, int year, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        var newYear = RequireYear(year);
        if (newYear == Year && Name.Equals(name))
        {
            return;
        }

        Year = newYear;
        Name = name;
        Raise(new BasePriceListUpdated(Id, Code, Year, nowUtc));
    }

    /// <summary>
    /// L3 Upsert lines (<c>pricing.edit</c>): adds services the list does not hold and updates the others; nothing is
    /// removed (a line is deactivated instead). Raises <see cref="BasePriceListPricesChanged"/> only when something changed.
    /// The handler has already checked that every service exists in the catalogue (PRC-015).
    /// </summary>
    public PriceLineChanges UpsertLines(IReadOnlyList<PriceLineInput> lines, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Require(lines.Count is >= 1 and <= MaxLinesPerUpsert, PricingCodes.PriceInvalid, $"Send between 1 and {MaxLinesPerUpsert} lines.", DomainFault.Validation);
        Require(lines.Select(line => line.ServiceId).Distinct().Count() == lines.Count, PricingCodes.DuplicateServiceLine, "A service appears more than once.", DomainFault.Validation);
        foreach (var input in lines)
        {
            Require(input.ServiceId != Guid.Empty, PricingCodes.ServiceUnknown, "A line needs a service.", DomainFault.Validation);
            PriceMath.RequireAmount(input.Price, PricingCodes.PriceInvalid, $"A price must be between 0 and {PriceMath.MaxAmount} with at most two decimals.");
        }

        var byService = _lines.ToDictionary(line => line.ServiceId);
        var (added, changed) = (0, 0);
        foreach (var input in lines)
        {
            if (byService.TryGetValue(input.ServiceId, out var existing))
            {
                if (existing.Set(input.Price, input.IsActive))
                {
                    changed++;
                }
            }
            else
            {
                _lines.Add(new PriceLine(Id, input.ServiceId, input.Price, input.IsActive));
                added++;
            }
        }

        if (added + changed > 0)
        {
            Raise(new BasePriceListPricesChanged(Id, Code, added, changed, nowUtc));
        }

        return new PriceLineChanges(added, changed);
    }

    /// <summary>
    /// L7 Bulk adjust, preview (<c>pricing.edit</c>, CR 2026-10-19 C21): every line, or the lines of <paramref name="scope"/>, priced
    /// + amount or × (1 + percent / 100) and rounded as asked (<see cref="PriceMath.Adjust"/>); a negative result becomes 0 and is
    /// reported. Pure: nothing changes until <see cref="ApplyAdjustment"/>. A scope id the list does not hold is refused (PRC-045).
    /// </summary>
    public IReadOnlyList<PriceAdjustment> PreviewAdjustment(PriceAdjustMode mode, decimal value, PriceRounding rounding, IReadOnlySet<Guid>? scope)
    {
        PriceMath.RequireAdjustment(mode, value);
        if (scope is not null)
        {
            var held = _lines.Select(line => line.ServiceId).ToHashSet();
            Require(scope.All(held.Contains), PricingCodes.AdjustScopeUnknown, "A selected service is not on the list.", DomainFault.Validation);
        }

        return _lines
            .Where(line => scope is null || scope.Contains(line.ServiceId))
            .Select(line =>
            {
                var adjusted = PriceMath.Adjust(line.Price, mode, value, rounding);
                return new PriceAdjustment(line.ServiceId, line.Price, adjusted.Price, line.IsActive, adjusted.Clamped);
            })
            .ToList();
    }

    /// <summary>L7 Bulk adjust, apply: the rows of a preview whose price changed, through <see cref="UpsertLines"/> (one event per chunk of <see cref="MaxLinesPerUpsert"/>).</summary>
    public PriceLineChanges ApplyAdjustment(IReadOnlyList<PriceAdjustment> rows, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var inputs = rows.Where(row => row.Changed).Select(row => new PriceLineInput(row.ServiceId, row.NewPrice, row.IsActive)).ToList();
        var (added, changed) = (0, 0);
        foreach (var chunk in inputs.Chunk(MaxLinesPerUpsert))
        {
            var outcome = UpsertLines(chunk, nowUtc);
            added += outcome.Added;
            changed += outcome.Changed;
        }

        return new PriceLineChanges(added, changed);
    }

    /// <summary>L5 Activate (<c>pricing.manage</c>). Idempotent.</summary>
    public void Activate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new BasePriceListActivated(Id, Code, nowUtc));
    }

    /// <summary>L5 Deactivate (<c>pricing.manage</c>). Idempotent; derived contract prices stay until the next Apply.</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new BasePriceListDeactivated(Id, Code, nowUtc));
    }

    /// <summary>What Apply reads from this list: the active flag and the prices of the active lines.</summary>
    public PriceListSnapshot Snapshot() =>
        new(Id, Code, IsActive, _lines.Where(line => line.IsActive).ToDictionary(line => line.ServiceId, line => line.Price));

    public static string CleanCode(string? code) =>
        PriceMath.CleanKey(code, MaxCodeLength, PricingCodes.ListCodeInvalid, $"The code must be 1 to {MaxCodeLength} characters: a letter or digit, then letters, digits, '.', '-' or '_'.");

    private static int RequireYear(int year)
    {
        Require(year is >= MinYear and <= MaxYear, PricingCodes.YearInvalid, $"The year must be between {MinYear} and {MaxYear}.", DomainFault.Validation);
        return year;
    }
}

/// <summary>The price of one service in a base list. Changed only through <see cref="BasePriceList"/>.</summary>
public sealed class PriceLine : Entity, ITenantScoped
{
    private PriceLine()
    {
    }

    internal PriceLine(Guid priceListId, Guid serviceId, decimal price, bool isActive)
        : base(Guid.CreateVersion7())
    {
        PriceListId = priceListId;
        ServiceId = serviceId;
        Price = price;
        IsActive = isActive;
    }

    public Guid TenantId { get; private set; }

    public Guid PriceListId { get; private set; }

    /// <summary>A catalogue service (<c>app.services</c>), referenced by id.</summary>
    public Guid ServiceId { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>Offered at ordering; an inactive line is ignored by Apply and by the cash default list (intake Q3).</summary>
    public bool IsActive { get; private set; }

    /// <summary>Returns true when the price or the flag changed.</summary>
    internal bool Set(decimal price, bool isActive)
    {
        if (Price == price && IsActive == isActive)
        {
            return false;
        }

        Price = price;
        IsActive = isActive;
        return true;
    }
}

/// <summary>A list as Apply sees it: whether it is active and the price of every active line.</summary>
public sealed record PriceListSnapshot(Guid ListId, string Code, bool IsActive, IReadOnlyDictionary<Guid, decimal> Prices);

/// <summary>One line of a bulk adjustment (C21): the price before and after, the line's active flag and whether a negative result was clamped to 0.</summary>
public sealed record PriceAdjustment(Guid ServiceId, decimal OldPrice, decimal NewPrice, bool IsActive, bool Clamped)
{
    public bool Changed => OldPrice != NewPrice;
}
