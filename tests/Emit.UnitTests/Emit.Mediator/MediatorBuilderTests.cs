namespace Emit.Mediator.Tests;

using global::Emit.Mediator;
using global::Emit.Mediator.DependencyInjection;
using Xunit;

public sealed class MediatorBuilderTests
{
    [Fact]
    public void GivenValidHandler_WhenAddHandler_ThenRegistersRequestType()
    {
        // Arrange
        var builder = new MediatorBuilder();

        // Act
        builder.AddHandler<TestHandler>();

        // Assert
        Assert.Single(builder.Registrations);
        Assert.True(builder.Registrations.ContainsKey(typeof(TestRequest)));
    }

    [Fact]
    public void GivenDuplicateRequestType_WhenAddHandler_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new MediatorBuilder();
        builder.AddHandler<TestHandler>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.AddHandler<DuplicateHandler>());
    }

    [Fact]
    public void GivenMultiHandler_WhenAddHandler_ThenRegistersAllRequestTypes()
    {
        // Arrange
        var builder = new MediatorBuilder();

        // Act
        builder.AddHandler<MultiHandler>();

        // Assert
        Assert.Equal(2, builder.Registrations.Count);
        Assert.True(builder.Registrations.ContainsKey(typeof(TestRequest)));
        Assert.True(builder.Registrations.ContainsKey(typeof(OtherRequest)));
    }

    [Fact]
    public void GivenVoidHandler_WhenAddHandler_ThenRegistersRequestType()
    {
        // Arrange
        var builder = new MediatorBuilder();

        // Act
        builder.AddHandler<VoidHandler>();

        // Assert
        Assert.Single(builder.Registrations);
        Assert.True(builder.Registrations.ContainsKey(typeof(VoidRequest)));
        Assert.Null(builder.Registrations[typeof(VoidRequest)].ResponseType);
    }

    [Fact]
    public void GivenMixedHandler_WhenAddHandler_ThenRegistersAllRequestTypes()
    {
        // Arrange
        var builder = new MediatorBuilder();

        // Act
        builder.AddHandler<MixedHandler>();

        // Assert
        Assert.Equal(2, builder.Registrations.Count);
        Assert.True(builder.Registrations.ContainsKey(typeof(TestRequest)));
        Assert.True(builder.Registrations.ContainsKey(typeof(VoidRequest)));
        Assert.NotNull(builder.Registrations[typeof(TestRequest)].ResponseType);
        Assert.Null(builder.Registrations[typeof(VoidRequest)].ResponseType);
    }

    [Fact]
    public void GivenResponseHandler_WhenAddHandlerWithConfigure_ThenRegistersRequestType()
    {
        // Arrange
        var builder = new MediatorBuilder();

        // Act
        builder.AddHandler<TestHandler, TestRequest>(handler => { });

        // Assert
        Assert.Single(builder.Registrations);
        Assert.True(builder.Registrations.ContainsKey(typeof(TestRequest)));
    }

    [Fact]
    public void GivenVoidHandler_WhenAddHandlerWithConfigure_ThenRegistersRequestType()
    {
        // Arrange
        var builder = new MediatorBuilder();

        // Act
        builder.AddHandler<VoidHandler, VoidRequest>(handler => { });

        // Assert
        Assert.Single(builder.Registrations);
        Assert.True(builder.Registrations.ContainsKey(typeof(VoidRequest)));
        Assert.Null(builder.Registrations[typeof(VoidRequest)].ResponseType);
    }

    // Note: Mismatched request type (e.g., AddHandler<TestHandler, VoidRequest>) is now a
    // compile-time error via the IHandlesRequest<TRequest> constraint — no runtime test needed.

    [Fact]
    public void GivenDuplicateRequestType_WhenAddHandlerWithConfigure_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new MediatorBuilder();
        builder.AddHandler<TestHandler>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddHandler<DuplicateHandler, TestRequest>(handler => { }));
    }

    private sealed record TestRequest(string Value) : IRequest<string>;
    private sealed record OtherRequest(int Value) : IRequest<int>;
    private sealed record VoidRequest : IRequest;

    private sealed class TestHandler : IRequestHandler<TestRequest, string>
    {
        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    private sealed class DuplicateHandler : IRequestHandler<TestRequest, string>
    {
        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    private sealed class MultiHandler : IRequestHandler<TestRequest, string>, IRequestHandler<OtherRequest, int>
    {
        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);

        public Task<int> HandleAsync(OtherRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    private sealed class VoidHandler : IRequestHandler<VoidRequest>
    {
        public Task HandleAsync(VoidRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class MixedHandler : IRequestHandler<TestRequest, string>, IRequestHandler<VoidRequest>
    {
        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);

        public Task HandleAsync(VoidRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
