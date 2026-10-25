using System.Diagnostics;

namespace SteppedOut.Tests;

// install.ps1, run by Windows PowerShell with every command that would reach out stood in for:
// nothing is downloaded, no process is looked at, stopped or started, and the registry is not
// touched. The install folder is a scratch one. A stand-in that is not in place stops the run
// before the script starts, so a mistake here can never stop a real program.
[TestClass]
public class InstallScriptTests
{
    const string Driver = """
        param($Install, $Fake, $Mode, $Product, $Other, $Folder)
        $ErrorActionPreference = 'Stop'
        $global:installLog = New-Object System.Collections.Generic.List[string]
        function Invoke-WebRequest {
            param($Uri, $OutFile, [switch]$UseBasicParsing)
            $file = ($Uri -split '/')[-1]
            $global:installLog.Add("download $file")
            if ($file -eq 'SHA256SUMS.txt') {
                if ($Mode -eq 'no list') { throw 'The remote server returned an error: (404) Not Found.' }
                try {
                    $hash = (Get-FileHash $Fake -Algorithm SHA256).Hash
                    Set-Content -Path $OutFile -Value @("$hash  SteppedOut-win-x64.exe", "$hash  SteppedOut-win-arm64.exe")
                } catch { $global:installLog.Add('stand-in: ' + $_.Exception.Message); throw }
            } else {
                Copy-Item $Fake $OutFile
            }
        }
        function Get-Process {
            param($Name, $Id, $ErrorAction)
            if ($Id) { return [pscustomobject]@{ SessionId = 1 } }
            @(
                [pscustomobject]@{ Name = 'SteppedOut-win-x64 (1)'; SessionId = 1; Path = $Product },
                [pscustomobject]@{ Name = 'SteppedOut.Helper'; SessionId = 1; Path = $Other },
                [pscustomobject]@{ Name = 'SteppedOut'; SessionId = 2; Path = $Product }
            )
        }
        function Stop-Process { param([Parameter(ValueFromPipeline = $true)]$InputObject, [switch]$Force) process { $global:installLog.Add("stop $($InputObject.Name)") } }
        function Wait-Process { param([Parameter(ValueFromPipeline = $true)]$InputObject, $Timeout) process { } }
        function Start-Process { param($FilePath, $ArgumentList) $global:installLog.Add('start') }
        function Get-ItemProperty { param($Path, $ErrorAction) $null }
        function Set-ItemProperty { param($Path, $Name, $Value) $global:installLog.Add('set the Run value') }
        foreach ($name in 'Invoke-WebRequest', 'Get-Process', 'Stop-Process', 'Wait-Process', 'Start-Process', 'Get-ItemProperty', 'Set-ItemProperty') {
            if ((Get-Command $name).CommandType -ne 'Function') { 'a stand-in is missing: ' + $name; exit 3 }
        }
        $env:LOCALAPPDATA = $Folder
        try {
            & $Install | Out-Null
            $global:installLog.Add('installed')
        } catch {
            $global:installLog.Add('error: ' + $_.Exception.Message)
        }
        $global:installLog
        """;

    string dir = "";

    [TestInitialize]
    public void Setup()
    {
        dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(dir, recursive: true);

    static string Script()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string install = Path.Combine(folder.FullName, "install.ps1");
            if (File.Exists(install)) return install;
        }
        throw new InvalidOperationException("install.ps1 not found above the test folder");
    }

    // The lines the run logged, in order.
    List<string> Run(string mode)
    {
        string driver = Path.Combine(dir, "driver.ps1");
        File.WriteAllText(driver, Driver);
        // A Windows program to stand for the download: the app's own dll, which is never started.
        string fake = typeof(AppInfo).Assembly.Location;
        string other = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Started from PowerShell 7, as on GitHub's machine, the tests pass on its module path, where
        // Windows PowerShell finds no Get-FileHash. Without it, Windows PowerShell uses its own.
        start.Environment.Remove("PSModulePath");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", driver,
            "-Install", Script(), "-Fake", fake, "-Mode", mode, "-Product", fake, "-Other", other, "-Folder", Path.Combine(dir, "Local") })
        {
            start.ArgumentList.Add(arg);
        }
        using var run = Process.Start(start)!;
        var error = run.StandardError.ReadToEndAsync();
        string output = run.StandardOutput.ReadToEnd();
        Assert.IsTrue(run.WaitForExit(TimeSpan.FromSeconds(60)), "PowerShell finished");
        Assert.AreEqual(0, run.ExitCode, output + error.Result);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    string Installed => Path.Combine(dir, "Local", "SteppedOut", "SteppedOut.exe");

    [TestMethod]
    public void WithoutTheSha256ListNothingIsInstalled()
    {
        var log = Run("no list");

        CollectionAssert.AreEqual(new[]
        {
            "download " + Name,
            "download SHA256SUMS.txt",
            "error: Could not fetch SHA256SUMS.txt, so the download could not be checked. Nothing was changed; run this again.",
        }, log);
        Assert.IsFalse(File.Exists(Installed), "nothing installed");
        CollectionAssert.AreEqual(Array.Empty<string>(), Directory.GetFiles(Path.Combine(dir, "Local", "SteppedOut")), "no download left behind");
    }

    // Only SteppedOut in this sign-in is stopped: another program whose name starts the same, or
    // SteppedOut in another sign-in, is left running.
    [TestMethod]
    public void OnlySteppedOutItselfIsStopped()
    {
        var log = Run("ok");

        CollectionAssert.AreEqual(new[]
        {
            "download " + Name,
            "download SHA256SUMS.txt",
            "stop SteppedOut-win-x64 (1)",
            "start",
            "installed",
        }, log, string.Join(" | ", log));
        Assert.IsTrue(File.Exists(Installed));
    }

    static string Name => $"SteppedOut-win-{(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "ARM64" ? "arm64" : "x64")}.exe";
}
