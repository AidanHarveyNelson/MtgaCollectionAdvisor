using System.Text.RegularExpressions;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// Guards on the release workflow that no build would catch: a mistake in any of them only
/// shows once a release is in players' hands.
/// </summary>
public sealed class ReleaseWorkflowTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "release.yml"));

    // Velopack installs to %LOCALAPPDATA%\<packId> and deletes that folder on uninstall; with
    // the data folder's name, uninstalling would take the player's collection and decks with it.
    [Fact]
    public void ReleaseWorkflow_PackId_Should_DifferFromDataFolder()
    {
        var packId = Regex.Match(Workflow, @"--packId\s+(\S+)");

        Assert.True(packId.Success, "release.yml has no --packId.");
        Assert.NotEqual(Database.DataFolderName, packId.Groups[1].Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseWorkflow_VpkVersion_Should_MatchVelopackPackage()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "MtgaCollectionAdvisor.Web", "MtgaCollectionAdvisor.Web.csproj"));
        var package = Regex.Match(project, @"<PackageReference\s+Include=""Velopack""\s+Version=""([^""]+)""");
        var vpk = Regex.Match(Workflow, @"VPK_VERSION:\s*(\S+)");

        Assert.True(package.Success, "The Web project has no Velopack package reference.");
        Assert.True(vpk.Success, "release.yml sets no VPK_VERSION.");
        Assert.Equal(package.Groups[1].Value, vpk.Groups[1].Value);
    }

    // A pull request runs this workflow as a dry run; nothing may leave it but an artifact.
    [Fact]
    public void ReleaseWorkflow_Should_OnlyUploadFromTags()
    {
        var steps = Regex.Split(Workflow, @"\r?\n\s*- (?=name:|uses:|run:)");
        var publishing = steps.Where(s => Regex.IsMatch(s, @"vpk (upload|download) github|gh release")).ToList();

        Assert.NotEmpty(publishing);
        Assert.All(publishing, step => Assert.Matches(@"if:.*startsWith\(github\.ref, 'refs/tags/v'\)", step));
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MtgaCollectionAdvisor.slnx"))) return dir.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory);
    }
}
