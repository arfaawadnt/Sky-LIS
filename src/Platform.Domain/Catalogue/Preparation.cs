using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>How a preparation question is answered at registration (SRS CP-46).</summary>
public enum PreparationAnswerType
{
    Text = 0,
    YesNo = 1,
    OptionList = 2,
}

/// <summary>The patients a preparation question applies to.</summary>
public enum PreparationGender
{
    Any = 0,
    Male = 1,
    Female = 2,
}

/// <summary>
/// A question about the patient's readiness asked at registration for the ordered services (SRS CP-46; LDM §4.4
/// "precautions": fasting, medication). An empty service list means every service. Option lists need at least two
/// distinct options (INV-CTB-004).
/// </summary>
public sealed class Preparation : CatalogueBRecord
{
    public const int MaxOptions = 30;
    public const int MaxOptionLength = 100;

    private Preparation()
    {
        Code = string.Empty;
        Question = null!;
        Options = [];
        ServiceIds = [];
    }

    private Preparation(Guid id, string code)
        : base(id)
    {
        Code = code;
        Question = null!;
        Options = [];
        ServiceIds = [];
    }

    /// <summary>Unique per tenant, immutable (INV-CTB-002).</summary>
    public string Code { get; private set; }

    public LocalizedText Question { get; private set; }

    public PreparationAnswerType AnswerType { get; private set; }

    /// <summary>The allowed answers of an option list, in display order; empty for the other types.</summary>
    public string[] Options { get; private set; }

    public PreparationGender Gender { get; private set; }

    /// <summary>The services the question is asked for; empty = all services.</summary>
    public Guid[] ServiceIds { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>). Code uniqueness and the existence of the services are checked by the handler.</summary>
    public static Preparation Create(string code, LocalizedText question, PreparationAnswerType answerType, IEnumerable<string>? options, PreparationGender gender, IEnumerable<Guid>? serviceIds, DateTimeOffset nowUtc)
    {
        var preparation = new Preparation(Guid.CreateVersion7(), CatalogueBGuard.Code(code));
        preparation.Apply(question, answerType, options, gender, serviceIds);
        preparation.Raise(new PreparationCreated(preparation.Id, preparation.Code, preparation.Question.En, nowUtc));
        return preparation;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): everything but the code.</summary>
    public void Update(LocalizedText question, PreparationAnswerType answerType, IEnumerable<string>? options, PreparationGender gender, IEnumerable<Guid>? serviceIds) =>
        Apply(question, answerType, options, gender, serviceIds);

    /// <summary>Asked for this order: the patient's gender matches (or is unknown) and the question is for all services or for one of the ordered ones.</summary>
    public bool AppliesTo(IReadOnlyCollection<Guid> orderedServiceIds, PreparationGender? patientGender)
    {
        ArgumentNullException.ThrowIfNull(orderedServiceIds);
        var genderMatches = Gender == PreparationGender.Any || patientGender is null or PreparationGender.Any || patientGender == Gender;
        return genderMatches && (ServiceIds.Length == 0 || ServiceIds.Any(orderedServiceIds.Contains));
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new PreparationDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new PreparationReactivated(Id, nowUtc);

    private void Apply(LocalizedText question, PreparationAnswerType answerType, IEnumerable<string>? options, PreparationGender gender, IEnumerable<Guid>? serviceIds)
    {
        ArgumentNullException.ThrowIfNull(question);
        Require(Enum.IsDefined(answerType), CatalogueBCodes.TextInvalid, "The answer type is unknown.", DomainFault.Validation);
        Require(Enum.IsDefined(gender), CatalogueBCodes.TextInvalid, "The gender is unknown.", DomainFault.Validation);

        Question = question;
        AnswerType = answerType;
        Options = CleanOptions(answerType, options);
        Gender = gender;
        ServiceIds = CatalogueBGuard.Ids(serviceIds);
    }

    private static string[] CleanOptions(PreparationAnswerType answerType, IEnumerable<string>? options)
    {
        if (answerType != PreparationAnswerType.OptionList)
        {
            return [];
        }

        var cleaned = (options ?? []).Select(option => (option ?? string.Empty).Trim()).Where(option => option.Length > 0).ToList();
        Require(
            cleaned.All(option => option.Length <= MaxOptionLength) && cleaned.Count <= MaxOptions,
            CatalogueBCodes.PreparationOptionInvalid,
            $"An option list holds at most {MaxOptions} options of at most {MaxOptionLength} characters.",
            DomainFault.Validation);

        var distinct = cleaned.DistinctBy(option => option, StringComparer.OrdinalIgnoreCase).ToArray();
        Require(distinct.Length == cleaned.Count, CatalogueBCodes.PreparationOptionInvalid, "The options of a list must be different from each other.", DomainFault.Validation);
        Require(distinct.Length >= 2, CatalogueBCodes.PreparationOptionsRequired, "An option list needs at least two different options.", DomainFault.Validation);
        return distinct;
    }
}
