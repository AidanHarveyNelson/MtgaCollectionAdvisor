namespace MtgaCollectionAdvisor.Core.Models;

/// <summary>
/// A constructed format the advisor can rank decks for. Legality comes from Scryfall, but
/// results are restricted to cards that actually exist on Arena.
/// </summary>
public sealed record FormatDefinition(
    string Key,
    string DisplayName,
    string ScryfallLegalityKey,
    string MoxfieldFormatCode);

public static class Formats
{
    public static readonly FormatDefinition Standard = new(
        Key: "standard",
        DisplayName: "Standard",
        ScryfallLegalityKey: "standard",
        MoxfieldFormatCode: "standard");

    public static readonly FormatDefinition Pioneer = new(
        Key: "pioneer",
        DisplayName: "Pioneer",
        ScryfallLegalityKey: "pioneer",
        MoxfieldFormatCode: "pioneer");

    public static readonly IReadOnlyList<FormatDefinition> All = [Standard, Pioneer];
}
