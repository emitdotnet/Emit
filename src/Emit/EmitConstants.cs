namespace Emit;

/// <summary>
/// Contains well-known constants used throughout the Emit library.
/// </summary>
public static class EmitConstants
{
    /// <summary>
    /// The sentinel value used for the registration key when a producer is registered
    /// without a named key (default/unnamed registration).
    /// </summary>
    public const string DefaultRegistrationKey = "__default__";

    /// <summary>
    /// Contains well-known provider identifiers.
    /// </summary>
    public static class Providers
    {
        /// <summary>
        /// The provider identifier for Kafka.
        /// </summary>
        public const string Kafka = "kafka";
    }
}
