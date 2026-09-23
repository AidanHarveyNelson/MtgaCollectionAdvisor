namespace MtgaCollectionAdvisor.Core.Creators;

/// <summary>One channel's feed result for a refresh. Videos is null when the fetch failed.</summary>
public sealed record ChannelFeedResult(CreatorChannel Channel, IReadOnlyList<FeedVideo>? Videos);

public static class CreatorVideoMerge
{
    /// <summary>
    /// The next cache, from the previous one and this refresh's feeds. Pure, so the rules
    /// that decide what survives a refresh are tested without the network:
    ///
    /// - a channel whose feed failed keeps its previous videos: YouTube's feeds return
    ///   404/500 at random, and reading that as "no videos" would wipe the channel;
    /// - a channel whose feed worked has exactly its current feed, which bounds the cache
    ///   to the ~15 latest uploads per channel;
    /// - a video already known keeps what was extracted from it (a published deck does not
    ///   change), with its title and date refreshed; a new one is extracted now;
    /// - a channel that also posts other content contributes only its Magic videos;
    /// - a creator no longer curated is dropped.
    ///
    /// Reading Archidekt is not done here: see <see cref="CreatorVideo.NeedsArchidektFetch"/>.
    /// </summary>
    public static IReadOnlyList<CreatorVideo> Merge(
        IReadOnlyList<CreatorVideo> previous,
        IReadOnlyList<ChannelFeedResult> results,
        IReadOnlyCollection<CreatorChannel> curated)
    {
        var curatedNames = curated.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var known = previous
            .GroupBy(v => v.VideoId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var failed = results
            .Where(r => r.Videos is null)
            .Select(r => r.Channel.Name)
            .ToHashSet(StringComparer.Ordinal);

        var merged = previous
            .Where(v => failed.Contains(v.Creator) && curatedNames.Contains(v.Creator))
            .ToList();

        foreach (var result in results)
        {
            if (result.Videos is null || !curatedNames.Contains(result.Channel.Name)) continue;

            foreach (var video in result.Videos)
            {
                if (known.TryGetValue(video.VideoId, out var stored))
                {
                    merged.Add(stored with
                    {
                        Creator = video.Creator,
                        Title = video.Title,
                        Published = video.Published,
                        Language = video.Language
                    });
                    continue;
                }

                var source = VideoDeckExtractor.Extract(video.Description);

                // A channel that also posts other things contributes only its Magic videos.
                if (result.Channel.PostsOtherContent && !CreatorVideoRelevance.IsMagicVideo(video, source)) continue;

                merged.Add(CreatorVideo.From(video, source));
            }
        }

        // A video cross-posted on two curated channels is one video.
        return merged
            .GroupBy(v => v.VideoId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }
}
