using Microsoft.Extensions.DependencyInjection;
namespace Ims.Application;

/// <summary>
/// Runs every FluentValidation validator registered for the request before its handler. Shape and format rules live in
/// validators; rules that need the database (uniqueness, existence) stay in handlers.
/// </summary>
public sealed class ValidationBehavior(IServiceProvider services) : IPipelineBehavior
{
    public async Task<TResult> HandleAsync<TResult>(IRequest<TResult> request, Func<Task<TResult>> next, CancellationToken ct)
    {
        var validators = services.GetServices(typeof(FluentValidation.IValidator<>).MakeGenericType(request.GetType()))
            .OfType<FluentValidation.IValidator>().ToList();
        if (validators.Count > 0)
        {
            var context = new FluentValidation.ValidationContext<object>(request);
            var failures = new List<FluentValidation.Results.ValidationFailure>();
            foreach (var v in validators)
                failures.AddRange((await v.ValidateAsync(context, ct)).Errors);

            if (failures.Count > 0)
            {
                var errors = failures.GroupBy(f => ErrorKeys.Normalize(f.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());
                throw new ValidationException(failures[0].ErrorMessage, errors);
            }
        }
        return await next();
    }
}
