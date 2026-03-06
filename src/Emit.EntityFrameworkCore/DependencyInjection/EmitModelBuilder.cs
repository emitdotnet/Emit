namespace Emit.EntityFrameworkCore.DependencyInjection;

using Emit.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Builder for applying database-specific optimizations to the Emit model configuration.
/// </summary>
/// <remarks>
/// After calling <see cref="ModelBuilderExtensions.AddEmitModel"/> to configure the portable
/// model, call provider-specific methods on this builder to optimize column types for your database.
/// </remarks>
public sealed class EmitModelBuilder
{
    private readonly ModelBuilder modelBuilder;

    internal EmitModelBuilder(ModelBuilder modelBuilder)
    {
        this.modelBuilder = modelBuilder;
    }

    /// <summary>
    /// Applies PostgreSQL-specific optimizations to the Emit model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method configures:
    /// <list type="bullet">
    /// <item><description>UUID type with <c>gen_random_uuid()</c> default for entry IDs</description></item>
    /// <item><description>BIGINT IDENTITY column for sequence numbers</description></item>
    /// <item><description>JSONB column for properties</description></item>
    /// <item><description>BYTEA column for payload</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <returns>This builder instance for method chaining.</returns>
    public EmitModelBuilder UseNpgsql()
    {
        var outboxEntry = modelBuilder.Entity<OutboxEntry>();

        outboxEntry.Property(e => e.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        outboxEntry.Property(e => e.Sequence)
            .UseIdentityByDefaultColumn();

        outboxEntry.Property(e => e.Payload)
            .HasColumnType("bytea");

        outboxEntry.Property(e => e.Properties)
            .HasColumnType("jsonb");

        return this;
    }
}
