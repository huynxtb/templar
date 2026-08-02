using Templar.Services;
using Xunit;

namespace Templar.Tests;

public class TemplateChannelServiceTests
{
    [Fact]
    public void Lists_every_channel_with_its_stored_value_and_name()
    {
        var channels = new TemplateChannelService().GetAll();

        Assert.Equal(Enum.GetValues<TemplateChannel>().Length, channels.Count);
        Assert.Equal(new TemplateChannelInfo(0, "Email"), channels[0]);
        Assert.Contains(new TemplateChannelInfo((int)TemplateChannel.Zalo, "Zalo"), channels);
    }

    [Fact]
    public void Orders_by_value_and_keeps_Other_last()
    {
        var channels = new TemplateChannelService().GetAll();

        Assert.Equal(channels.OrderBy(c => c.Value), channels);
        Assert.Equal("Other", channels[^1].Label);
    }

    [Fact]
    public void Labels_match_what_the_channel_column_stores()
    {
        var channels = new TemplateChannelService().GetAll();

        Assert.All(channels, channel =>
            Assert.Equal(((TemplateChannel)channel.Value).ToString(), channel.Label));
    }
}
