namespace Templar.Abstractions;

/// <summary>
/// The channels this build of Templar supports. Metadata only: it touches neither the store nor the
/// cache, so it is a singleton and needs no database provider.
/// </summary>
public interface ITemplateChannelService
{
    /// <summary>
    /// Every <see cref="TemplateChannel"/> as a value/label pair, ordered by value, so an admin
    /// screen can fill a channel picker without hard-coding the enum.
    /// </summary>
    IReadOnlyList<TemplateChannelInfo> GetAll();
}
