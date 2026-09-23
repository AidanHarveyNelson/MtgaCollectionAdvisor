using System.Text.RegularExpressions;

namespace MtgaCollectionAdvisor.Core.Creators;

/// <summary>
/// Whether a video from a channel that also posts other things is a Magic video. Only
/// consulted for channels marked <see cref="CreatorChannel.PostsOtherContent"/>: a
/// Magic-only channel's videos are all listed, deck or not.
/// </summary>
public static partial class CreatorVideoRelevance
{
    public static bool IsMagicVideo(FeedVideo video, VideoDeckSource source)
    {
        // A recoverable deck settles it; so does any deck-site link.
        if (source.Kind != DeckSourceKind.None) return true;

        // Titles carry the signal; only the start of a description is read, so a
        // channel's standing footer ("I also play Magic!") does not tag every video.
        var head = video.Description.Length > 300 ? video.Description[..300] : video.Description;
        return MagicTerms().IsMatch(video.Title) || MagicTerms().IsMatch(head);
    }

    // Deliberately Magic-specific: "deck", "arena" or "draft" alone would also catch other
    // card games and unrelated "arena" games on a mixed channel.
    [GeneratedRegex(
        @"\bmtga?\b|magic\W*(?:the\W*gathering|arena)|\bmagic\b.{0,40}\b(?:deck|standard|historic|hist[óo]rico|pioneer|brawl|alchemy|timeless|explorer|draft)\b|\b(?:mtg\s*)?arena\b.{0,20}\b(?:standard|historic|hist[óo]rico|brawl|bo1|bo3|m[íi]tico|mythic|ranqueada|ranked)\b|planeswalker|wildcards?\b|curingas?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MagicTerms();
}
