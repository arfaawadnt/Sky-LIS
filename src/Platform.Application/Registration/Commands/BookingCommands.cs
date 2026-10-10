using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Pricing;
using Platform.Contracts.Registration;
using Platform.Domain.Authorization;
using Platform.Domain.Registration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration.Commands;

// Home reservations (CP-07, WF-03): B1 create, B2 edit, B3 cancel. Conversion (B4) is RegisterVisitCommand with the booking id.

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Create)]
public sealed record CreateBookingCommand(BookingRequest Booking) : ICommand<BookingCreatedResponse>;

public sealed class CreateBookingValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingValidator() => RuleFor(command => command.Booking).NotNull().SetValidator(new BookingRequestValidator());
}

/// <summary>Shape of a booking (B1/B2).</summary>
public sealed class BookingRequestValidator : AbstractValidator<BookingRequest>
{
    public BookingRequestValidator()
    {
        RuleFor(request => request.PatientName).MaximumLength(Booking.MaxNameLength);
        RuleFor(request => request.Mobile).MaximumLength(Booking.MaxMobileLength);
        RuleFor(request => request.Gender).Must(gender => gender is null or "Male" or "Female").WithMessage("The gender is Male or Female.");
        RuleFor(request => request.AgeYears).InclusiveBetween(0, 150).When(request => request.AgeYears is not null);
        RuleFor(request => request.Address).NotEmpty().MaximumLength(Booking.MaxAddressLength);
        RuleFor(request => request.PayerId).NotEmpty();
        RuleFor(request => request.ContractTierId).NotEmpty();
        RuleFor(request => request.ServiceIds).NotNull().Must(ids => ids is { Count: >= 1 and <= Booking.MaxServices }).WithMessage($"Book 1 to {Booking.MaxServices} services.");
        RuleForEach(request => request.ServiceIds).NotEmpty();
        RuleFor(request => request.Notes).MaximumLength(Booking.MaxNotesLength);
    }
}

/// <summary>B1: session branch; patient file Active or name + mobile; contract; services orderable; phlebotomist a user of the tenant.</summary>
public sealed class CreateBookingHandler(
    IBookingRepository bookings,
    IPatientLookup patients,
    IPayerLookup payers,
    ICatalogueLookup catalogue,
    IUserDirectory users,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<CreateBookingCommand, BookingCreatedResponse>
{
    public async Task<BookingCreatedResponse> Handle(CreateBookingCommand command, CancellationToken cancellationToken)
    {
        RegistrationActor.Of(user);
        var branch = BranchScope.Require(user);
        var now = clock.UtcNow;
        var content = await BookingChecks.ContentAsync(command.Booking, patients, payers, catalogue, users, cancellationToken);
        var number = await numbers.NextAsync(Booking.NumberKind, Booking.NumberPrefix, now, cancellationToken);

        var booking = Booking.Create(number, branch, content, now);
        bookings.Add(booking);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BookingCreatedResponse(booking.Id, booking.Number);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Edit)]
public sealed record UpdateBookingCommand(Guid BookingId, BookingRequest Booking) : ICommand;

public sealed class UpdateBookingValidator : AbstractValidator<UpdateBookingCommand>
{
    public UpdateBookingValidator()
    {
        RuleFor(command => command.BookingId).NotEmpty();
        RuleFor(command => command.Booking).NotNull().SetValidator(new BookingRequestValidator());
    }
}

/// <summary>B2: Booked only; same checks as B1.</summary>
public sealed class UpdateBookingHandler(
    IBookingRepository bookings,
    IPatientLookup patients,
    IPayerLookup payers,
    ICatalogueLookup catalogue,
    IUserDirectory users,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateBookingCommand>
{
    public async Task Handle(UpdateBookingCommand command, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetForUpdateAsync(command.BookingId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.BookingNotFound, "Booking not found.", DomainFault.NotFound);
        if (booking.Status != BookingStatus.Booked)
        {
            throw new InvalidStateTransitionException(RegistrationCodes.BookingNotBooked, "booking", booking.Status.ToString(), "edit");
        }

        var content = await BookingChecks.ContentAsync(command.Booking, patients, payers, catalogue, users, cancellationToken);
        booking.Update(content, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Cancel)]
public sealed record CancelBookingCommand(Guid BookingId, string Reason) : ICommand;

public sealed class CancelBookingValidator : AbstractValidator<CancelBookingCommand>
{
    public CancelBookingValidator()
    {
        RuleFor(command => command.BookingId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(Booking.MaxReasonLength);
    }
}

/// <summary>B3: Booked only; reason required.</summary>
public sealed class CancelBookingHandler(IBookingRepository bookings, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<CancelBookingCommand>
{
    public async Task Handle(CancelBookingCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        var booking = await bookings.GetForUpdateAsync(command.BookingId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.BookingNotFound, "Booking not found.", DomainFault.NotFound);

        booking.Cancel(actor, command.Reason, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The cross-module checks of B1/B2.</summary>
internal static class BookingChecks
{
    public static async Task<BookingContent> ContentAsync(BookingRequest request, IPatientLookup patients, IPayerLookup payers, ICatalogueLookup catalogue, IUserDirectory users, CancellationToken cancellationToken)
    {
        if (request.PatientId is { } patientId)
        {
            await RegistrationRules.RequirePatientAsync(patients, patientId, cancellationToken);
        }

        await RegistrationRules.RequireContractAsync(payers, request.PayerId, request.ContractTierId, cancellationToken);

        var ids = request.ServiceIds.Distinct().ToList();
        var services = (await catalogue.GetServicesAsync(ids, cancellationToken)).ToDictionary(service => service.Id);
        foreach (var id in ids)
        {
            if (!services.TryGetValue(id, out var service))
            {
                throw new DomainException(RegistrationCodes.ServiceUnknown, "A booked service does not exist.", DomainFault.Validation);
            }

            if (!service.IsActive || !service.IsOrderable)
            {
                throw new DomainException(RegistrationCodes.ServiceNotOrderable, $"'{service.Code}' is inactive or not orderable.", DomainFault.Validation);
            }
        }

        if (request.PhlebotomistUserId is { } phlebotomist)
        {
            var names = await users.GetNamesAsync([phlebotomist], cancellationToken);
            if (!names.ContainsKey(phlebotomist))
            {
                throw new DomainException(RegistrationCodes.PhlebotomistUnknown, "The phlebotomist is not a user of this laboratory.", DomainFault.Validation);
            }
        }

        return new BookingContent(
            new BookingPatient(request.PatientId, request.PatientName, request.Gender, request.Mobile, request.AgeYears),
            request.Address,
            request.VisitAtUtc,
            request.PhlebotomistUserId,
            request.PayerId,
            request.ContractTierId,
            ids,
            request.Notes);
    }
}
