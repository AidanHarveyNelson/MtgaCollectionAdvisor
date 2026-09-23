using System.IO.Compression;
using System.Text;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Export;

/// <summary>
/// Every user deck at once: a zip with one Arena-format file per deck, under a folder per
/// format. Each file is exactly what <b>Copy for Arena</b> produces, so any one of them
/// pastes into the game or back into <b>Import deck</b> as it is.
/// </summary>
public static class UserDeckExportWriter
{
    private static readonly HashSet<char> Invalid = [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static byte[] WriteZip(IReadOnlyList<CandidateDeck> decks) =>
        WriteZip(decks.Select(d => new ZipEntryFile(d.FormatKey, d.Name, ArenaDeckListWriter.Write(d))));

    /// <summary>
    /// Any set of deck files, one folder each - shared by the user-deck and Arena-deck
    /// exports so both zips look the same.
    /// </summary>
    public static byte[] WriteZip(IEnumerable<ZipEntryFile> files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.OrderBy(f => f.Folder, StringComparer.Ordinal).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entry = zip.CreateEntry(UniquePath(file.Folder, file.Name, used), CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(file.Content);
            }
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// A deck name as a file name: characters Windows refuses become "_", and two decks with
    /// the same name in a format get " (2)", " (3)" rather than overwriting each other.
    /// </summary>
    public static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray())
            .Trim().TrimEnd('.', ' ');
        return cleaned.Length == 0 ? "deck" : cleaned;
    }

    private static string UniquePath(string folder, string name, HashSet<string> used)
    {
        var baseName = SafeFileName(name);
        var path = $"{folder}/{baseName}.txt";
        for (var n = 2; !used.Add(path); n++)
        {
            path = $"{folder}/{baseName} ({n}).txt";
        }
        return path;
    }
}

/// <summary>One file in a deck zip: the folder it goes in, the deck's name, and its text.</summary>
public sealed record ZipEntryFile(string Folder, string Name, string Content);
