using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace SteppedOut.Tests;

// The app as the tests build it names its real source files in its debug info, so a code coverage
// report (dotnet test --collect "Code Coverage;Format=cobertura") can show them. An exe built with -r
// names /_/src instead, as in a git checkout, which keeps the build machine's folders out of it.
[TestClass]
public class DebugInfoTests
{
    [TestMethod]
    public void TheTestBuildNamesTheRealSourceFiles()
    {
        string dll = typeof(AppInfo).Assembly.Location;
        using var file = File.OpenRead(dll);
        using var pe = new PEReader(file);
        var embedded = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        using var provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
        var pdb = provider.GetMetadataReader();

        var sources = pdb.Documents.Select(handle => pdb.GetString(pdb.GetDocument(handle).Name))
            .Where(name => name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !name.Contains("/obj/", StringComparison.OrdinalIgnoreCase) && !name.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsGreaterThan(50, sources.Count);
        string lockEngine = sources.Single(name => name.EndsWith("LockEngine.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(Path.IsPathFullyQualified(lockEngine), lockEngine + " is a full path");
        Assert.IsTrue(File.Exists(lockEngine), lockEngine + " is the file on this disk");
    }

    // Built with -r from a copy of the source that has no git folder, as from GitHub's "Source code"
    // zip, the app names none of the folders it was built in: not in its debug info, not anywhere.
    // The folder's name has a comma and an equals sign, which the compiler's path map would read
    // as its own separators if they were not escaped, and the build would fail.
    [TestMethod]
    [DoNotParallelize]
    public void AnExeBuiltFromACopyWithoutGitNamesNoBuildFolder()
    {
        string token = "zip" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(TestRunFolders.Scratch, token + ",copy=1");
        try
        {
            string project = CopySource(root);
            for (var folder = new DirectoryInfo(root); folder is not null; folder = folder.Parent)
            {
                string git = Path.Combine(folder.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git)) Assert.Inconclusive("the scratch folder is inside a git checkout here");
            }

            string dll = Build(project);
            byte[] image = File.ReadAllBytes(dll);
            byte[] pdb = EmbeddedPdb(image);

            foreach (var (what, bytes) in new[] { ("the dll", image), ("its debug info", pdb) })
            {
                Assert.IsFalse(Contains(bytes, Encoding.UTF8.GetBytes(token)), what + " names the folder it was built in");
                Assert.IsFalse(Contains(bytes, Encoding.Unicode.GetBytes(token)), what + " names the folder it was built in");
            }

            using var provider = MetadataReaderProvider.FromPortablePdbImage(pdb.ToImmutableArray());
            var reader = provider.GetMetadataReader();
            var sources = reader.Documents.Select(handle => reader.GetString(reader.GetDocument(handle).Name))
                .Where(name => name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.IsGreaterThan(50, sources.Count);
            string lockEngine = sources.Single(name => name.EndsWith("LockEngine.cs", StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith("/_/src/", lockEngine.Replace('\\', '/'));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // The src folder without its build output, and the .editorconfig the zip has beside it.
    static string CopySource(string root)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "src", "SteppedOut.csproj"))) repo = repo.Parent;
        if (repo is null)
        {
            Assert.Inconclusive("the source is not next to the tests here");
            throw new InvalidOperationException();
        }
        string from = Path.Combine(repo.FullName, "src");
        string to = Path.Combine(root, "src");
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            string top = relative.Split(Path.DirectorySeparatorChar)[0];
            if (top.Equals("bin", StringComparison.OrdinalIgnoreCase) || top.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        string editorConfig = Path.Combine(repo.FullName, ".editorconfig");
        if (File.Exists(editorConfig)) File.Copy(editorConfig, Path.Combine(root, ".editorconfig"));
        return Path.Combine(to, "SteppedOut.csproj");
    }

    // Framework-dependent, so no runtime pack is needed; the build is otherwise the one -r gives.
    static string Build(string project)
    {
        string dotnetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : Path.Combine(dotnetRoot, "dotnet.exe");
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(project)!,
        };
        foreach (string argument in new[]
        {
            "build", project, "-c", "Release", "-r", "win-x64", "-p:SelfContained=false",
            "-nodeReuse:false", "--disable-build-servers", "-p:UseSharedCompilation=false", "--nologo", "-v", "q",
        }) start.ArgumentList.Add(argument);
        // The test run's own build settings must not leak into this one.
        foreach (string key in start.Environment.Keys.ToList())
        {
            if (key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase) || key.StartsWith("VSTEST", StringComparison.OrdinalIgnoreCase) || key.StartsWith("TESTINGPLATFORM", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(key);
        }
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Inconclusive("the build took over 5 minutes here");
        }
        string dll = Path.Combine(Path.GetDirectoryName(project)!, "obj", "Release", "net10.0-windows", "win-x64", "SteppedOut.dll");
        // A build that fails is the project's fault, such as a path map that breaks in some folders,
        // and fails the test; only a build that never finished says nothing either way.
        if (process.ExitCode != 0 || !File.Exists(dll))
        {
            string text = (stdout.Result + stderr.Result).Trim();
            Assert.Fail($"the copy did not build (exit {process.ExitCode}): {text[Math.Max(0, text.Length - 600)..]}");
        }
        return dll;
    }

    // The embedded debug info: "MPDB", its size, then the deflated portable PDB.
    static byte[] EmbeddedPdb(byte[] image)
    {
        using var pe = new PEReader(image.ToImmutableArray());
        var entry = pe.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        var data = image.AsSpan(entry.DataPointer, entry.DataSize);
        Assert.AreEqual("MPDB", Encoding.ASCII.GetString(data[..4]));
        int size = BitConverter.ToInt32(data[4..8]);
        using var inflate = new DeflateStream(new MemoryStream(data[8..].ToArray()), CompressionMode.Decompress);
        using var pdb = new MemoryStream(size);
        inflate.CopyTo(pdb);
        Assert.AreEqual(size, (int)pdb.Length);
        return pdb.ToArray();
    }

    static bool Contains(byte[] bytes, byte[] part) => bytes.AsSpan().IndexOf(part) >= 0;
}
