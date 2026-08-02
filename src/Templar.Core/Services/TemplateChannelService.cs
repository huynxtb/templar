using Templar.Abstractions;

namespace Templar.Services;

/// <summary>
/// Default implementation, read straight off <see cref="TemplateChannel"/> — a member added to the
/// enum shows up here without any further change.
/// </summary>
public sealed class TemplateChannelService : ITemplateChannelService
{
    private static readonly TemplateChannelInfo[] Channels =
        [.. Enum.GetValues<TemplateChannel>().Select(channel => new TemplateChannelInfo((int)channel, channel.ToString()))];

    public IReadOnlyList<TemplateChannelInfo> GetAll() => Channels;
}
