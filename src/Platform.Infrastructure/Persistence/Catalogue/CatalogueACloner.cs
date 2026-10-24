using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// BR-25 for catalogue half A (see <see cref="ICatalogueACloner"/>). Every copy is rebuilt through the aggregates'
/// factories (the domain rules hold for the copy; events and the field history are recorded for the new tenant) and
/// stamped with the target tenant explicitly, which the tenant-stamp interceptor accepts only inside an elevated
/// scope. Nothing is saved here.
/// </summary>
internal sealed partial class CatalogueACloner(AppDbContext context, ICurrentTenant tenant, IClock clock, ILogger<CatalogueACloner> logger) : ICatalogueACloner
{
    public Task<CatalogueACloneResult> CloneAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken) =>
        CloneAsync(sourceTenantId, targetTenantId, null, cancellationToken);

    public async Task<CatalogueACloneResult> CloneAsync(Guid sourceTenantId, Guid targetTenantId, IReadOnlyDictionary<Guid, Guid>? reportTemplateMap, CancellationToken cancellationToken)
    {
        if (!tenant.IsElevated)
        {
            throw new InvalidOperationException("The catalogue cloner reads one tenant and writes another: run it inside the elevated provisioning scope.");
        }

        if (sourceTenantId == Guid.Empty || targetTenantId == Guid.Empty || sourceTenantId == targetTenantId)
        {
            throw new ArgumentException("Source and target must be two different tenants.", nameof(targetTenantId));
        }

        var now = clock.UtcNow;
        var groups = new Dictionary<Guid, Guid>();
        var sampleTypes = new Dictionary<Guid, Guid>();
        var conditions = new Dictionary<Guid, Guid>();
        var units = new Dictionary<Guid, Guid>();
        var patientConditions = new Dictionary<Guid, Guid>();
        var tests = new Dictionary<Guid, Guid>();
        var services = new Dictionary<Guid, Guid>();

        foreach (var source in await Source<ClinicalGroup>(sourceTenantId).Where(group => group.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var template = source.ReportTemplateId is { } templateId && reportTemplateMap?.TryGetValue(templateId, out var mapped) == true ? mapped : (Guid?)null;
            var copy = ClinicalGroup.Create(source.Code, source.UnitCode, source.Name, source.ReportName, template, source.DisplayOrder, now);
            Stage(copy, targetTenantId);
            groups[source.Id] = copy.Id;
        }

        foreach (var source in await Source<MeasurementUnit>(sourceTenantId).Where(unit => unit.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var copy = MeasurementUnit.Create(source.Name, now);
            Stage(copy, targetTenantId);
            units[source.Id] = copy.Id;
        }

        foreach (var source in await Source<PatientCondition>(sourceTenantId).Where(condition => condition.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var copy = PatientCondition.Create(source.Name, now);
            Stage(copy, targetTenantId);
            patientConditions[source.Id] = copy.Id;
        }

        foreach (var source in await Source<SampleType>(sourceTenantId).Where(sampleType => sampleType.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var activeConditions = source.Conditions.Where(condition => condition.IsActive).ToList();
            var copy = SampleType.Create(source.Code, source.ShortName, source.Name, activeConditions.Select(condition => new SampleConditionSpec(null, condition.Code, condition.Name, true)), now, source.ForCulture);
            Stage(copy, targetTenantId);
            sampleTypes[source.Id] = copy.Id;
            foreach (var condition in activeConditions)
            {
                conditions[condition.Id] = copy.Conditions.Single(created => created.Code == condition.Code).Id;
            }
        }

        var sourceTests = await Source<LabTest>(sourceTenantId).Where(test => test.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var source in sourceTests)
        {
            if (!groups.TryGetValue(source.ClinicalGroupId, out var groupId))
            {
                Skipped(nameof(LabTest), source.Code, "its clinical group was not copied");
                continue;
            }

            var (sampleTypeId, conditionId) = MapSample(source.SampleTypeId, source.SampleConditionId, sampleTypes, conditions);
            var definition = new LabTestDefinition(
                source.Name, source.ReportName, source.Loinc, source.Cpt, groupId, source.Type, sampleTypeId, conditionId,
                source.UnitId is { } unitId && units.TryGetValue(unitId, out var mappedUnit) ? mappedUnit : null,
                source.Gender, source.Methodology, source.Decimals, source.IsHiddenHelper, source.PrintReference, source.LabelCount, source.Prefix);
            try
            {
                var copy = LabTest.Create(source.Code, definition, now);
                copy.SetTiming(source.Timing, now);
                copy.ReplaceRangeBands(
                    source.RangeBands.OrderBy(band => band.DisplayOrder)
                        .Where(band => band.PatientConditionId is null || patientConditions.ContainsKey(band.PatientConditionId.Value))
                        .Select(band => new RangeBandSpec(
                            band.Sex, band.AgeFrom, band.AgeTo, band.AgeUnit,
                            band.PatientConditionId is { } conditionRef ? patientConditions[conditionRef] : null,
                            band.Low, band.High, band.PanicLow, band.PanicHigh, band.ReportableLow, band.ReportableHigh, band.TextRange))
                        .ToList(),
                    now);
                copy.ReplaceListValues(source.ListValues.OrderBy(value => value.DisplayOrder).Select(value => CopyOf(value, source.Code)).ToList(), now);
                copy.ReplaceParagraphs(source.Paragraphs.OrderBy(paragraph => paragraph.DisplayOrder).Select(paragraph => new ParagraphSpec(paragraph.Title, paragraph.Text)).ToList(), now);
                if (source.FormulaExpression is not null)
                {
                    copy.SetFormula(source.FormulaExpression, source.FormulaReferences, now);   // codes, valid in the copy as they are
                }

                Stage(copy, targetTenantId);
                tests[source.Id] = copy.Id;
            }
            catch (DomainException refusal)
            {
                Skipped(nameof(LabTest), source.Code, refusal.Message);
            }
        }

        var testTypes = sourceTests.ToDictionary(test => test.Id, test => test.Type);
        var sourceServices = await Source<Service>(sourceTenantId).Where(service => service.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false);
        var serviceTypes = sourceServices.ToDictionary(service => service.Id, service => service.Type);

        // packages last: their members are services that must already have their copies
        foreach (var source in sourceServices.OrderBy(service => service.Type == ServiceType.Package ? 1 : 0).ThenBy(service => service.Sequence))
        {
            Guid? groupId = null;
            if (source.ClinicalGroupId is { } sourceGroup)
            {
                if (!groups.TryGetValue(sourceGroup, out var mappedGroup))
                {
                    Skipped(nameof(Service), source.Code, "its clinical group was not copied");
                    continue;
                }

                groupId = mappedGroup;
            }

            var (sampleTypeId, conditionId) = MapSample(source.SampleTypeId, source.SampleConditionId, sampleTypes, conditions);
            var members = new List<ResolvedServiceMember>();
            foreach (var member in source.Members.OrderBy(member => member.Order))
            {
                if (member.Kind == ServiceMemberKind.Test && tests.TryGetValue(member.MemberId, out var testId))
                {
                    members.Add(new ResolvedServiceMember(member.Kind, testId, member.Order, member.InSum, member.Visible, testTypes[member.MemberId], null));
                }
                else if (member.Kind == ServiceMemberKind.Service && services.TryGetValue(member.MemberId, out var serviceId))
                {
                    members.Add(new ResolvedServiceMember(member.Kind, serviceId, member.Order, member.InSum, member.Visible, null, serviceTypes[member.MemberId]));
                }
            }

            var definition = new ServiceDefinition(
                source.ShortName, source.ShortcutNo, source.Name, source.ReportName, source.Loinc, source.Cpt, groupId, sampleTypeId, conditionId,
                source.Header, source.Aliases, source.Precaution, source.Gender, source.TurnaroundHours, source.LabelCount, source.PrintAlone,
                source.IsOrderable, source.ShowOnWeb, source.ProfileSum);
            try
            {
                var copy = Service.Create(source.Code, source.Type, definition, source.Sequence, members, now);
                Stage(copy, targetTenantId);
                services[source.Id] = copy.Id;
            }
            catch (DomainException refusal)
            {
                Skipped(nameof(Service), source.Code, refusal.Message);
            }
        }

        LogCloned(logger, sourceTenantId, targetTenantId, groups.Count, sampleTypes.Count, units.Count, patientConditions.Count, tests.Count, services.Count);
        return new CatalogueACloneResult(groups, sampleTypes, conditions, units, patientConditions, tests, services);
    }

    /// <summary>
    /// A list value as the copy takes it. A value stored before CAT-061 (validation v0.5.0 O-2) can be both Normal and
    /// Critical; the copy keeps it Critical only (the panic flag still fires), so the whole test is not skipped.
    /// </summary>
    private ListValueSpec CopyOf(ListValue value, string testCode)
    {
        var bothFlags = value.IsNormal && value.IsCritical;
        if (bothFlags)
        {
            NormalAndCriticalCopiedAsCritical(logger, testCode, value.Value);
        }

        return new ListValueSpec(value.Value, value.IsNormal && !bothFlags, value.IsCritical, value.IsDefault, value.IsPlaceholder, value.Colour);
    }

    private IQueryable<TEntity> Source<TEntity>(Guid sourceTenantId)
        where TEntity : class, ITenantScoped =>
        context.Set<TEntity>().AsNoTracking().Where(entity => entity.TenantId == sourceTenantId);

    private static (Guid? SampleTypeId, Guid? ConditionId) MapSample(Guid? sampleTypeId, Guid? conditionId, Dictionary<Guid, Guid> sampleTypes, Dictionary<Guid, Guid> conditions)
    {
        if (sampleTypeId is not { } typeId || !sampleTypes.TryGetValue(typeId, out var mappedType))
        {
            return (null, null);
        }

        return (mappedType, conditionId is { } condition && conditions.TryGetValue(condition, out var mappedCondition) ? mappedCondition : null);
    }

    /// <summary>Tracks the copy as added and stamps the target tenant on the root and on every child row.</summary>
    private void Stage(object root, Guid targetTenantId)
    {
        context.Add(root);
        var entry = context.Entry(root);
        entry.Property(nameof(ITenantScoped.TenantId)).CurrentValue = targetTenantId;
        foreach (var collection in entry.Collections)
        {
            foreach (var child in collection.CurrentValue?.Cast<object>() ?? [])
            {
                context.Entry(child).Property(nameof(ITenantScoped.TenantId)).CurrentValue = targetTenantId;
            }
        }
    }

    private void Skipped(string kind, string code, string reason) => LogSkipped(logger, kind, code, reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue A cloned from {Source} to {Target}: {Groups} groups, {SampleTypes} sample types, {Units} units, {Conditions} patient conditions, {Tests} tests, {Services} services.")]
    private static partial void LogCloned(ILogger logger, Guid source, Guid target, int groups, int sampleTypes, int units, int conditions, int tests, int services);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalogue A clone skipped {Kind} {Code}: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string kind, string code, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalogue A clone: list value {Value} of test {Code} was Normal and Critical; the copy keeps it Critical only (CAT-061).")]
    private static partial void NormalAndCriticalCopiedAsCritical(ILogger logger, string code, string value);
}
