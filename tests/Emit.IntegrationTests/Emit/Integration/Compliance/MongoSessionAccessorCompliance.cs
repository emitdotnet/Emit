namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.MongoDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for IMongoSessionAccessor. Verifies that the session accessor
/// is populated after IUnitOfWork.BeginAsync and cleared after disposal.
/// </summary>
[Trait("Category", "Integration")]
public abstract class MongoSessionAccessorCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures Emit with MongoDB persistence (outbox enabled).
    /// </summary>
    protected abstract void ConfigureEmit(EmitBuilder emit);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenMongoUnitOfWork_WhenBeginAsyncCalled_ThenSessionAccessorHasSession()
    {
        // Arrange
        var host = BuildHost();
        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var accessor = sp.GetRequiredService<IMongoSessionAccessor>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            // Assert — session is null before begin.
            Assert.Null(accessor.Session);

            // Act
            await using var tx = await unitOfWork.BeginAsync();

            // Assert — session is now populated.
            Assert.NotNull(accessor.Session);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenMongoUnitOfWork_WhenTransactionDisposed_ThenSessionAccessorSessionCleared()
    {
        // Arrange
        var host = BuildHost();
        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var accessor = sp.GetRequiredService<IMongoSessionAccessor>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            // Act
            var tx = await unitOfWork.BeginAsync();
            Assert.NotNull(accessor.Session);
            await tx.DisposeAsync();

            // Assert — session is cleared after dispose.
            Assert.Null(accessor.Session);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private IHost BuildHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new OutboxProviderMarker());
                services.AddEmit(emit => ConfigureEmit(emit));
            })
            .Build();
    }
}
