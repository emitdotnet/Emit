namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MessagePipelineBuilderTests
{
    [Fact]
    public async Task GivenLevelsOfBaseContexts_WhenBuilt_ThenTheirMiddlewareRunsOnThePipelineOutermostLevelFirst()
    {
        // Arrange: middleware of every pipeline, of every consumer, and of consumers of string, each on its level,
        // singleton, factory and scoped alike
        var log = new List<string>();
        var services = new ServiceCollection().AddSingleton(log);
        var everyPipeline = new MessagePipelineBuilder<MessageContext>();
        everyPipeline.Use<RecordingMessageMiddleware>().Use(_ => new RecordingMiddleware("second", log));
        var everyConsumer = new MessagePipelineBuilder<ConsumeContext>(typeof(ConsumeContext<>));
        everyConsumer.Use<RecordingConsumeMiddleware>();
        var stringConsumers = new MessagePipelineBuilder<ConsumeContext<string>>();
        stringConsumers.Use<RecordingStringMiddleware>(MiddlewareLifetime.Scoped);
        foreach (var level in new MessagePipelineBuilder[] { everyPipeline, everyConsumer, stringConsumers })
        {
            level.RegisterServices(services);
        }

        var provider = services.BuildServiceProvider();
        var pipeline = MessagePipelineBuilder.Build<ConsumeContext<string>, string>(
            provider,
            Terminal(log),
            everyPipeline,
            null,
            everyConsumer,
            stringConsumers);

        // Act
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert
        Assert.Equal(["message", "second", "consume", "string", "terminal"], log);
    }

    [Fact]
    public async Task GivenGenericDefinition_WhenBuilt_ThenItIsClosedWithTheMessageTypeOfThePipeline()
    {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection().AddSingleton(log);
        var level = new MessagePipelineBuilder<ConsumeContext>(typeof(ConsumeContext<>));
        level.Use(typeof(RecordingGenericMiddleware<>));
        level.RegisterServices(services);
        var provider = services.BuildServiceProvider();
        var pipeline = MessagePipelineBuilder.Build<ConsumeContext<string>, string>(provider, Terminal(log), level);

        // Act
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert
        Assert.Equal([nameof(String), "terminal"], log);
    }

    [Theory]
    [InlineData(typeof(RecordingConsumeMiddleware))]
    [InlineData(typeof(RecordingGenericSendMiddleware<>))]
    [InlineData(typeof(TwoParameterMiddleware<,>))]
    [InlineData(typeof(GenericOuter<>.NestedMiddleware))]
    public void GivenTypeThatIsNotAGenericDefinitionOfThisLevel_WhenUsed_ThenItIsRejected(Type middlewareType)
    {
        // Arrange
        var level = new MessagePipelineBuilder<ConsumeContext>(typeof(ConsumeContext<>));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => level.Use(middlewareType));
        Assert.True(level.IsEmpty);
    }

    [Fact]
    public async Task GivenScopedMiddleware_WhenTwoMessagesFlow_ThenEachResolvesItsOwnFromItsScope()
    {
        // Arrange
        var instances = new List<ScopedMiddleware>();
        var services = new ServiceCollection().AddSingleton(instances);
        var level = new MessagePipelineBuilder<ConsumeContext<string>>();
        level.Use<ScopedMiddleware>(MiddlewareLifetime.Scoped);
        level.RegisterServices(services);
        var provider = services.BuildServiceProvider();
        var pipeline = MessagePipelineBuilder.Build<ConsumeContext<string>, string>(
            provider, new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask), level);

        // Act
        using (var first = provider.CreateScope())
        using (var second = provider.CreateScope())
        {
            await pipeline.InvokeAsync(CreateContext(first.ServiceProvider));
            await pipeline.InvokeAsync(CreateContext(second.ServiceProvider));
        }

        // Assert
        Assert.Equal(2, instances.Distinct().Count());
    }

    [Theory]
    [InlineData(MiddlewareLifetime.Singleton, 1)]
    [InlineData(MiddlewareLifetime.Scoped, 2)]
    public async Task GivenFactory_WhenTwoMessagesFlow_ThenItIsInvokedOnceAtBuildOrOncePerMessage(
        MiddlewareLifetime lifetime,
        int expectedCalls)
    {
        // Arrange
        var calls = 0;
        var level = new MessagePipelineBuilder<ConsumeContext<string>>();
        level.Use(
            _ =>
            {
                calls++;
                return new ShortCircuitMiddleware();
            },
            lifetime);
        var provider = new ServiceCollection().BuildServiceProvider();
        var pipeline = MessagePipelineBuilder.Build<ConsumeContext<string>, string>(
            provider, new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask), level);

        // Act
        await pipeline.InvokeAsync(CreateContext(provider));
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public void GivenTypesAndAFactory_WhenServicesAreRegisteredTwice_ThenEachTypeIsRegisteredOnceWithItsLifetime()
    {
        // Arrange
        var level = new MessagePipelineBuilder<ConsumeContext>(typeof(ConsumeContext<>));
        level.Use<RecordingConsumeMiddleware>()
            .Use(typeof(RecordingGenericMiddleware<>), MiddlewareLifetime.Scoped)
            .Use(_ => new ShortCircuitMiddleware());
        var services = new ServiceCollection();

        // Act
        level.RegisterServices(services);
        level.RegisterServices(services);

        // Assert: the factory creates its middleware itself, so nothing is registered for it
        Assert.Equal(
            [
                (typeof(RecordingConsumeMiddleware), ServiceLifetime.Singleton),
                (typeof(RecordingGenericMiddleware<>), ServiceLifetime.Scoped),
            ],
            services.Select(d => (d.ServiceType, d.Lifetime)));
    }

    [Fact]
    public void GivenNullFactoryOrType_WhenUsed_ThenItIsRejected()
    {
        // Arrange
        var level = new MessagePipelineBuilder<ConsumeContext>(typeof(ConsumeContext<>));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => level.Use((Func<IServiceProvider, IMiddleware<ConsumeContext>>)null!));
        Assert.Throws<ArgumentNullException>(() => level.Use((Type)null!));
        Assert.True(level.IsEmpty);
    }

    [Fact]
    public async Task GivenMiddlewareThatSkipsNext_WhenInvoked_ThenTheRestOfThePipelineDoesNotRun()
    {
        // Arrange
        var log = new List<string>();
        var level = new MessagePipelineBuilder<ConsumeContext<string>>();
        level.Use(_ => new ShortCircuitMiddleware()).Use(_ => new RecordingMiddleware("inner", log));
        var provider = new ServiceCollection().BuildServiceProvider();
        var pipeline = MessagePipelineBuilder.Build<ConsumeContext<string>, string>(provider, Terminal(log), level);

        // Act
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert
        Assert.Empty(log);
    }

    [Fact]
    public void GivenGroupLevelOfMiddlewareForEveryConsumer_WhenEnsuredToRunOnBatches_ThenItIsAccepted()
    {
        // Arrange
        var level = new MessagePipelineBuilder<ConsumeContext<string>>();
        level.Use<RecordingConsumeMiddleware>().Use<RecordingMessageMiddleware>();

        // Act
        var exception = Record.Exception(level.EnsureRunsOn<ConsumeContext<MessageBatch<string>>>);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenGroupLevelOfMiddlewareForSingleMessages_WhenEnsuredToRunOnBatches_ThenItIsRejected(bool factory)
    {
        // Arrange: a middleware typed for single messages, or a factory, whose middleware cannot be checked
        var level = new MessagePipelineBuilder<ConsumeContext<string>>();
        if (factory)
        {
            level.Use(_ => new RecordingConsumeMiddleware(new List<string>()));
        }
        else
        {
            level.Use<RecordingStringMiddleware>();
        }

        // Act & Assert
        Assert.Throws<InvalidOperationException>(level.EnsureRunsOn<ConsumeContext<MessageBatch<string>>>);
    }

    private static IMiddlewarePipeline<ConsumeContext<string>> Terminal(List<string> log) =>
        new TestPipeline<ConsumeContext<string>>(_ =>
        {
            log.Add("terminal");
            return Task.CompletedTask;
        });

    private static ConsumeContext<string> CreateContext(IServiceProvider services) => new()
    {
        MessageId = "test",
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Message = "hello",
        Services = services,
        TransportContext = TestTransportContext.Create(services),
    };

    private class RecordingMiddleware(string name, List<string> log) : IMiddleware<MessageContext>
    {
        public Task InvokeAsync(MessageContext context, Func<Task> next)
        {
            log.Add(name);
            return next();
        }
    }

    private sealed class RecordingMessageMiddleware(List<string> log) : RecordingMiddleware("message", log);

    private sealed class RecordingConsumeMiddleware(List<string> log) : IMiddleware<ConsumeContext>
    {
        public Task InvokeAsync(ConsumeContext context, Func<Task> next)
        {
            log.Add("consume");
            return next();
        }
    }

    private sealed class RecordingStringMiddleware(List<string> log) : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, Func<Task> next)
        {
            log.Add("string");
            return next();
        }
    }

    private sealed class RecordingGenericMiddleware<T>(List<string> log) : IMiddleware<ConsumeContext<T>>
    {
        public Task InvokeAsync(ConsumeContext<T> context, Func<Task> next)
        {
            log.Add(typeof(T).Name);
            return next();
        }
    }

    private sealed class RecordingGenericSendMiddleware<T> : IMiddleware<SendContext<T>>
    {
        public Task InvokeAsync(SendContext<T> context, Func<Task> next) => next();
    }

    private sealed class TwoParameterMiddleware<T1, T2> : IMiddleware<ConsumeContext<T1>>
    {
        public Task InvokeAsync(ConsumeContext<T1> context, Func<Task> next) => next();
    }

    private static class GenericOuter<T>
    {
        // Generic through its parent, so its name has no arity marker
        public sealed class NestedMiddleware : IMiddleware<SendContext<T>>
        {
            public Task InvokeAsync(SendContext<T> context, Func<Task> next) => next();
        }
    }

    private sealed class ScopedMiddleware(List<ScopedMiddleware> instances) : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, Func<Task> next)
        {
            instances.Add(this);
            return next();
        }
    }

    private sealed class ShortCircuitMiddleware : IMiddleware<MessageContext>
    {
        public Task InvokeAsync(MessageContext context, Func<Task> next) => Task.CompletedTask;
    }
}
