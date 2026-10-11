namespace Platform.Application.Patients;

/// <summary>The visit figures of one file in the register (CP-11 "visit count, last visit").</summary>
public sealed record PatientVisitFigures(int VisitCount, DateTimeOffset? LastVisitAtUtc)
{
    public static PatientVisitFigures None { get; } = new(0, null);
}

/// <summary>
/// Read port the Patients register needs from Registration (MODULES.md §1 "cross-module reads"; UAT A-09): how many
/// visits each file has and when the latest was registered, counted on the file the visit belongs to now (a merged
/// file's visits count on the survivor). Bounded to the current tenant; files without visits are absent. The adapter
/// is <c>Platform.Infrastructure.Persistence.Registration.PatientVisitStatistics</c>.
/// </summary>
public interface IPatientVisitStatistics
{
    /// <summary>At most <see cref="MaxIds"/> ids per call (one register page).</summary>
    Task<IReadOnlyDictionary<Guid, PatientVisitFigures>> ForPatientsAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken);

    public const int MaxIds = 500;
}
