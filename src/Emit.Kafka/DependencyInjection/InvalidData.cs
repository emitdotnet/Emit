namespace Emit.Kafka.DependencyInjection;

/// <summary>
/// Rules to pass as <c>isInvalidData</c> when setting an async deserializer, telling which of its failures mean the
/// bytes are invalid.
/// </summary>
public static class InvalidData
{
    /// <summary>
    /// Every failure means invalid data: for a deserializer that calls no service, whose failures can only come from
    /// the bytes.
    /// </summary>
    /// <param name="exception">The exception the deserializer threw.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool Always(Exception exception) => true;
}
