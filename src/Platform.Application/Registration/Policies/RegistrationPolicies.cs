using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Domain.Patients;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Registration.Policies;

// Cross-module reactions of the Registration module (intake §11). Each policy runs after the commit that raised the
// event, inside the event's tenant scope, is idempotent on its natural key and commits once.

/// <summary>
/// V10 / B5: when two patient files are merged (Patients T3, CP-11 "merge moves visits … to the surviving file"), then
/// re-point every visit and every open booking of the merged file to the survivor; the visit keeps the original file id
/// (BR-15). Idempotent: after the first delivery nothing references the merged id any more.
/// </summary>
public sealed class RepointVisitsOnPatientsMergedPolicy(IVisitRepository visits, IBookingRepository bookings, IUnitOfWork unitOfWork, IClock clock) : IDomainEventHandler<PatientsMerged>
{
    public async Task HandleAsync(PatientsMerged domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.MergedId == domainEvent.SurvivorId)
        {
            return;
        }

        var changed = false;
        foreach (var visit in await visits.ListByPatientForUpdateAsync(domainEvent.MergedId, cancellationToken))
        {
            changed |= visit.RepointPatient(domainEvent.MergedId, domainEvent.SurvivorId, domainEvent.ActorUserId, domainEvent.Reason, clock.UtcNow);
        }

        foreach (var booking in await bookings.ListBookedByPatientForUpdateAsync(domainEvent.MergedId, cancellationToken))
        {
            changed |= booking.RepointPatient(domainEvent.MergedId, domainEvent.SurvivorId, domainEvent.Reason, clock.UtcNow);
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
