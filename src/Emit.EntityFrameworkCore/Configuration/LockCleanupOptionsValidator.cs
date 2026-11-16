namespace Emit.EntityFrameworkCore.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="LockCleanupOptions"/> configuration.
/// </summary>
internal sealed class LockCleanupOptionsValidator : IValidateOptions<LockCleanupOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, LockCleanupOptions options)
    {
        if (options.CleanupInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.CleanupInterval)} must be greater than zero.");
        }

        return ValidateOptionsResult.Success;
    }
}
