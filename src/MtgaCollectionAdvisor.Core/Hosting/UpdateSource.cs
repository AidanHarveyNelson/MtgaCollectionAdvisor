namespace MtgaCollectionAdvisor.Core.Hosting;

/// <summary>
/// Where an installed copy looks for new versions: this repository's GitHub Releases, unless
/// <c>MTGA_ADVISOR_UPDATE_SOURCE</c> names a folder or URL, which is how the update loop is
/// tested without publishing a release.
/// </summary>
public static class UpdateSource
{
    public const string RepositoryUrl = "https://github.com/Dasayeve/MtgaCollectionAdvisor";
    public const string OverrideVariable = "MTGA_ADVISOR_UPDATE_SOURCE";

    /// <summary>The override to use, or null for GitHub Releases.</summary>
    public static string? OverrideFrom(string? environmentValue) =>
        string.IsNullOrWhiteSpace(environmentValue) ? null : environmentValue.Trim();
}
