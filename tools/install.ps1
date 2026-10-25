# Installs SteppedOut: downloads the latest release into %LOCALAPPDATA%\SteppedOut and starts it.
# Read it before running it: https://github.com/medric-z/steppedout/blob/main/tools/install.ps1
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 redraws the progress bar so often that it slows the download many times over.
$ProgressPreference = 'SilentlyContinue'

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$name = "SteppedOut-win-$arch.exe"
$release = 'https://github.com/medric-z/steppedout/releases/latest/download'
$dir = Join-Path $env:LOCALAPPDATA 'SteppedOut'
$exe = Join-Path $dir 'SteppedOut.exe'
$download = "$exe.download"
$sums = "$exe.sha256sums"

# A dropped connection or an error page is not a program: a Windows exe starts with MZ and points
# to a PE header.
function Test-Exe($path) {
    $head = New-Object byte[] 1024
    $stream = [IO.File]::OpenRead($path)
    try { $read = $stream.Read($head, 0, $head.Length) } finally { $stream.Dispose() }
    if ($read -lt 64 -or $head[0] -ne 0x4D -or $head[1] -ne 0x5A) { return $false }
    $pe = [BitConverter]::ToInt32($head, 0x3C)
    $pe -ge 64 -and $pe + 4 -le $read -and [BitConverter]::ToUInt32($head, $pe) -eq 0x4550
}

# Started from an admin PowerShell, the app would keep admin rights until sign-out and run the
# programs named in its config with them. Explorer starts it with the normal rights of this sign-in.
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
function Start-SteppedOut($path = $exe) {
    if ($elevated) { Start-Process explorer.exe -ArgumentList "`"$path`"" } else { Start-Process $path }
}

New-Item -ItemType Directory -Force -Path $dir | Out-Null
$session = (Get-Process -Id $PID).SessionId

# The new file is downloaded and checked next to the installed one before anything is stopped, so
# a failure on the way leaves the installed copy as it was.
$running = @()
$stopped = $null
try {
    Invoke-WebRequest "$release/$name" -OutFile $download -UseBasicParsing
    if (-not (Test-Exe $download)) { throw "The download is not a Windows program. Nothing was changed; run this again." }

    # Each release lists the SHA256 of its exes in SHA256SUMS.txt, and the download has to be listed
    # there with the same hash. Without that list it is not installed: every release has one.
    $listed = $null
    try {
        Invoke-WebRequest "$release/SHA256SUMS.txt" -OutFile $sums -UseBasicParsing
    } catch {
        throw "Could not fetch SHA256SUMS.txt, so the download could not be checked. Nothing was changed; run this again."
    }
    foreach ($line in Get-Content $sums) {
        $hash, $file = $line.Trim() -split '\s+', 2
        if ($file -and $file.TrimStart('*') -eq $name) { $listed = $hash }
    }
    if (-not $listed) { throw "SHA256SUMS.txt does not list $name. Nothing was changed." }
    if ((Get-FileHash $download -Algorithm SHA256).Hash -ne $listed) {
        throw "The download does not match its SHA256 in SHA256SUMS.txt. Nothing was changed; run this again."
    }

    # Only one copy runs per sign-in, so the one running in this sign-in is stopped, whatever folder
    # it was started from. A copy downloaded by hand runs under its file name, SteppedOut-win-x64 or
    # a browser's "SteppedOut-win-x64 (1)", so any name starting SteppedOut is looked at, and it is
    # stopped only when its file says it is SteppedOut, never another program whose name starts the
    # same. A copy whose file cannot be read runs with admin rights; stopping it fails below and
    # says so. Other people signed in on this PC run their own, which this account cannot stop.
    $running = @(Get-Process -Name 'SteppedOut*' -ErrorAction SilentlyContinue | Where-Object {
        $_.SessionId -eq $session -and (-not $_.Path -or (Get-Item -LiteralPath $_.Path -ErrorAction SilentlyContinue).VersionInfo.ProductName -eq 'SteppedOut')
    })
    $stopped = @($running | ForEach-Object { $_.Path } | Where-Object { $_ })[0]
    try { $running | Stop-Process -Force }
    catch { $running = @(); throw "A copy of SteppedOut is running with admin rights, and this PowerShell is not allowed to stop it. Nothing was changed. Sign out and back in, then run this line again, or run it from a PowerShell opened as administrator." }
    $running | Wait-Process -Timeout 10
    Move-Item -Force $download $exe
} catch {
    Remove-Item $download -ErrorAction SilentlyContinue
    # Put back what was running: the installed copy, or on a first install the copy that was stopped.
    if ($running.Count -gt 0) {
        if (Test-Path $exe) { Start-SteppedOut } elseif ($stopped) { Start-SteppedOut $stopped }
    }
    throw
} finally {
    Remove-Item $sums -ErrorAction SilentlyContinue
}

# "Start with Windows" may still start a copy in another folder, such as one downloaded by hand, so
# an entry that is there is pointed at this copy, keeping any arguments such as --dry-run. With no
# entry, starting with Windows stays off.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).SteppedOut
if ($command -is [string] -and $command) {
    $arguments = if ($command.Trim() -match '^(?:"[^"]*"?|.*?\.exe)(.*)$') { $Matches[1].Trim() } else { '' }
    $value = "`"$exe`" $arguments".TrimEnd()
    # Some security programs block writes to this key. The new copy is in place either way, so it
    # still gets started; only the entry is left as it was.
    try { Set-ItemProperty $runKey -Name SteppedOut -Value $value }
    catch { Write-Warning "Could not point Start with Windows at $exe, so it still starts the old copy: $($_.Exception.Message)" }
}

Start-SteppedOut
"SteppedOut is running from $exe. Look for the padlock next to the clock; click it for settings."
