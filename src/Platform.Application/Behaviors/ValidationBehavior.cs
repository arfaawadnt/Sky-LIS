using FluentValidation;
using MediatR;

namespace Platform.Application.Behaviors;

/// <summary>
/// Shape validation (required, length, format) with FluentValidation, after authorization and before the
/// handler. Business rules never live in validators (pitfall P-10); the aggregate decides.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var validatorList = validators as IReadOnlyList<IValidator<TRequest>> ?? validators.ToList();
        if (validatorList.Count == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validatorList.Select(validator => validator.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(result => result.Errors).Where(failure => failure is not null).ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
