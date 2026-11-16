namespace Emit.MongoDB.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="MongoDbInboxOptions"/> configuration.
/// </summary>
internal sealed class MongoDbInboxOptionsValidator : IValidateOptions<MongoDbInboxOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, MongoDbInboxOptions options)
    {
        return options.Retention <= TimeSpan.Zero
            ? ValidateOptionsResult.Fail($"{nameof(options.Retention)} must be greater than zero.")
            : ValidateOptionsResult.Success;
    }
}
