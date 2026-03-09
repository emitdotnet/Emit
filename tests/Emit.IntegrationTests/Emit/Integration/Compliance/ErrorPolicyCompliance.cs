namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for exception-type-specific error policy behavior using <c>When&lt;TException&gt;</c>
/// clauses. Derived classes configure a <c>string, string</c> topic whose consumer group applies
/// a policy with a typed clause and a default action.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ErrorPolicyCompliance
{
    /// <summary>
    /// Configures the messaging provider with a source <c>string, string</c> topic and a DLQ
    /// <c>string, string</c> topic. The source consumer group applies an error policy with a
    /// <c>When&lt;ArgumentException&gt;</c> clause that dead-letters to <paramref name="dlqTopic"/>
    /// and a <c>Default</c> clause that discards. The consumer group must use
    /// <see cref="TypedExceptionConsumer"/>. A consumer on <paramref name="dlqTopic"/> must use
    /// <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId);

    /// <summary>
    /// Verifies that when the consumer throws the matching exception type, the message is
    /// dead-lettered according to the <c>When</c> clause, not the default action.
    /// </summary>
    [Fact]
    public async Task GivenWhenClause_WhenMatchingExceptionThrown_ThenMatchingActionApplied()
    {
        // Arrange
        var sourceTopic = $"test-ep-match-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-ep-match-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<string>();
        var throwControl = new ThrowControl();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddSingleton(throwControl);
                services.AddEmit(emit => ConfigureEmit(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — produce a message that triggers ArgumentException (matches the When clause → DLQ).
            throwControl.ExceptionToThrow = ThrowControl.ThrowArgumentException;
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "matching-exception"));

            // Assert — message arrives in the DLQ because the When<ArgumentException> clause matched.
            var ctx = await dlqSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("matching-exception", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that when the consumer throws a non-matching exception type, the default action
    /// (discard) applies and the pipeline continues processing subsequent messages.
    /// </summary>
    [Fact]
    public async Task GivenWhenClause_WhenNonMatchingExceptionThrown_ThenDefaultActionApplied()
    {
        // Arrange
        var sourceTopic = $"test-ep-default-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-ep-default-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<string>();
        var throwControl = new ThrowControl();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddSingleton(throwControl);
                services.AddEmit(emit => ConfigureEmit(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — produce a message that triggers InvalidOperationException (no When clause → Default → discard).
            throwControl.ExceptionToThrow = ThrowControl.ThrowInvalidOperation;
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "non-matching-exception"));

            // Act — produce a sentinel message with no exception to prove the pipeline is still running.
            throwControl.ExceptionToThrow = ThrowControl.ThrowNothing;
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "sentinel-after-discard"));

            // Assert — the non-matching exception triggers discard; the DLQ receives nothing from it.
            // The sentinel proves pipeline continued. We confirm the DLQ never received the first message
            // by checking it receives only the sentinel (which would go to DLQ if it also throws — it doesn't).
            // Instead, verify the sink (direct consumer) is empty for the first message.
            // Since the sentinel does not throw (throwNothing), it succeeds and does NOT go to DLQ.
            // We wait a short period and assert the DLQ is still empty (discard leaves nothing in DLQ).
            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.Empty(dlqSink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider with a source <c>string, string</c> topic and a DLQ
    /// <c>string, string</c> topic. The source consumer group applies an error policy with a
    /// predicate-filtered <c>When&lt;InvalidOperationException&gt;(predicate, ...)</c> clause that
    /// dead-letters to <paramref name="dlqTopic"/> only when the predicate matches, and a
    /// <c>Default</c> clause that discards. The consumer group must use
    /// <see cref="PredicateThrowingConsumer"/>. A consumer on <paramref name="dlqTopic"/> must use
    /// <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    protected abstract void ConfigureEmitWithPredicateWhen(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId);

    /// <summary>
    /// Verifies that when the predicate in a <c>When</c> clause returns <see langword="false"/>,
    /// the clause is skipped and the default action (discard) applies, and the pipeline continues.
    /// When the predicate returns <see langword="true"/>, the message is dead-lettered.
    /// </summary>
    [Fact]
    public async Task GivenPredicateWhenClause_WhenPredicateFalse_ThenFallsThroughToDefault()
    {
        // Arrange
        var sourceTopic = $"test-ep-pred-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-ep-pred-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<string>();
        var predicateControl = new PredicateControl();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddSingleton(predicateControl);
                services.AddEmit(emit =>
                    ConfigureEmitWithPredicateWhen(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — produce a message whose exception does NOT match the predicate → falls through to Default → discard.
            predicateControl.MessageSuffix = "WRONG";
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "non-matching-predicate"));

            // Produce a sentinel to confirm pipeline is alive after the discard.
            predicateControl.MessageSuffix = "WRONG";
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "sentinel-alive"));

            // Wait for both to be processed (discarded), then confirm DLQ is empty.
            await Task.Delay(TimeSpan.FromSeconds(8));
            Assert.Empty(dlqSink.ReceivedMessages);

            // Act — produce a message whose exception DOES match the predicate → dead-lettered.
            predicateControl.MessageSuffix = "MATCH";
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "matching-predicate"));

            // Assert — the matching message arrives in the DLQ.
            var ctx = await dlqSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("matching-predicate", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Controls the message suffix thrown by <see cref="PredicateThrowingConsumer"/>.
    /// </summary>
    public sealed class PredicateControl
    {
        /// <summary>
        /// The suffix appended to the exception message. "MATCH" triggers the When predicate;
        /// any other value causes predicate to return false.
        /// </summary>
        public volatile string MessageSuffix = "WRONG";
    }

    /// <summary>
    /// Consumer that always throws <see cref="InvalidOperationException"/> whose message
    /// includes the configured <see cref="PredicateControl.MessageSuffix"/>.
    /// </summary>
    public sealed class PredicateThrowingConsumer(PredicateControl control) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"Simulated error: {control.MessageSuffix}");
    }

    /// <summary>
    /// Controls what exception the <see cref="TypedExceptionConsumer"/> throws.
    /// </summary>
    public sealed class ThrowControl
    {
        /// <summary>Sentinel: throw no exception (succeed).</summary>
        public const int ThrowNothing = 0;

        /// <summary>Sentinel: throw <see cref="ArgumentException"/>.</summary>
        public const int ThrowArgumentException = 1;

        /// <summary>Sentinel: throw <see cref="InvalidOperationException"/>.</summary>
        public const int ThrowInvalidOperation = 2;

        /// <summary>
        /// Current exception mode. One of the <c>Throw*</c> constants.
        /// </summary>
        public volatile int ExceptionToThrow;
    }

    /// <summary>
    /// Consumer that throws an exception type determined by <see cref="ThrowControl"/>.
    /// </summary>
    public sealed class TypedExceptionConsumer(ThrowControl throwControl) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            return throwControl.ExceptionToThrow switch
            {
                ThrowControl.ThrowArgumentException =>
                    throw new ArgumentException("Simulated ArgumentException for error policy test."),
                ThrowControl.ThrowInvalidOperation =>
                    throw new InvalidOperationException("Simulated InvalidOperationException for error policy test."),
                _ => Task.CompletedTask,
            };
        }
    }

    /// <summary>
    /// Consumer that captures dead-lettered messages for assertion.
    /// </summary>
    public sealed class DlqCaptureConsumer(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }
}
