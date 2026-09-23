using System.Globalization;
using System.Xml.Linq;

namespace MtgaCollectionAdvisor.Core.Creators;

/// <summary>
/// Reads a channel's public Atom feed. Pure, so it is tested against sample XML rather
/// than the network.
/// </summary>
public static class YouTubeFeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Yt = "http://www.youtube.com/xml/schemas/2015";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    public static IReadOnlyList<FeedVideo> Parse(string atomXml, CreatorChannel channel)
    {
        var root = XDocument.Parse(atomXml).Root;
        if (root is null) return [];

        var videos = new List<FeedVideo>();
        foreach (var entry in root.Elements(Atom + "entry"))
        {
            var videoId = entry.Element(Yt + "videoId")?.Value;
            if (string.IsNullOrWhiteSpace(videoId)) continue;

            var published = DateTimeOffset.TryParse(
                entry.Element(Atom + "published")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : DateTimeOffset.MinValue;

            videos.Add(new FeedVideo(
                Creator: channel.Name,
                VideoId: videoId.Trim(),
                Title: entry.Element(Atom + "title")?.Value ?? "",
                Published: published,
                Description: entry.Element(Media + "group")?.Element(Media + "description")?.Value ?? "",
                Language: channel.Language));
        }

        return videos;
    }
}

/// <summary>
/// A channel's public feed: the latest ~15 uploads with full descriptions, no API key and
/// no quota. It also fails at random (404/500), which callers must treat as "no news",
/// not as "no videos".
/// </summary>
public sealed class YouTubeFeedClient(HttpClient httpClient)
{
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MtgaCollectionAdvisor/1.0");
        return client;
    }

    /// <summary>Throws on any HTTP or XML failure.</summary>
    public async Task<IReadOnlyList<FeedVideo>> FetchAsync(CreatorChannel channel, CancellationToken ct = default)
    {
        var xml = await httpClient.GetStringAsync(
            $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(channel.ChannelId)}", ct);
        return YouTubeFeedParser.Parse(xml, channel);
    }
}
