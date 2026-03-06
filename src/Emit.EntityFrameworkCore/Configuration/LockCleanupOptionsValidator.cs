namespace Emit.EntityFrameworkCore.Configuration;

using Emit.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="LockCleanupOptions"/> configuration.
/// </summary>
internal sealed class LockCleanupOptionsValidator : IValidateOptions<LockCleanupOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, LockCleanupOptions options)
    {
        if (options.CleanupInterval < ValidationConstants.MinLockCleanupInterval)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(options.CleanupInterval)} must be at least {ValidationConstants.MinLockCleanupInterval.TotalMinutes} minute(s).");
        }

        return ValidateOptionsResult.Success;
    }
}
