using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Creators;

public static class CreatorVideoPricing
{
    /// <summary>The id a video's deck carries during a ranking pass, to match results back.</summary>
    public static string DraftSourceId(string videoId) => $"video:{videoId}";

    /// <summary>
    /// A video's format is only in its title, so its deck is priced under every format the
    /// app ranks and the most nearly legal result is kept: a legal one first, otherwise the
    /// one with fewest illegal cards. Ties go to <see cref="Formats.All"/> order.
    /// </summary>
    public static (FormatDefinition Format, DeckAnalysisResult Analysis) PickBest(
        IReadOnlyList<(FormatDefinition Format, DeckAnalysisResult Analysis)> perFormat)
    {
        if (perFormat.Count == 0) throw new ArgumentException("Nothing to pick from.", nameof(perFormat));

        return perFormat
            .Select((candidate, index) => (candidate, index))
            .OrderBy(x => x.candidate.Analysis.IllegalInFormat.Count)
            .ThenBy(x => FormatOrder(x.candidate.Format))
            .ThenBy(x => x.index)
            .First()
            .candidate;
    }

    private static int FormatOrder(FormatDefinition format)
    {
        for (var i = 0; i < Formats.All.Count; i++)
        {
            if (Formats.All[i].Key == format.Key) return i;
        }
        return int.MaxValue;
    }
}
