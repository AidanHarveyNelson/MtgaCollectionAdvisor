using MtgaCollectionAdvisor.Core.Creators;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public class YouTubeFeedParserTests
{
    private static readonly CreatorChannel Channel = new("Crokeyz", "UCz3pj4DM9MuRWJ5nCCvBe_g");

    // Trimmed from a real channel feed; the shape (namespaces, media:group) is what matters.
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015"
              xmlns:media="http://search.yahoo.com/mrss/"
              xmlns="http://www.w3.org/2005/Atom">
          <title>CROKEYZ</title>
          <entry>
            <id>yt:video:v9XmRwiVUao</id>
            <yt:videoId>v9XmRwiVUao</yt:videoId>
            <title>PSA: Life Gain Aggro is Extremely Addictive!</title>
            <published>2026-09-22T15:00:12+00:00</published>
            <media:group>
              <media:title>PSA: Life Gain Aggro is Extremely Addictive!</media:title>
              <media:description>Deck
        4 Starting Town (FIN) 289</media:description>
            </media:group>
          </entry>
          <entry>
            <title>An entry with no video id</title>
            <published>2026-09-21T15:00:12+00:00</published>
          </entry>
        </feed>
        """;

    [Fact]
    public void Parse_Should_ReadIdTitleDateAndDescription()
    {
        var video = YouTubeFeedParser.Parse(Feed, Channel)[0];

        Assert.Equal("v9XmRwiVUao", video.VideoId);
        Assert.Equal("Crokeyz", video.Creator);
        Assert.Equal("PSA: Life Gain Aggro is Extremely Addictive!", video.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 15, 0, 12, TimeSpan.Zero), video.Published);
        Assert.Contains("4 Starting Town (FIN) 289", video.Description);
    }

    [Fact]
    public void Parse_Should_TagVideosWithTheChannelLanguage()
    {
        var video = YouTubeFeedParser.Parse(Feed, Channel with { Language = "pt" })[0];

        Assert.Equal("pt", video.Language);
    }

    [Fact]
    public void Parse_Should_SkipEntriesWithoutVideoId()
    {
        var videos = YouTubeFeedParser.Parse(Feed, Channel);

        Assert.Single(videos);
    }
}
