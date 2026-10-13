using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

/// <summary>
/// A visit (accession) of one patient at one branch on one contract (SRS CP-06, intake LIS-INT-REG-001). The root of
/// its lines, tubes, preparation answers and transfer log: every change passes through a method here that guards the
/// state, applies BR-02 (tubes), BR-05 (Reserved), BR-15 (nothing erased) and the discount rules, recomputes the
/// totals of MODULES.md §5 and raises the shared events of <c>RegistrationEvents.cs</c>. Every transition increments
/// <see cref="Revision"/>, so the root row (and its xmin token) changes with any child and concurrent edits serialise.
/// </summary>
public sealed class Visit : AggregateRoot, ITenantScoped
{
    public const int MaxAccessionLength = 20;
    public const int MaxTreatmentNoLength = 50;
    public const int MaxInsuranceIdLength = 50;
    public const int MaxLines = 200;
    public const int MaxReasonLength = 1000;
    public const int MaxUtcOffsetMinutes = 840;
    public const string AccessionKind = "accession";
    public const string AccessionPrefix = "ACC";
    public const string SampleKind = "sample";
    public const string SamplePrefix = "SMP";

    /// <summary>Service types that need a sample (CAT-028/029: Package and Additional carry none).</summary>
    public static readonly IReadOnlySet<string> SampleCarryingTypes = new HashSet<string>(StringComparer.Ordinal) { "Test", "Profile", "Culture", "TextReport" };

    private readonly List<VisitLine> _lines = [];
    private readonly List<VisitTube> _tubes = [];
    private readonly List<VisitPreparationAnswer> _answers = [];
    private readonly List<VisitTransfer> _transfers = [];

    private Visit()
    {
        Accession = string.Empty;
    }

    private Visit(VisitRegistration registration, Guid actorUserId, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        Accession = registration.Accession.Trim();
        PatientId = registration.PatientId;
        BranchId = registration.BranchId;
        LocationType = registration.LocationType;
        TreatmentNo = Blank(registration.TreatmentNo);
        ReferringDoctorId = registration.ReferringDoctorId;
        PayerId = registration.PayerId;
        ContractTierId = registration.ContractTierId;
        InsuranceId = Blank(registration.InsuranceId);
        DiagnosisId = registration.DiagnosisId;
        BookingId = registration.BookingId;
        BatchId = registration.BatchId;
        Urgent = registration.Urgent;
        Biohazard = registration.Biohazard;
        CollectedOutside = registration.CollectedOutside;
        UtcOffsetMinutes = registration.UtcOffsetMinutes;
        Status = VisitStatus.Open;
        RegisteredAtUtc = nowUtc;
        RegisteredByUserId = actorUserId;
        Revision = 1;
    }

    public Guid TenantId { get; private set; }

    /// <summary><c>ACC-yyyy-nnnnn</c> from the per-tenant counter; unique; never changes (BR-01).</summary>
    public string Accession { get; private set; }

    public Guid PatientId { get; private set; }

    /// <summary>The file the visit was registered on, kept when a merge re-points the visit (BR-15).</summary>
    public Guid? OriginalPatientId { get; private set; }

    public Guid BranchId { get; private set; }

    public VisitLocationType LocationType { get; private set; }

    public string? TreatmentNo { get; private set; }

    public Guid? ReferringDoctorId { get; private set; }

    public Guid PayerId { get; private set; }

    public Guid ContractTierId { get; private set; }

    public string? InsuranceId { get; private set; }

    public Guid? DiagnosisId { get; private set; }

    public Guid? BookingId { get; private set; }

    public Guid? BatchId { get; private set; }

    public bool Urgent { get; private set; }

    public bool Biohazard { get; private set; }

    /// <summary>Lab-to-lab "sample collected inside the lab" unticked: printed on the job order (LDM §4.9).</summary>
    public bool CollectedOutside { get; private set; }

    /// <summary>The registering desk's offset from UTC in minutes: test days (BR-13) are counted on its local days.</summary>
    public int UtcOffsetMinutes { get; private set; }

    public VisitStatus Status { get; private set; }

    public DateTimeOffset RegisteredAtUtc { get; private set; }

    public Guid RegisteredByUserId { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public decimal TotalListPrice { get; private set; }

    public decimal DiscountTotal { get; private set; }

    /// <summary>Net of the discount, Ordered + Reserved lines (MODULES.md §5).</summary>
    public decimal PatientShareTotal { get; private set; }

    public decimal PayerShareTotal { get; private set; }

    public DiscountKind? DiscountKind { get; private set; }

    public decimal DiscountValue { get; private set; }

    public Guid? DiscountReasonId { get; private set; }

    public string? DiscountReason { get; private set; }

    public Guid? DiscountByUserId { get; private set; }

    public DateTimeOffset? DiscountAtUtc { get; private set; }

    /// <summary>Incremented by every transition (the root row changes with any child change).</summary>
    public int Revision { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyList<VisitLine> Lines => _lines;

    public IReadOnlyList<VisitTube> Tubes => _tubes;

    public IReadOnlyList<VisitPreparationAnswer> PreparationAnswers => _answers;

    public IReadOnlyList<VisitTransfer> Transfers => _transfers;

    /// <summary>The service ids of the live lines, package members included (INV-REG-002).</summary>
    public IReadOnlySet<Guid> LiveServiceIds => _lines.Where(line => line.IsLive).Select(line => line.ServiceId).ToHashSet();

    /// <summary>
    /// The tests already ordered on the live lines, each with the service that orders it (validation O-3, test-level
    /// duplicates): the line's code, or "PACKAGE › MEMBER" for a package member. The first holder wins.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> LiveTestHolders()
    {
        var holders = new Dictionary<Guid, string>();
        foreach (var line in _lines.Where(line => line.IsLive).OrderBy(line => line.Sequence))
        {
            var parent = line.ParentLineId is { } parentId ? _lines.FirstOrDefault(candidate => candidate.Id == parentId) : null;
            var holder = parent is null ? line.ServiceCode : $"{parent.ServiceCode} › {line.ServiceCode}";
            foreach (var testId in line.TestIds)
            {
                holders.TryAdd(testId, holder);
            }
        }

        return holders;
    }

    /// <summary>V1 Register (<c>registration.create</c>): header checks, lines, tubes (BR-02), answers, discount, totals; raises <see cref="VisitRegistered"/>.</summary>
    public static Visit Register(
        VisitRegistration registration,
        IReadOnlyList<VisitOrderItem> items,
        IReadOnlyList<PreparationAnswerInput> answers,
        DiscountRequest? discount,
        SampleNumberSource sampleNumbers,
        Guid actorUserId,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(sampleNumbers);
        CheckHeader(registration);
        Require(actorUserId != Guid.Empty, RegistrationCodes.HeaderInvalid, "A registration needs an actor.", DomainFault.Validation);
        Require(items.Count > 0, RegistrationCodes.LinesRequired, "Add at least one service.", DomainFault.Validation);
        Require(items.Count <= MaxLines, RegistrationCodes.TooManyLines, $"A visit holds at most {MaxLines} services.", DomainFault.Validation);

        var visit = new Visit(registration, actorUserId, nowUtc);
        var added = visit.AddItems(items, registration.CollectorUserId, actorUserId, nowUtc);
        var tubes = visit.AssignTubes(added, sampleNumbers, nowUtc);
        visit.Answer(answers, actorUserId, nowUtc);
        if (discount is not null)
        {
            visit.ApplyDiscountRequest(discount, actorUserId, nowUtc);
        }

        visit.Recalculate();
        visit.Raise(new VisitRegistered(
            visit.Id,
            visit.Accession,
            visit.BranchId,
            visit.PatientId,
            visit.PayerId,
            visit.ContractTierId,
            visit.Urgent,
            actorUserId,
            [.. tubes.Select(visit.ToRegisteredTube)],
            [.. added.Select(pair => ToRegisteredLine(pair.Line))],
            nowUtc));
        if (discount is not null)
        {
            visit.RaiseDiscountChanged(actorUserId, discount.Reason, nowUtc);
        }

        return visit;
    }

    /// <summary>How many new tubes (and sample numbers) <paramref name="tubeNeeds"/> require on this visit (the handler issues the numbers first).</summary>
    public int CountNewTubes(IEnumerable<TubeSpec?> tubeNeeds) =>
        TubePlanner.CountNewTubes(ExistingTubes(), [.. (tubeNeeds ?? []).Select(spec => spec?.Key)]);

    /// <summary>The tube needs of items about to be ordered: one per Ordered sample-carrying line (members of an Ordered package included).</summary>
    public static IReadOnlyList<TubeSpec?> TubeNeedsOf(IEnumerable<VisitOrderItem> items) =>
        [.. (items ?? [])
            .Where(item => !item.Reserved)
            .SelectMany(item => item.Kind == VisitLineKind.Package ? item.Members : [item])
            .Select(item => SampleCarryingTypes.Contains(item.ServiceType) ? item.Tube : null)];

    /// <summary>V2 Add services (<c>registration.edit</c>): Open only; no service already live (INV-REG-002); tubes join live tubes of their key (BR-02).</summary>
    public void AddLines(IReadOnlyList<VisitOrderItem> items, IReadOnlyList<PreparationAnswerInput> answers, SampleNumberSource sampleNumbers, Guid actorUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(sampleNumbers);
        RequireOpen("add services");
        Require(items.Count > 0, RegistrationCodes.LinesRequired, "Add at least one service.", DomainFault.Validation);
        Require(_lines.Count(line => line.IsLive && line.IsTopLevel) + items.Count <= MaxLines, RegistrationCodes.TooManyLines, $"A visit holds at most {MaxLines} services.", DomainFault.Validation);

        var added = AddItems(items, null, actorUserId, nowUtc);
        var touched = AssignTubes(added, sampleNumbers, nowUtc);
        Answer(answers, actorUserId, nowUtc);
        Recalculate();
        Revision++;
        Raise(new VisitLinesAdded(Id, Accession, BranchId, PatientId, actorUserId, [.. touched.Select(ToRegisteredTube)], [.. added.Select(pair => ToRegisteredLine(pair.Line))], nowUtc));
    }

    /// <summary>V3 Release Reserved services (<c>registration.edit</c>): Reserved → Ordered with their tubes (BR-05, BR-02); a package releases with its members.</summary>
    public void ReleaseReserved(IReadOnlyList<LineRelease> releases, SampleNumberSource sampleNumbers, Guid actorUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(sampleNumbers);
        RequireOpen("release reserved services");
        Require(releases.Count > 0, RegistrationCodes.LinesRequired, "Choose at least one reserved service.", DomainFault.Validation);

        var byLine = new Dictionary<Guid, LineRelease>();
        foreach (var release in releases)
        {
            byLine[release.LineId] = release;
        }

        var released = new List<(VisitLine Line, TubeSpec? Spec)>();
        foreach (var lineId in releases.Select(release => release.LineId).Distinct())
        {
            var line = LineOf(lineId);
            if (line.Kind == VisitLineKind.PackageMember)
            {
                if (line.ParentLineId is { } parentId && byLine.ContainsKey(parentId))
                {
                    continue;   // released with its package below
                }

                throw new DomainException(RegistrationCodes.PackageMemberAction, "This service belongs to a package; release the package.", DomainFault.Validation);
            }

            if (line.Status != VisitLineStatus.Reserved)
            {
                throw new DomainException(RegistrationCodes.LineNotReserved, $"'{line.ServiceCode}' is not reserved.", DomainFault.Conflict);
            }

            line.Release(byLine[lineId].ExpectedResultAtUtc, actorUserId, nowUtc);
            released.Add((line, byLine[lineId].Tube));
            foreach (var member in MembersOf(line))
            {
                var memberRelease = byLine.GetValueOrDefault(member.Id);
                member.Release(memberRelease is null ? member.ExpectedResultAtUtc : memberRelease.ExpectedResultAtUtc, actorUserId, nowUtc);
                released.Add((member, memberRelease?.Tube));
            }
        }

        var touched = AssignTubes(released, sampleNumbers, nowUtc);
        Recalculate();
        Revision++;
        Raise(new VisitLinesAdded(Id, Accession, BranchId, PatientId, actorUserId, [.. touched.Select(ToRegisteredTube)], [.. released.Select(pair => ToRegisteredLine(pair.Line))], nowUtc));
    }

    /// <summary>
    /// V4 Cancel services (<c>registration.cancel</c> + Cancel right, checked by the handler with the verified-result
    /// port): live lines only; a package cancels with its members; reason mandatory; nothing erased (BR-15). One
    /// <see cref="VisitLineCancelled"/> per cancelled line.
    /// </summary>
    public IReadOnlyList<VisitLine> CancelLines(IReadOnlyCollection<Guid> lineIds, Guid? reasonId, string reason, Guid actorUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        Require(lineIds.Count > 0, RegistrationCodes.LinesRequired, "Choose at least one service to cancel.", DomainFault.Validation);
        var cleaned = RequireReason(reason, RegistrationCodes.CancellationReasonRequired, "A cancellation needs a reason.");

        var cancelled = new List<VisitLine>();
        foreach (var lineId in lineIds.Distinct())
        {
            var line = LineOf(lineId);
            Require(line.Kind != VisitLineKind.PackageMember, RegistrationCodes.PackageMemberAction, "This service belongs to a package; cancel the package.", DomainFault.Validation);
            if (!line.IsLive)
            {
                throw new DomainException(RegistrationCodes.LineNotLive, $"'{line.ServiceCode}' is already cancelled.", DomainFault.Conflict);
            }

            line.Cancel(reasonId, cleaned, actorUserId, nowUtc);
            cancelled.Add(line);
            foreach (var member in MembersOf(line).Where(member => member.IsLive))
            {
                member.Cancel(reasonId, cleaned, actorUserId, nowUtc);
                cancelled.Add(member);
            }
        }

        Recalculate();
        Revision++;
        foreach (var line in cancelled)
        {
            Raise(new VisitLineCancelled(Id, Accession, line.Id, line.ServiceId, line.TubeId, actorUserId, cleaned, nowUtc));
        }

        return cancelled;
    }

    /// <summary>The lines a cancellation of <paramref name="lineIds"/> would touch (packages with their members), for the verified-result check before the change.</summary>
    public IReadOnlyList<VisitLine> LinesAffectedBy(IEnumerable<Guid> lineIds) =>
        [.. (lineIds ?? []).Distinct().Select(LineOf).SelectMany(line => MembersOf(line).Prepend(line))];

    /// <summary>V5 Update details (<c>registration.edit</c>): Open only.</summary>
    public void UpdateDetails(VisitDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        RequireOpen("edit the visit");
        Require((details.TreatmentNo?.Trim().Length ?? 0) <= MaxTreatmentNoLength, RegistrationCodes.HeaderInvalid, $"The treatment number has at most {MaxTreatmentNoLength} characters.", DomainFault.Validation);
        Require((details.InsuranceId?.Trim().Length ?? 0) <= MaxInsuranceIdLength, RegistrationCodes.HeaderInvalid, $"The insurance id has at most {MaxInsuranceIdLength} characters.", DomainFault.Validation);
        TreatmentNo = Blank(details.TreatmentNo);
        ReferringDoctorId = details.ReferringDoctorId;
        InsuranceId = Blank(details.InsuranceId);
        Urgent = details.Urgent;
        Biohazard = details.Biohazard;
        CollectedOutside = details.CollectedOutside;
        Revision++;
    }

    /// <summary>V6 Set or clear the discount (<c>registration.edit</c>): Open only; reason and cap when setting (INV-REG-005).</summary>
    public void SetDiscount(DiscountRequest? discount, string? clearReason, Guid actorUserId, DateTimeOffset nowUtc)
    {
        RequireOpen("change the discount");
        string reason;
        if (discount is null)
        {
            if (DiscountKind is null)
            {
                return;
            }

            reason = string.IsNullOrWhiteSpace(clearReason) ? "Discount removed" : clearReason.Trim();
            DiscountKind = null;
            DiscountValue = 0m;
            DiscountReasonId = null;
            DiscountReason = null;
            DiscountByUserId = actorUserId;
            DiscountAtUtc = nowUtc;
        }
        else
        {
            ApplyDiscountRequest(discount, actorUserId, nowUtc);
            reason = discount.Reason;
        }

        Recalculate();
        Revision++;
        RaiseDiscountChanged(actorUserId, reason, nowUtc);
    }

    /// <summary>
    /// V7 Contract transfer (<c>registration.approve</c> + Transfer right, checked by the handler): every live top-level
    /// line takes the new contract's price (BR-06); the transfer is logged with totals before and after (BR-15).
    /// </summary>
    public VisitTransfer TransferContract(Guid toPayerId, Guid toContractTierId, IReadOnlyDictionary<Guid, LinePrice> pricesByService, Guid? reasonId, string reason, Guid actorUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(pricesByService);
        Require(toPayerId != Guid.Empty && toContractTierId != Guid.Empty, RegistrationCodes.TierInvalid, "Choose the new payer and contract tier.", DomainFault.Validation);
        Require(toContractTierId != ContractTierId || toPayerId != PayerId, RegistrationCodes.TransferSameContract, "The visit is already on this contract.", DomainFault.Validation);
        var cleaned = RequireReason(reason, RegistrationCodes.TransferReasonRequired, "A transfer needs a reason.");

        var repriced = _lines.Where(line => line.IsLive && line.IsTopLevel).ToList();
        foreach (var line in repriced)
        {
            if (!pricesByService.TryGetValue(line.ServiceId, out var price))
            {
                throw new DomainException(RegistrationCodes.ServiceUnpriced, $"'{line.ServiceCode}' has no price under the new contract.", DomainFault.Validation);
            }
        }

        var (patientBefore, payerBefore) = (PatientShareTotal, PayerShareTotal);
        var (fromPayer, fromTier) = (PayerId, ContractTierId);
        foreach (var line in repriced)
        {
            line.Reprice(pricesByService[line.ServiceId]);
        }

        PayerId = toPayerId;
        ContractTierId = toContractTierId;
        Recalculate();
        Revision++;

        var transfer = VisitTransfer.Contract(Id, fromPayer, fromTier, toPayerId, toContractTierId, patientBefore, PatientShareTotal, payerBefore, PayerShareTotal, reasonId, cleaned, actorUserId, nowUtc);
        _transfers.Add(transfer);
        Raise(new VisitTransferred(Id, Accession, fromPayer, fromTier, toPayerId, toContractTierId, actorUserId, nowUtc));
        Raise(new VisitTransferRecorded(Id, Accession, transfer.Id, fromPayer, fromTier, toPayerId, toContractTierId, patientBefore, PatientShareTotal, actorUserId, cleaned, nowUtc));
        return transfer;
    }

    /// <summary>
    /// V8 Adjust line shares (<c>registration.approve</c> + Transfer right): the line's gross total stays the same
    /// (INV-REG-009); the discount is re-allocated; the shift is logged (BR-15).
    /// </summary>
    public VisitTransfer AdjustLineShares(Guid lineId, decimal patientShare, decimal payerShare, Guid? reasonId, string reason, Guid actorUserId, DateTimeOffset nowUtc)
    {
        var line = LineOf(lineId);
        Require(line.Kind != VisitLineKind.PackageMember, RegistrationCodes.PackageMemberAction, "This service belongs to a package; adjust the package.", DomainFault.Validation);
        if (!line.IsLive)
        {
            throw new DomainException(RegistrationCodes.LineNotLive, $"'{line.ServiceCode}' is cancelled.", DomainFault.Conflict);
        }

        var cleaned = RequireReason(reason, RegistrationCodes.TransferReasonRequired, "A share adjustment needs a reason.");
        var newPatient = DiscountAllocator.Round(patientShare);
        var newPayer = DiscountAllocator.Round(payerShare);
        Require(
            newPatient >= 0 && newPayer >= 0 && newPatient + newPayer == line.GrossPatientShare + line.PayerShare,
            RegistrationCodes.SharesInvalid,
            $"The patient and payer shares must add up to the line total ({line.GrossPatientShare + line.PayerShare:0.00}).",
            DomainFault.Validation);

        var (patientBefore, payerBefore) = (line.GrossPatientShare, line.PayerShare);
        line.SetShares(newPatient, newPayer);
        Recalculate();
        Revision++;

        var transfer = VisitTransfer.Shares(Id, line.Id, PayerId, ContractTierId, patientBefore, newPatient, payerBefore, newPayer, reasonId, cleaned, actorUserId, nowUtc);
        _transfers.Add(transfer);
        Raise(new VisitLineSharesAdjusted(Id, Accession, line.Id, line.ServiceId, patientBefore, newPatient, payerBefore, newPayer, actorUserId, cleaned, nowUtc));
        return transfer;
    }

    /// <summary>V9 Complete (system, later policies): Open → Completed; idempotent.</summary>
    public bool Complete(DateTimeOffset nowUtc)
    {
        if (Status == VisitStatus.Completed)
        {
            return false;
        }

        Status = VisitStatus.Completed;
        CompletedAtUtc = nowUtc;
        Revision++;
        Raise(new VisitCompleted(Id, Accession, nowUtc));
        return true;
    }

    /// <summary>V10 Re-point the visit to the surviving file of a merge (policy on <c>PatientsMerged</c>); idempotent; the first file is kept.</summary>
    public bool RepointPatient(Guid fromPatientId, Guid toPatientId, Guid actorUserId, string reason, DateTimeOffset nowUtc)
    {
        if (PatientId != fromPatientId || toPatientId == Guid.Empty || toPatientId == PatientId)
        {
            return false;
        }

        OriginalPatientId ??= PatientId;
        PatientId = toPatientId;
        Revision++;
        Raise(new VisitPatientRepointed(Id, Accession, fromPatientId, toPatientId, actorUserId, string.IsNullOrWhiteSpace(reason) ? "Patient files merged" : reason.Trim(), nowUtc));
        return true;
    }

    /// <summary>The prices a contract transfer would apply and the totals it would produce (V7 preview), without changing the visit.</summary>
    public TransferOutcome PreviewTransfer(IReadOnlyDictionary<Guid, LinePrice> pricesByService)
    {
        ArgumentNullException.ThrowIfNull(pricesByService);
        var top = _lines.Where(line => line.IsLive && line.IsTopLevel).ToList();
        var newGross = top.Select(line => pricesByService.TryGetValue(line.ServiceId, out var price) ? DiscountAllocator.Round(price.PatientShare) : 0m).ToList();
        var amount = DiscountKind is { } kind ? DiscountAllocator.AmountFor(kind, DiscountValue, newGross.Sum()) : 0m;
        var allocation = DiscountAllocator.Allocate(amount, newGross);
        var lines = top.Select((line, index) =>
        {
            var priced = pricesByService.TryGetValue(line.ServiceId, out var price);
            return new TransferOutcomeLine(
                line.Id,
                line.ServiceId,
                priced,
                priced ? DiscountAllocator.Round(price!.ListPrice) : 0m,
                newGross[index] - allocation[index],
                priced ? DiscountAllocator.Round(price!.PayerShare) : 0m,
                allocation[index]);
        }).ToList();

        return new TransferOutcome(
            lines,
            lines.Sum(line => line.ListPrice),
            lines.Sum(line => line.Discount),
            lines.Sum(line => line.PatientShare),
            lines.Sum(line => line.PayerShare));
    }

    private List<(VisitLine Line, TubeSpec? Spec)> AddItems(IReadOnlyList<VisitOrderItem> items, Guid? collectorUserId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        var present = LiveServiceIds.ToHashSet();
        var sequence = _lines.Count == 0 ? 0 : _lines.Max(line => line.Sequence);
        var added = new List<(VisitLine Line, TubeSpec? Spec)>();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            Require(item.Kind != VisitLineKind.PackageMember, RegistrationCodes.HeaderInvalid, "A package member is ordered through its package.", DomainFault.Validation);
            foreach (var serviceId in item.AllServiceIds)
            {
                if (!present.Add(serviceId))
                {
                    var code = item.ServiceId == serviceId ? item.ServiceCode : item.Members.First(member => member.ServiceId == serviceId).ServiceCode;
                    throw new DomainException(RegistrationCodes.DuplicateService, $"'{code}' is already on this visit.", DomainFault.Conflict);
                }
            }

            var status = item.Reserved ? VisitLineStatus.Reserved : VisitLineStatus.Ordered;
            var kind = item.Kind == VisitLineKind.Package ? VisitLineKind.Package : VisitLineKind.Service;
            var line = VisitLine.Create(Id, item, kind, null, status, ++sequence, collectorUserId, actorUserId, nowUtc);
            _lines.Add(line);
            added.Add((line, kind == VisitLineKind.Package ? null : item.Tube));
            if (kind == VisitLineKind.Package)
            {
                foreach (var member in item.Members)
                {
                    var memberLine = VisitLine.Create(Id, member, VisitLineKind.PackageMember, line.Id, status, ++sequence, collectorUserId, actorUserId, nowUtc);
                    _lines.Add(memberLine);
                    added.Add((memberLine, member.Tube));
                }
            }
        }

        return added;
    }

    /// <summary>BR-02 over the lines just added or released, each with its tube spec: returns every tube that gained a line.</summary>
    private List<VisitTube> AssignTubes(IReadOnlyList<(VisitLine Line, TubeSpec? Spec)> lines, SampleNumberSource sampleNumbers, DateTimeOffset nowUtc)
    {
        var needs = lines.Select(pair => NeedsTube(pair.Line) ? pair.Spec : null).ToList();
        var plan = TubePlanner.Assign(ExistingTubes(), [.. needs.Select(spec => spec?.Key)]);
        var created = plan.NewTubes.Select(key => VisitTube.Create(Id, sampleNumbers.Next(), needs.First(spec => spec is not null && spec.Key == key)!, nowUtc)).ToList();
        _tubes.AddRange(created);

        var touched = new List<VisitTube>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (plan.Assignments[index] is not { } assignment)
            {
                continue;
            }

            var tube = assignment.ExistingTubeId is { } existingId ? _tubes.First(candidate => candidate.Id == existingId) : created[assignment.NewTubeIndex!.Value];
            lines[index].Line.AssignTube(tube.Id);
            if (!touched.Contains(tube))
            {
                touched.Add(tube);
            }
        }

        return touched;
    }

    private static bool NeedsTube(VisitLine line) =>
        line.Status == VisitLineStatus.Ordered && line.Kind != VisitLineKind.Package && SampleCarryingTypes.Contains(line.ServiceType);

    private List<TubePlanner.ExistingTube> ExistingTubes() =>
        [.. _tubes.Select(tube => new TubePlanner.ExistingTube(tube.Id, tube.Key, _lines.Any(line => line.TubeId == tube.Id && line.Status == VisitLineStatus.Ordered)))];

    private void Answer(IReadOnlyList<PreparationAnswerInput> answers, Guid actorUserId, DateTimeOffset nowUtc)
    {
        foreach (var input in answers)
        {
            ArgumentNullException.ThrowIfNull(input);
            var answer = (input.Answer ?? string.Empty).Trim();
            Require(input.PreparationId != Guid.Empty && answer.Length is >= 1 and <= VisitPreparationAnswer.MaxAnswerLength, RegistrationCodes.PreparationInvalid, $"An answer has 1 to {VisitPreparationAnswer.MaxAnswerLength} characters.", DomainFault.Validation);
            var existing = _answers.FirstOrDefault(candidate => candidate.PreparationId == input.PreparationId);
            if (existing is null)
            {
                _answers.Add(VisitPreparationAnswer.Create(Id, input with { Answer = answer }, actorUserId, nowUtc));
            }
            else if (!string.Equals(existing.Answer, answer, StringComparison.Ordinal))
            {
                existing.Reanswer(answer, actorUserId, nowUtc);
            }
        }
    }

    private void ApplyDiscountRequest(DiscountRequest discount, Guid actorUserId, DateTimeOffset nowUtc)
    {
        Require(discount.ReasonId != Guid.Empty && !string.IsNullOrWhiteSpace(discount.Reason), RegistrationCodes.DiscountReasonRequired, "A discount needs a reason.", DomainFault.Validation);
        Require(
            discount.Value > 0 && (discount.Kind != Registration.DiscountKind.Percent || discount.Value <= 100m) && discount.Value == decimal.Round(discount.Value, 2),
            RegistrationCodes.DiscountInvalid,
            "The discount must be a positive amount, or a percent up to 100, with at most two decimals.",
            DomainFault.Validation);

        var baseAmount = DiscountBase();
        Require(baseAmount > 0, RegistrationCodes.NothingToDiscount, "There is no patient share to discount.", DomainFault.Validation);
        Require(discount.Kind != Registration.DiscountKind.Amount || discount.Value <= baseAmount, RegistrationCodes.DiscountInvalid, $"The discount cannot exceed the patient share ({baseAmount:0.00}).", DomainFault.Validation);

        var amount = DiscountAllocator.AmountFor(discount.Kind, discount.Value, baseAmount);
        var percent = DiscountAllocator.PercentOf(amount, baseAmount);
        if (percent > Math.Max(0, discount.CapPercent))
        {
            throw new DomainException(RegistrationCodes.DiscountAboveCap, $"The discount ({percent:0.##} %) exceeds your limit of {Math.Max(0, discount.CapPercent)} %.", DomainFault.Refused);
        }

        DiscountKind = discount.Kind;
        DiscountValue = discount.Value;
        DiscountReasonId = discount.ReasonId;
        DiscountReason = discount.Reason.Trim().Length <= MaxReasonLength ? discount.Reason.Trim() : discount.Reason.Trim()[..MaxReasonLength];
        DiscountByUserId = actorUserId;
        DiscountAtUtc = nowUtc;
    }

    private void RaiseDiscountChanged(Guid actorUserId, string reason, DateTimeOffset nowUtc) =>
        Raise(new VisitDiscountChanged(Id, Accession, DiscountKind?.ToString(), DiscountValue, DiscountTotal, actorUserId, reason, nowUtc));

    private decimal DiscountBase() => _lines.Where(line => line.IsLive && line.IsTopLevel).Sum(line => line.GrossPatientShare);

    /// <summary>INV-REG-004: the discount re-allocated over the live top-level lines, then the totals of the live lines.</summary>
    private void Recalculate()
    {
        var top = _lines.Where(line => line.IsLive && line.IsTopLevel).ToList();
        var weights = top.Select(line => line.GrossPatientShare).ToList();
        var amount = DiscountKind is { } kind ? DiscountAllocator.AmountFor(kind, DiscountValue, weights.Sum()) : 0m;
        var allocation = DiscountAllocator.Allocate(amount, weights);
        for (var index = 0; index < top.Count; index++)
        {
            top[index].ApplyDiscount(allocation[index]);
        }

        var live = _lines.Where(line => line.IsLive).ToList();
        TotalListPrice = live.Sum(line => line.ListPrice);
        DiscountTotal = live.Sum(line => line.Discount);
        PatientShareTotal = live.Sum(line => line.PatientShare);
        PayerShareTotal = live.Sum(line => line.PayerShare);
    }

    private VisitLine LineOf(Guid lineId) =>
        _lines.FirstOrDefault(line => line.Id == lineId)
        ?? throw new DomainException(RegistrationCodes.LineNotOnVisit, "The service line does not belong to this visit.", DomainFault.Validation);

    private IEnumerable<VisitLine> MembersOf(VisitLine line) =>
        line.Kind == VisitLineKind.Package ? _lines.Where(member => member.ParentLineId == line.Id) : [];

    private RegisteredTube ToRegisteredTube(VisitTube tube) =>
        new(tube.Id, tube.SampleNumber, tube.ClinicalGroupId, tube.SampleTypeId, tube.SampleConditionId,
            [.. _lines.Where(line => line.TubeId == tube.Id && line.Status == VisitLineStatus.Ordered).Select(line => line.ServiceId).Distinct()]);

    private static RegisteredLine ToRegisteredLine(VisitLine line) =>
        new(line.Id, line.ServiceId, line.TubeId, line.Status.ToString(), [.. line.TestIds]);

    private void RequireOpen(string action)
    {
        if (Status != VisitStatus.Open)
        {
            throw new InvalidStateTransitionException(RegistrationCodes.VisitNotOpen, "visit", Status.ToString(), action);
        }
    }

    private static string RequireReason(string? reason, string code, string message)
    {
        var cleaned = (reason ?? string.Empty).Trim();
        Require(cleaned.Length is >= 1 and <= MaxReasonLength, code, message, DomainFault.Validation);
        return cleaned;
    }

    private static void CheckHeader(VisitRegistration registration)
    {
        var accession = (registration.Accession ?? string.Empty).Trim();
        Require(accession.Length is >= 1 and <= MaxAccessionLength, RegistrationCodes.HeaderInvalid, "A visit needs an accession number.", DomainFault.Validation);
        Require(registration.PatientId != Guid.Empty, RegistrationCodes.PatientNotFound, "A visit needs a patient file.", DomainFault.Validation);
        Require(registration.BranchId != Guid.Empty, RegistrationCodes.HeaderInvalid, "A visit needs the session branch.", DomainFault.Validation);
        Require(registration.PayerId != Guid.Empty, RegistrationCodes.PayerInvalid, "A visit needs a payer.", DomainFault.Validation);
        Require(registration.ContractTierId != Guid.Empty, RegistrationCodes.TierInvalid, "A visit needs a contract tier.", DomainFault.Validation);
        Require((registration.TreatmentNo?.Trim().Length ?? 0) <= MaxTreatmentNoLength, RegistrationCodes.HeaderInvalid, $"The treatment number has at most {MaxTreatmentNoLength} characters.", DomainFault.Validation);
        Require((registration.InsuranceId?.Trim().Length ?? 0) <= MaxInsuranceIdLength, RegistrationCodes.HeaderInvalid, $"The insurance id has at most {MaxInsuranceIdLength} characters.", DomainFault.Validation);
        Require(Math.Abs(registration.UtcOffsetMinutes) <= MaxUtcOffsetMinutes, RegistrationCodes.HeaderInvalid, "The time-zone offset is out of range.", DomainFault.Validation);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One live top-level line under a previewed contract (V7 preview).</summary>
public sealed record TransferOutcomeLine(Guid LineId, Guid ServiceId, bool Priced, decimal ListPrice, decimal PatientShare, decimal PayerShare, decimal Discount);

/// <summary>The totals a contract transfer would produce (V7 preview).</summary>
public sealed record TransferOutcome(IReadOnlyList<TransferOutcomeLine> Lines, decimal TotalListPrice, decimal DiscountTotal, decimal PatientShareTotal, decimal PayerShareTotal);
