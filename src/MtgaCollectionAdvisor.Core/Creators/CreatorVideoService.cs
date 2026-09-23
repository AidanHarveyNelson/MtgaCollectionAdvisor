using MtgaCollectionAdvisor.Core.Analysis;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Creators;

public sealed record CreatorVideoRefreshResult(
    IReadOnlyList<CreatorVideo> Videos,
    bool FromCache,
    int FailedFeeds,
    int ArchidektFetches);

/// <summary>
/// Brings creator videos in and prices their decks. The rules - what survives a refresh,
/// which format a deck is priced under - live in <see cref="CreatorVideoMerge"/> and
/// <see cref="CreatorVideoPricing"/>; this only does the I/O around them.
/// </summary>
public sealed class CreatorVideoService(
    YouTubeFeedClient feeds,
    ArchidektClient archidekt,
    CreatorVideoStore store,
    DeckRankingService ranking)
{
    /// <summary>
    /// The cached videos if the cache is fresh and <paramref name="force"/> is false;
    /// otherwise every curated feed is fetched and merged into the cache. A feed or
    /// Archidekt failure never throws: it leaves what the cache already knew.
    /// </summary>
    public async Task<CreatorVideoRefreshResult> LoadAsync(bool force, CancellationToken ct = default)
    {
        var cached = await store.LoadAsync(ct);
        if (!force && !cached.IsStale(DateTimeOffset.UtcNow))
        {
            return new CreatorVideoRefreshResult(cached.Videos, FromCache: true, FailedFeeds: 0, ArchidektFetches: 0);
        }

        var results = await Task.WhenAll(CreatorChannels.All.Select(async channel =>
        {
            try
            {
                return new ChannelFeedResult(channel, await feeds.FetchAsync(channel, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new ChannelFeedResult(channel, null);
            }
        }));

        var merged = CreatorVideoMerge.Merge(cached.Videos, results, CreatorChannels.All).ToList();

        var archidektFetches = 0;
        for (var i = 0; i < merged.Count; i++)
        {
            if (!merged[i].NeedsArchidektFetch) continue;

            archidektFetches++;
            // The format only decides legality flags on the fetched deck; the list itself is
            // re-priced under every format below, so Standard is as good as any.
            var deck = await archidekt.TryFetchDeckAsync(merged[i].ArchidektId!.Value, Formats.Standard, ct);
            if (deck is not null)
            {
                merged[i] = merged[i] with { Decklist = ArenaDeckListWriter.Write(deck) };
            }
        }

        await store.ReplaceAsync(new CreatorVideoSnapshot(DateTimeOffset.UtcNow, merged), ct);

        return new CreatorVideoRefreshResult(
            merged,
            FromCache: false,
            FailedFeeds: results.Count(r => r.Videos is null),
            ArchidektFetches: archidektFetches);
    }

    /// <summary>
    /// One ranking pass per format over every video's deck, never one per video: a pass
    /// shares its card-name lookups, so each card is resolved once, not once per deck.
    /// </summary>
    public async Task<IReadOnlyList<CreatorVideoCard>> PriceAsync(
        IReadOnlyList<CreatorVideo> videos, CollectionSnapshot collection, CancellationToken ct = default)
    {
        var drafts = videos
            .Where(v => v.Decklist is not null)
            .Select(v => new CandidateDeck(
                SourceId: CreatorVideoPricing.DraftSourceId(v.VideoId),
                Name: v.Title,
                Url: v.Url,
                FormatKey: Formats.Standard.Key,
                Popularity: 0,
                Cards: ArenaDeckListParser.Parse(v.Decklist!),
                FetchedAt: v.Published))
            .Where(d => d.Cards.Count > 0)
            .ToList();

        var perVideo = new Dictionary<string, List<(FormatDefinition, DeckAnalysisResult)>>(StringComparer.Ordinal);
        if (drafts.Count > 0)
        {
            foreach (var format in Formats.All)
            {
                foreach (var result in await ranking.RankAsync(format, drafts, collection, ct))
                {
                    if (!perVideo.TryGetValue(result.Deck.SourceId, out var list))
                    {
                        perVideo[result.Deck.SourceId] = list = [];
                    }
                    list.Add((format, result));
                }
            }
        }

        return videos
            .Select(v =>
            {
                if (!perVideo.TryGetValue(CreatorVideoPricing.DraftSourceId(v.VideoId), out var priced))
                {
                    return new CreatorVideoCard(v, null, null);
                }

                var (format, analysis) = CreatorVideoPricing.PickBest(priced);
                return new CreatorVideoCard(v, format, analysis);
            })
            .ToList();
    }
}
