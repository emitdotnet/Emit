namespace BuildingSentinel.PostgreSQL.Entities;

/// <summary>
/// The highest generation of a daemon that wrote, keyed by the daemon's fencing key.
/// </summary>
public sealed class DaemonFenceEntity
{
    public Guid FencingKey { get; set; }
    public long Generation { get; set; }
}
