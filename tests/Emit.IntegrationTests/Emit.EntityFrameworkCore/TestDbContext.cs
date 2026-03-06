namespace Emit.EntityFrameworkCore.Tests;

using Emit.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;

internal sealed class IntegrationTestDbContext(DbContextOptions<IntegrationTestDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddEmitModel(emit => emit.UseNpgsql());
    }
}
