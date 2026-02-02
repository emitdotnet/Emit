namespace Emit.Tests.Resilience;

using Emit.Resilience;
using Xunit;

public class ResiliencePolicyResolverTests
{
    private readonly ResiliencePolicy producerPolicy;
    private readonly ResiliencePolicy providerPolicy;
    private readonly ResiliencePolicy globalPolicy;

    public ResiliencePolicyResolverTests()
    {
        producerPolicy = new ResiliencePolicy
        {
            MaxRetryCount = 3,
            BackoffStrategy = BackoffStrategy.FixedInterval,
            BackoffBaseDelay = TimeSpan.FromSeconds(1),
            MaxBackoffDelay = TimeSpan.FromMinutes(1),
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(1)
        };

        providerPolicy = new ResiliencePolicy
        {
            MaxRetryCount = 5,
            BackoffStrategy = BackoffStrategy.Exponential,
            BackoffBaseDelay = TimeSpan.FromSeconds(2),
            MaxBackoffDelay = TimeSpan.FromMinutes(3),
            CircuitBreakerFailureThreshold = 3,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(5)
        };

        globalPolicy = new ResiliencePolicy
        {
            MaxRetryCount = 7,
            BackoffStrategy = BackoffStrategy.Exponential,
            BackoffBaseDelay = TimeSpan.FromSeconds(3),
            MaxBackoffDelay = TimeSpan.FromMinutes(7),
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(10)
        };
    }

    [Fact]
    public void GivenProducerPolicy_WhenResolving_ThenProducerPolicyTakesPrecedence()
    {
        // Arrange
        var producerPolicies = new Dictionary<string, ResiliencePolicy>
        {
            ["producer-1"] = producerPolicy
        };
        var providerPolicies = new Dictionary<string, ResiliencePolicy>
        {
            ["kafka"] = providerPolicy
        };
        var resolver = new ResiliencePolicyResolver(producerPolicies, providerPolicies, globalPolicy);

        // Act
        var result = resolver.Resolve("producer-1", "kafka");

        // Assert
        Assert.Same(producerPolicy, result);
    }

    [Fact]
    public void GivenNoProducerPolicy_WhenResolving_ThenProviderPolicyUsed()
    {
        // Arrange
        var providerPolicies = new Dictionary<string, ResiliencePolicy>
        {
            ["kafka"] = providerPolicy
        };
        var resolver = new ResiliencePolicyResolver(null, providerPolicies, globalPolicy);

        // Act
        var result = resolver.Resolve("producer-1", "kafka");

        // Assert
        Assert.Same(providerPolicy, result);
    }

    [Fact]
    public void GivenNoProducerOrProviderPolicy_WhenResolving_ThenGlobalPolicyUsed()
    {
        // Arrange
        var resolver = new ResiliencePolicyResolver(null, null, globalPolicy);

        // Act
        var result = resolver.Resolve("producer-1", "kafka");

        // Assert
        Assert.Same(globalPolicy, result);
    }

    [Fact]
    public void GivenNoConfiguredPolicies_WhenResolving_ThenDefaultPolicyUsed()
    {
        // Arrange
        var resolver = ResiliencePolicyResolver.CreateEmpty();

        // Act
        var result = resolver.Resolve("producer-1", "kafka");

        // Assert
        Assert.Same(ResiliencePolicy.Default, result);
    }

    [Fact]
    public void GivenDifferentProducerKeys_WhenResolving_ThenCorrectPolicyReturned()
    {
        // Arrange
        var otherProducerPolicy = new ResiliencePolicy
        {
            MaxRetryCount = 99,
            BackoffStrategy = BackoffStrategy.FixedInterval,
            BackoffBaseDelay = TimeSpan.FromSeconds(1),
            MaxBackoffDelay = TimeSpan.FromMinutes(1),
            CircuitBreakerFailureThreshold = 1,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(1)
        };
        var producerPolicies = new Dictionary<string, ResiliencePolicy>
        {
            ["producer-1"] = producerPolicy,
            ["producer-2"] = otherProducerPolicy
        };
        var resolver = new ResiliencePolicyResolver(producerPolicies, null, null);

        // Act
        var result1 = resolver.Resolve("producer-1", "kafka");
        var result2 = resolver.Resolve("producer-2", "kafka");

        // Assert
        Assert.Equal(3, result1.MaxRetryCount);
        Assert.Equal(99, result2.MaxRetryCount);
    }

    [Fact]
    public void GivenDifferentProviders_WhenResolving_ThenCorrectPolicyReturned()
    {
        // Arrange
        var rabbitPolicy = new ResiliencePolicy
        {
            MaxRetryCount = 15,
            BackoffStrategy = BackoffStrategy.FixedInterval,
            BackoffBaseDelay = TimeSpan.FromSeconds(1),
            MaxBackoffDelay = TimeSpan.FromMinutes(1),
            CircuitBreakerFailureThreshold = 4,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(3)
        };
        var providerPolicies = new Dictionary<string, ResiliencePolicy>
        {
            ["kafka"] = providerPolicy,
            ["rabbitmq"] = rabbitPolicy
        };
        var resolver = new ResiliencePolicyResolver(null, providerPolicies, null);

        // Act
        var kafkaResult = resolver.Resolve("any-producer", "kafka");
        var rabbitResult = resolver.Resolve("any-producer", "rabbitmq");

        // Assert
        Assert.Equal(5, kafkaResult.MaxRetryCount);
        Assert.Equal(15, rabbitResult.MaxRetryCount);
    }

    [Fact]
    public void GivenUnknownProducerAndProvider_WhenResolving_ThenFallsBackToGlobal()
    {
        // Arrange
        var producerPolicies = new Dictionary<string, ResiliencePolicy>
        {
            ["known-producer"] = producerPolicy
        };
        var providerPolicies = new Dictionary<string, ResiliencePolicy>
        {
            ["known-provider"] = providerPolicy
        };
        var resolver = new ResiliencePolicyResolver(producerPolicies, providerPolicies, globalPolicy);

        // Act
        var result = resolver.Resolve("unknown-producer", "unknown-provider");

        // Assert
        Assert.Same(globalPolicy, result);
    }

    [Fact]
    public void GivenWithProducerPolicy_WhenCreatingNew_ThenNewResolverHasPolicy()
    {
        // Arrange
        var resolver = ResiliencePolicyResolver.CreateEmpty();

        // Act
        var newResolver = resolver.WithProducerPolicy("producer-1", producerPolicy);
        var result = newResolver.Resolve("producer-1", "kafka");

        // Assert
        Assert.Same(producerPolicy, result);
    }

    [Fact]
    public void GivenWithProviderPolicy_WhenCreatingNew_ThenNewResolverHasPolicy()
    {
        // Arrange
        var resolver = ResiliencePolicyResolver.CreateEmpty();

        // Act
        var newResolver = resolver.WithProviderPolicy("kafka", providerPolicy);
        var result = newResolver.Resolve("any-producer", "kafka");

        // Assert
        Assert.Same(providerPolicy, result);
    }

    [Fact]
    public void GivenWithGlobalPolicy_WhenCreatingNew_ThenNewResolverHasPolicy()
    {
        // Arrange
        var resolver = ResiliencePolicyResolver.CreateEmpty();

        // Act
        var newResolver = resolver.WithGlobalPolicy(globalPolicy);
        var result = newResolver.Resolve("any-producer", "any-provider");

        // Assert
        Assert.Same(globalPolicy, result);
    }

    [Fact]
    public void GivenImmutableResolver_WhenWithMethod_ThenOriginalUnchanged()
    {
        // Arrange
        var originalResolver = ResiliencePolicyResolver.CreateEmpty();

        // Act
        var newResolver = originalResolver.WithProducerPolicy("producer-1", producerPolicy);
        var originalResult = originalResolver.Resolve("producer-1", "kafka");
        var newResult = newResolver.Resolve("producer-1", "kafka");

        // Assert
        Assert.Same(ResiliencePolicy.Default, originalResult);
        Assert.Same(producerPolicy, newResult);
    }
}
