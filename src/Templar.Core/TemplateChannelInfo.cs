namespace Templar;

/// <summary>
/// One <see cref="TemplateChannel"/> as a pair a UI can bind to: the stored numeric value and the
/// member name.
/// </summary>
/// <param name="Value">Numeric value of the member, for example <c>0</c> for <c>Email</c>.</param>
/// <param name="Label">Member name, which is also what the <c>channel</c> column stores.</param>
public sealed record TemplateChannelInfo(int Value, string Label);
