namespace Emit.Abstractions;

/// <summary>
/// Provides non-generic access to the message key's <see cref="System.Type"/>.
/// Set alongside <see cref="IKeyFeature{TKey}"/> on <see cref="MessageContext.Features"/>
/// so that transport-agnostic middleware can inspect the key type without knowing the generic parameter.
/// </summary>
public interface IKeyTypeFeature
{
    /// <summary>
    /// The CLR type of the message key.
    /// </summary>
    Type KeyType { get; }
}
