namespace Templar;

/// <summary>
/// Which parts of a template should be rendered. Skipping parts you do not need avoids
/// pointless rendering work (a large HTML body, for example).
/// </summary>
[Flags]
public enum TemplateParts
{
    None = 0,
    Subject = 1,
    Text = 2,
    Html = 4,
    All = Subject | Text | Html,
}
