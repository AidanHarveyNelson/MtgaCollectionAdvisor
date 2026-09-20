namespace MtgaCollectionAdvisor.Core.Models;

/// <summary>
/// A constructed format the advisor can rank decks for. "Pioneer" is mapped to the
/// Scryfall "pioneer" legality list but results are restricted to cards that actually
/// exist on Arena (there is no native Pioneer queue - Explorer is Arena's equivalent).
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
        DisplayName: "Pioneer (Explorer no Arena)",
        ScryfallLegalityKey: "pioneer",
        MoxfieldFormatCode: "pioneer");

    public static readonly IReadOnlyList<FormatDefinition> All = [Standard, Pioneer];
}
