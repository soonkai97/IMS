using FluentValidation;
using Ims.Domain;
namespace Ims.Application;

/// <summary>Rules shared by the list and report filters, so a silly filter is a 400 with a sentence, never a 500 or an empty page with no explanation.</summary>
public static class FilterRuleExtensions
{
    /// <summary>A search box value of at most <see cref="Limits.SearchMaxLength"/> characters (a 4,000 character paste used to fail in the database).</summary>
    public static IRuleBuilderOptions<T, string?> MustBeAShortSearch<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(Limits.SearchMaxLength).WithMessage(Messages.SearchTooLong);

    /// <summary>The "to" date is not earlier than the "from" date.</summary>
    public static IRuleBuilderOptions<T, DateOnly?> MustNotBeBefore<T>(this IRuleBuilder<T, DateOnly?> rule, Func<T, DateOnly?> from) =>
        rule.Must((filter, to) => to is null || from(filter) is not { } start || start <= to).WithMessage(Messages.DateRangeInvalid);
}
