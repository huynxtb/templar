namespace Templar;

/// <summary>
/// One stored template: a single (key, culture, channel) triple with its raw, un-rendered bodies.
/// </summary>
/// <remarks>
/// Declared as a record so callers can derive a variant with <c>with</c>, which is the common shape
/// of template editing: <c>existing with { Subject = "…" }</c>.
/// </remarks>
public sealed record TemplateDefinition
{
    /// <summary>Logical name shared by every culture, for example <c>welcome-user</c>.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>BCP-47 culture name, for example <c>en</c>, <c>vi</c> or <c>en-GB</c>.</summary>
    public required string Culture { get; init; }

    /// <summary>Channel this row belongs to.</summary>
    public TemplateChannel Channel { get; init; } = TemplateChannel.Email;

    /// <summary>
    /// Human-readable label for template management screens, for example
    /// <c>Welcome e-mail (Vietnamese)</c>. Metadata only: it is never rendered.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Note about what this template is for and when it is sent, so whoever edits it later has
    /// the context. Metadata only: it is never rendered.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>Subject line (e-mail) or title (in-app). May itself contain placeholders.</summary>
    public string? Subject { get; init; }

    /// <summary>Plain-text body. <see langword="null"/> when the template is HTML only.</summary>
    public string? TextBody { get; init; }

    /// <summary>HTML body. <see langword="null"/> when the template is text only.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Inactive rows are never resolved for rendering, but queries still return them.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>Last modification time, always stored in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public override string ToString() => $"{TemplateKey}/{Culture}/{Channel}";
}
