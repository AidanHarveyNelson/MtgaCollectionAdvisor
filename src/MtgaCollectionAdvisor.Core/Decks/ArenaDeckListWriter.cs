using System.Text;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Decks;

/// <summary>
/// Writes a deck back out in the same "Export Deck" text format MTGA's own deckbuilder
/// accepts on import, so a suggested deck can be copied straight into the game.
/// </summary>
public static class ArenaDeckListWriter
{
    public static string Write(CandidateDeck deck)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Deck");
        foreach (var card in deck.Cards.Where(c => c.Board == DeckBoard.Main))
        {
            sb.AppendLine($"{card.Quantity} {card.Name}");
        }

        var sideboard = deck.Cards.Where(c => c.Board == DeckBoard.Sideboard).ToList();
        if (sideboard.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Sideboard");
            foreach (var card in sideboard)
            {
                sb.AppendLine($"{card.Quantity} {card.Name}");
            }
        }

        return sb.ToString();
    }
}
