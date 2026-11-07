using FluentValidation.Results;

namespace Anitime.Application.Validation;

public static class ValidationExtensions
{
    public static Dictionary<string, string[]> ToDictionary(this ValidationResult result) =>
        result.Errors
            .GroupBy(x => x.PropertyName, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);
}
