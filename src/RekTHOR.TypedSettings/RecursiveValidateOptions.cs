using Microsoft.Extensions.Options;

namespace TypedSettings;

internal sealed class RecursiveValidateOptions<T> : IValidateOptions<T> where T : class, ISettings<T>
{
    public ValidateOptionsResult Validate(string? name, T options)
    {
        List<string> errors = RecursiveValidator.Validate(options, T.SectionName);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
