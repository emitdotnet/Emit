namespace Emit.Resilience;

/// <summary>
/// Resolves the effective <see cref="ResiliencePolicy"/> for a given producer and provider
/// based on the configured override hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// The resolver implements a 4-level override hierarchy:
/// <list type="number">
/// <item><description><b>Producer-level</b> (most specific) - policy configured for a specific producer</description></item>
/// <item><description><b>Provider-level</b> - policy configured for a provider type (e.g., all Kafka producers)</description></item>
/// <item><description><b>Global-level</b> - policy configured at the Emit registration level</description></item>
/// <item><description><b>Built-in default</b> - the fallback policy when no overrides are configured</description></item>
/// </list>
/// </para>
/// <para>
/// The most specific non-null policy wins. This allows fine-grained control over
/// resilience behavior while providing sensible defaults.
/// </para>
/// </remarks>
internal sealed class ResiliencePolicyResolver(
    IReadOnlyDictionary<string, ResiliencePolicy>? producerPolicies,
    IReadOnlyDictionary<string, ResiliencePolicy>? providerPolicies,
    ResiliencePolicy? globalPolicy)
{
    private readonly IReadOnlyDictionary<string, ResiliencePolicy> producerPolicies =
        producerPolicies ?? new Dictionary<string, ResiliencePolicy>();
    private readonly IReadOnlyDictionary<string, ResiliencePolicy> providerPolicies =
        providerPolicies ?? new Dictionary<string, ResiliencePolicy>();
    private readonly ResiliencePolicy? globalPolicy = globalPolicy;

    /// <summary>
    /// Resolves the effective resilience policy for the specified producer and provider.
    /// </summary>
    /// <param name="producerKey">
    /// The unique key identifying the producer. For Kafka, this is typically
    /// derived from the registration key and producer types.
    /// </param>
    /// <param name="providerId">
    /// The provider identifier (e.g., "kafka"). See <see cref="EmitConstants.Providers"/>.
    /// </param>
    /// <returns>
    /// The most specific <see cref="ResiliencePolicy"/> configured, or
    /// <see cref="ResiliencePolicy.Default"/> if no overrides are configured.
    /// </returns>
    public ResiliencePolicy Resolve(string producerKey, string providerId)
    {
        // Level 1: Producer-specific policy (most specific)
        if (producerPolicies.TryGetValue(producerKey, out var producerPolicy))
        {
            return producerPolicy;
        }

        // Level 2: Provider-level policy
        if (providerPolicies.TryGetValue(providerId, out var providerPolicy))
        {
            return providerPolicy;
        }

        // Level 3: Global-level policy
        if (globalPolicy is not null)
        {
            return globalPolicy;
        }

        // Level 4: Built-in default
        return ResiliencePolicy.Default;
    }

    /// <summary>
    /// Creates a new resolver with an additional producer-level policy.
    /// </summary>
    /// <param name="producerKey">The producer key.</param>
    /// <param name="policy">The policy to associate with the producer.</param>
    /// <returns>A new resolver with the added producer policy.</returns>
    public ResiliencePolicyResolver WithProducerPolicy(string producerKey, ResiliencePolicy policy)
    {
        var newProducerPolicies = new Dictionary<string, ResiliencePolicy>(producerPolicies)
        {
            [producerKey] = policy
        };
        return new ResiliencePolicyResolver(newProducerPolicies, providerPolicies, globalPolicy);
    }

    /// <summary>
    /// Creates a new resolver with an additional provider-level policy.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="policy">The policy to associate with the provider.</param>
    /// <returns>A new resolver with the added provider policy.</returns>
    public ResiliencePolicyResolver WithProviderPolicy(string providerId, ResiliencePolicy policy)
    {
        var newProviderPolicies = new Dictionary<string, ResiliencePolicy>(providerPolicies)
        {
            [providerId] = policy
        };
        return new ResiliencePolicyResolver(producerPolicies, newProviderPolicies, globalPolicy);
    }

    /// <summary>
    /// Creates a new resolver with a global-level policy.
    /// </summary>
    /// <param name="policy">The global policy.</param>
    /// <returns>A new resolver with the global policy.</returns>
    public ResiliencePolicyResolver WithGlobalPolicy(ResiliencePolicy policy)
    {
        return new ResiliencePolicyResolver(producerPolicies, providerPolicies, policy);
    }

    /// <summary>
    /// Creates an empty resolver that uses only the built-in default policy.
    /// </summary>
    /// <returns>A new resolver with no configured policies.</returns>
    public static ResiliencePolicyResolver CreateEmpty() =>
        new(null, null, null);
}
