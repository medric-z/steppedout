using System.Text.RegularExpressions;

namespace SteppedOut.Tests;

// The GitHub workflows run third-party actions on the code that becomes the release. Each action is
// pinned to one commit, so a moved tag cannot change what runs, and no job can write to the
// repository unless it says so itself.
[TestClass]
public partial class WorkflowTests
{
    [GeneratedRegex(@"^\s*(?:-\s*)?uses:\s*(?<action>\S+)", RegexOptions.Multiline)]
    private static partial Regex Uses();

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+@[0-9a-f]{40}$")]
    private static partial Regex PinnedToACommit();

    static string Folder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string workflows = Path.Combine(folder.FullName, ".github", "workflows");
            if (Directory.Exists(workflows)) return workflows;
        }
        throw new InvalidOperationException(".github/workflows not found above the test folder");
    }

    static IEnumerable<string> Workflows() => Directory.GetFiles(Folder(), "*.yml");

    [TestMethod]
    public void EveryActionIsPinnedToACommit()
    {
        int found = 0;
        foreach (string file in Workflows())
        {
            foreach (Match use in Uses().Matches(File.ReadAllText(file)))
            {
                found++;
                string action = use.Groups["action"].Value;
                Assert.IsTrue(PinnedToACommit().IsMatch(action), $"{Path.GetFileName(file)}: {action} is pinned to a commit");
            }
        }
        Assert.IsGreaterThan(5, found, "the actions were found");
    }

    // The top level grants nothing that writes; a job that writes says so in its own block.
    [TestMethod]
    public void NoWorkflowGrantsWritingToEveryJob()
    {
        foreach (string file in Workflows())
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n");
            int jobs = text.IndexOf("\njobs:", StringComparison.Ordinal);
            Assert.IsGreaterThan(0, jobs, file);
            string top = text[..jobs];
            Assert.IsFalse(Regex.IsMatch(top, @":\s*write\b"), $"{Path.GetFileName(file)} grants write to every job:\n{top}");
        }
    }

    [TestMethod]
    public void OnlyTheReleaseJobWritesTheRepository()
    {
        string text = File.ReadAllText(Path.Combine(Folder(), "release.yml")).Replace("\r\n", "\n");
        string build = text[text.IndexOf("\n  build:", StringComparison.Ordinal)..text.IndexOf("\n  release:", StringComparison.Ordinal)];
        string release = text[text.IndexOf("\n  release:", StringComparison.Ordinal)..];

        StringAssert.Contains(build, "      contents: read\n");
        Assert.DoesNotContain("contents: write", build);
        StringAssert.Contains(release, "      contents: write\n");
    }
}
