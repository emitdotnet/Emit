namespace Emit.EntityFrameworkCore.Models;

/// <summary>
/// Represents a message a consumer handled, recorded in the inbox.
/// </summary>
internal sealed class InboxEntity
{
    /// <summary>
    /// Gets or sets the message id.
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identity of the consumer that handled the message.
    /// </summary>
    public string Handler { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the message was handled (UTC).
    /// </summary>
    public DateTime HandledAt { get; set; }

    /// <summary>
    /// Gets or sets when the record expires (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
