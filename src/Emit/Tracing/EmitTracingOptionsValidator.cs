namespace Emit.Tracing;

using Microsoft.Extensions.Options;

internal sealed class EmitTracingOptionsValidator : IValidateOptions<EmitTracingOptions>
{
    public ValidateOptionsResult Validate(string? name, EmitTracingOptions options)
    {
        if (options.MaxBaggageSizeBytes < 1024)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(options.MaxBaggageSizeBytes)} must be at least 1024 bytes.");
        }

        if (options.MaxBaggageSizeBytes > 65536) // 64KB absolute max
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(options.MaxBaggageSizeBytes)} must not exceed 65536 bytes (64KB).");
        }

        return ValidateOptionsResult.Success;
    }
}
