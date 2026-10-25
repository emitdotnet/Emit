namespace Emit.MongoDB.Tests.TestInfrastructure;

using Xunit;

/// <summary>
/// The test classes that configure the <c>failCommand</c> fail point. It is one setting for the whole server, shared
/// by every test class through <see cref="MongoDbContainerFixture"/>, and each configuration replaces the previous one,
/// so these classes run one at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MongoDbFailPointCollection
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public const string Name = "MongoDB fail point";
}
