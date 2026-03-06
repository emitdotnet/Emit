namespace Emit.Tracing;

/// <summary>
/// Configuration options for distributed tracing in Emit.
/// </summary>
public sealed class EmitTracingOptions
{
    /// <summary>
    /// Gets or sets whether distributed tracing is enabled.
    /// </summary>
    /// <remarks>
    /// When disabled, no Activities are created and trace context is not propagated.
    /// Default: <c>true</c>.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to create root Activities when <see cref="System.Diagnostics.Activity.Current"/> is <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <c>true</c>, Emit creates root Activities for console apps and background services.
    /// When <c>false</c>, tracing only occurs when already within an Activity context (e.g., ASP.NET Core request).
    /// </para>
    /// <para>Default: <c>true</c>.</para>
    /// </remarks>
    public bool CreateRootActivities { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to propagate Activity baggage in message headers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Baggage carries correlation data (e.g., tenant-id, user-id) across service boundaries.
    /// Baggage is limited to <see cref="MaxBaggageSizeBytes"/> total size; excess items are dropped with a warning.
    /// </para>
    /// <para>Default: <c>true</c>.</para>
    /// </remarks>
    public bool PropagateBaggage { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum total size of baggage in bytes.
    /// </summary>
    /// <remarks>
    /// W3C recommends 8KB (8192 bytes) maximum. Excess baggage items are dropped.
    /// Default: 8192.
    /// </remarks>
    public int MaxBaggageSizeBytes { get; set; } = 8192;
}
