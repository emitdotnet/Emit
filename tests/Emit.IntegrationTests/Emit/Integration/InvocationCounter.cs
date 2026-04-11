namespace Emit.IntegrationTests.Integration;

/// <summary>
/// Thread-safe counter for tracking consumer handler invocations in tests.
/// Register as a singleton in the test's service collection.
/// </summary>
public sealed class InvocationCounter
{
    private int count;

    /// <summary>Gets the total invocation count so far.</summary>
    public int Count => Volatile.Read(ref count);

    /// <summary>Increments and returns the new count.</summary>
    public int Increment() => Interlocked.Increment(ref count);
}
