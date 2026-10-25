<#
.SYNOPSIS
Builds the Microsoft Store upload for SteppedOut: an x64 and an arm64 package bundled into one
.msixupload. The packages are unsigned; the Store signs them.

.DESCRIPTION
Run it from any folder with Windows PowerShell 5.1, the one built into Windows:

  powershell -ExecutionPolicy Bypass -File tools\store\build.ps1 -Name <Package/Identity/Name> `
      -Publisher "<Package/Identity/Publisher>" -DisplayName "<Package/Properties/PublisherDisplayName>"

The three values come from Partner Center, the app's Product management, Product identity page.

Steps: publish the app for win-x64 and win-arm64, draw the icons from the app's logo code, lay out
each package with the filled-in manifest, index its resources with makepri, pack it with makeappx
(which checks the manifest), bundle both, put the bundle in the .msixupload, then unpack everything
again to check it reads back.

makeappx and makepri come from an installed Windows SDK when there is one. Otherwise they come from
the Microsoft.Windows.SDK.BuildTools package on nuget.org, pinned in tools/store/StoreIcons.csproj:
the restore runs in locked mode against the content hash in packages.lock.json, the package's
signature must verify as Microsoft's, and both tools must carry a valid Microsoft signature.

Nothing here installs a certificate, turns on Developer Mode or changes a setting.

.PARAMETER Name
Package/Identity/Name from the Product identity page, such as 12345Publisher.SteppedOut.

.PARAMETER Publisher
Package/Identity/Publisher from the Product identity page, starting with CN=.

.PARAMETER DisplayName
Package/Properties/PublisherDisplayName from the Product identity page.

.PARAMETER AppName
The name reserved in Partner Center. SteppedOut unless another name had to be reserved.

.PARAMETER Version
Package version. Defaults to the Version in src/SteppedOut.csproj with a fourth part of 0.

.PARAMETER Out
Output folder. Defaults to publish\store in the repository, which git ignores.

.PARAMETER Dotnet
The dotnet executable. Defaults to the one on PATH.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$Publisher,
    [Parameter(Mandatory = $true)][string]$DisplayName,
    [string]$AppName = 'SteppedOut',
    [string]$Version,
    [string]$Out,
    [string]$Dotnet
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$app = Join-Path $repo 'src\SteppedOut.csproj'
$icons = Join-Path $PSScriptRoot 'StoreIcons.csproj'
$template = Join-Path $repo 'tools\store\Package.appxmanifest.template'
$architectures = [ordered]@{ 'win-x64' = 'x64'; 'win-arm64' = 'arm64' }
$marker = '.store-build'

function Fail([string]$message) {
    Write-Host "build.ps1: $message" -ForegroundColor Red
    exit 1
}

function Step([string]$message) {
    Write-Host ''
    Write-Host "== $message" -ForegroundColor Cyan
}

# Runs a program, shows its output, and stops the build if it fails.
function Invoke-Tool([string]$exe, [string[]]$arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $exe @arguments 2>&1 | ForEach-Object { "$_" } | Out-Host
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $saved
    }
    if ($code -ne 0) { Fail "$([IO.Path]::GetFileName($exe)) failed with exit code $code" }
}

# Same, but returns the output instead of showing it.
function Read-Tool([string]$exe, [string[]]$arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $exe @arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $saved
    }
    if ($code -ne 0) {
        $output | Out-Host
        Fail "$([IO.Path]::GetFileName($exe)) failed with exit code $code"
    }
    return $output
}

function Assert-MicrosoftSigned([string]$path) {
    $signature = Get-AuthenticodeSignature -FilePath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        Fail "$path is not validly signed by Microsoft ($($signature.Status))"
    }
}

function Write-Utf8([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
}

# ---- Inputs ----

if ($Name -notmatch '^[A-Za-z0-9.-]{3,50}$') { Fail "-Name '$Name' is not a package name (3 to 50 letters, digits, dots or dashes)" }
if ($Publisher -notmatch '^CN=') { Fail "-Publisher must be the full value from Partner Center, starting with CN=" }
if ([string]::IsNullOrWhiteSpace($DisplayName)) { Fail '-DisplayName is empty' }
if ([string]::IsNullOrWhiteSpace($AppName)) { Fail '-AppName is empty' }

if (-not $Version) {
    $projectVersion = ([xml](Get-Content -Raw -Encoding UTF8 $app)).Project.PropertyGroup |
        ForEach-Object { $_.SelectSingleNode('Version') } | Where-Object { $_ } | Select-Object -First 1
    if (-not $projectVersion) { Fail "no <Version> in $app" }
    $Version = $projectVersion.InnerText.Trim()
}
if ($Version -match '^\d+\.\d+\.\d+$') { $Version = "$Version.0" }
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { Fail "version '$Version' must be a.b.c or a.b.c.0; the Store keeps the fourth part for itself" }

if (-not $Dotnet) {
    $found = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $found) { Fail 'dotnet was not found on PATH; pass -Dotnet with its full path' }
    $Dotnet = $found.Source
}

if (-not $Out) { $Out = Join-Path $repo 'publish\store' }
$Out = [IO.Path]::GetFullPath($Out)

# Only a folder this script made earlier is emptied; anything else must not already have files in it.
if (Test-Path $Out) {
    if (Test-Path (Join-Path $Out $marker)) {
        Remove-Item -Recurse -Force -LiteralPath $Out
    } elseif (Get-ChildItem -Force -LiteralPath $Out | Select-Object -First 1) {
        Fail "$Out has files this script did not make; choose an empty folder with -Out"
    }
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Write-Utf8 (Join-Path $Out $marker) "made by tools/store/build.ps1; the next build empties this folder`n"

Write-Host "SteppedOut Store build $Version"
Write-Host "  identity: $Name"
Write-Host "  publisher: $Publisher ($DisplayName)"
Write-Host "  output: $Out"

# ---- Tools ----

Step 'Restoring the icon tool and the pinned packaging tools (locked mode)'
Invoke-Tool $Dotnet @('restore', $icons, '--locked-mode')

$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$makeappx = $null
$makepri = $null
$kits = if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin' } else { $null }
if ($kits -and (Test-Path $kits)) {
    $sdk = Get-ChildItem -Directory -LiteralPath $kits | Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Where-Object { (Test-Path (Join-Path $_.FullName "$hostArch\makeappx.exe")) -and (Test-Path (Join-Path $_.FullName "$hostArch\makepri.exe")) } |
        Select-Object -First 1
    if ($sdk) {
        $makeappx = Join-Path $sdk.FullName "$hostArch\makeappx.exe"
        $makepri = Join-Path $sdk.FullName "$hostArch\makepri.exe"
        Write-Host "Using the installed Windows SDK $($sdk.Name)"
    }
}
if (-not $makeappx) {
    $package = (Read-Tool $Dotnet @('msbuild', $icons, '-nologo', '-getProperty:PkgMicrosoft_Windows_SDK_BuildTools') | Select-Object -Last 1).Trim()
    if (-not $package -or -not (Test-Path $package)) { Fail 'the Microsoft.Windows.SDK.BuildTools package was not restored' }
    $nupkg = Get-ChildItem -LiteralPath $package -Filter '*.nupkg' | Select-Object -First 1
    if (-not $nupkg) { Fail "no .nupkg in $package to verify" }
    $verify = Read-Tool $Dotnet @('nuget', 'verify', '--all', $nupkg.FullName)
    $author = ($verify -join "`n") -match 'Signature type: Author\s+Subject Name: CN=Microsoft Corporation'
    if (-not $author) { $verify | Out-Host; Fail "$($nupkg.Name) is not author-signed by Microsoft" }
    Write-Host "$($nupkg.Name): signature verified (Microsoft), content hash matches packages.lock.json"
    $bin = Get-ChildItem -Directory -LiteralPath (Join-Path $package 'bin') | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $makeappx = Join-Path $bin.FullName "$hostArch\makeappx.exe"
    $makepri = Join-Path $bin.FullName "$hostArch\makepri.exe"
}
foreach ($tool in @($makeappx, $makepri)) {
    if (-not (Test-Path $tool)) { Fail "$tool is missing" }
    Assert-MicrosoftSigned $tool
}
Write-Host "makeappx: $makeappx"
Write-Host "makepri:  $makepri"

# ---- Icons ----

Step 'Drawing the icons from the app''s logo code'
$iconOut = Join-Path $Out 'icons'
Invoke-Tool $Dotnet @('run', '--project', $icons, '-c', 'Release', '--no-restore', '--', $iconOut)
$assets = Join-Path $iconOut 'Assets'

# ---- Manifest text, shared by both packages ----

$escape = { param($text) [Security.SecurityElement]::Escape($text) }
$manifestText = Get-Content -Raw -Encoding UTF8 $template
# The template's first comment explains the placeholders; the package doesn't need it.
$manifestText = ([regex]'(?s)<!--.*?-->\s*').Replace($manifestText, '', 1)
$manifestText = $manifestText.Replace('__STORE_NAME__', (& $escape $Name))
$manifestText = $manifestText.Replace('__STORE_PUBLISHER__', (& $escape $Publisher))
$manifestText = $manifestText.Replace('__STORE_DISPLAY__', (& $escape $DisplayName))
$manifestText = $manifestText.Replace('__APP_NAME__', (& $escape $AppName))
$manifestText = $manifestText.Replace('__VERSION__', $Version)

# ---- One package per architecture ----

$packages = Join-Path $Out 'packages'
New-Item -ItemType Directory -Force -Path $packages | Out-Null
$priConfig = Join-Path $Out 'priconfig.xml'
Invoke-Tool $makepri @('createconfig', '/cf', $priConfig, '/dq', 'lang-en-US_lang-ar', '/pv', '10.0.0', '/o')
# One resources.pri per package. The default config would split the scales into extra .pri files,
# which only resource packages use.
$config = [xml](Get-Content -Raw -Encoding UTF8 $priConfig)
$split = $config.SelectSingleNode('/resources/packaging')
if ($split) { [void]$split.ParentNode.RemoveChild($split) }
$config.Save($priConfig)

foreach ($rid in $architectures.Keys) {
    $arch = $architectures[$rid]

    Step "Publishing $rid"
    $published = Join-Path $Out "publish\$rid"
    Invoke-Tool $Dotnet @('publish', $app, '-c', 'Release', '-r', $rid, '-p:ContinuousIntegrationBuild=true', '-o', $published)
    if (-not (Test-Path (Join-Path $published 'SteppedOut.exe'))) { Fail "the $rid publish has no SteppedOut.exe" }

    Step "Laying out and packing $arch"
    $layout = Join-Path $Out "layout\$arch"
    New-Item -ItemType Directory -Force -Path $layout | Out-Null
    Get-ChildItem -LiteralPath $published -File | Where-Object { $_.Extension -ne '.pdb' } |
        Copy-Item -Destination $layout
    Copy-Item -Recurse -LiteralPath $assets -Destination (Join-Path $layout 'Assets')
    $manifest = Join-Path $layout 'AppxManifest.xml'
    Write-Utf8 $manifest ($manifestText.Replace('__ARCH__', $arch))
    if ((Get-Content -Raw $manifest) -match '__[A-Z_]+__') { Fail "a placeholder was left in $manifest" }

    # makeappx doesn't check that the images the manifest names exist, so this does.
    $named = [regex]::Matches((Get-Content -Raw $manifest), '"(Assets\\[^"]+)\.png"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    foreach ($image in $named) {
        if (-not (Test-Path (Join-Path $layout "$image.scale-100.png")) -and -not (Test-Path (Join-Path $layout "$image.png"))) {
            Fail "the manifest names $image.png, but no such image was drawn"
        }
    }

    # The index is written outside the layout so it never indexes itself, then copied in.
    $pri = Join-Path $Out "pri\$arch"
    New-Item -ItemType Directory -Force -Path $pri | Out-Null
    Invoke-Tool $makepri @('new', '/pr', $layout, '/cf', $priConfig, '/mn', $manifest, '/of', (Join-Path $pri 'resources.pri'), '/o')
    Copy-Item -LiteralPath (Join-Path $pri 'resources.pri') -Destination $layout

    # makeappx checks the manifest against the schema and its rules while packing (no /nv).
    $msix = Join-Path $packages "SteppedOut_${Version}_$arch.msix"
    Invoke-Tool $makeappx @('pack', '/d', $layout, '/p', $msix, '/o')
}

# ---- Bundle and upload file ----

Step 'Bundling'
$bundle = Join-Path $Out "SteppedOut_${Version}_x64_arm64.msixbundle"
Invoke-Tool $makeappx @('bundle', '/d', $packages, '/p', $bundle, '/bv', $Version, '/o')

$upload = Join-Path $Out "SteppedOut_${Version}_x64_arm64.msixupload"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($upload, [IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $bundle, [IO.Path]::GetFileName($bundle),
        [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
} finally {
    $zip.Dispose()
}

# ---- Read everything back ----

Step 'Checking the upload reads back'
$check = Join-Path $Out 'check'
[IO.Compression.ZipFile]::ExtractToDirectory($upload, (Join-Path $check 'upload'))
$inner = Get-ChildItem -LiteralPath (Join-Path $check 'upload') -Filter '*.msixbundle'
if (@($inner).Count -ne 1) { Fail 'the .msixupload must hold exactly one .msixbundle' }
Invoke-Tool $makeappx @('unbundle', '/p', $inner[0].FullName, '/d', (Join-Path $check 'bundle'), '/o')
$found = @{}
foreach ($msix in Get-ChildItem -LiteralPath (Join-Path $check 'bundle') -Filter '*.msix') {
    $dir = Join-Path $check $msix.BaseName
    Invoke-Tool $makeappx @('unpack', '/p', $msix.FullName, '/d', $dir, '/o')
    $identity = ([xml](Get-Content -Raw -Encoding UTF8 (Join-Path $dir 'AppxManifest.xml'))).Package.Identity
    if ($identity.Name -ne $Name -or $identity.Publisher -ne $Publisher -or $identity.Version -ne $Version) {
        Fail "$($msix.Name) has the wrong identity"
    }
    if (-not (Test-Path (Join-Path $dir 'SteppedOut.exe')) -or -not (Test-Path (Join-Path $dir 'resources.pri'))) {
        Fail "$($msix.Name) is missing SteppedOut.exe or resources.pri"
    }
    $found[$identity.ProcessorArchitecture] = $msix.Name
}
foreach ($arch in $architectures.Values) {
    if (-not $found.ContainsKey($arch)) { Fail "the bundle has no $arch package" }
}
Remove-Item -Recurse -Force -LiteralPath $check

$wack = if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe' } else { $null }

Step 'Done'
foreach ($file in @($upload, $bundle) + @(Get-ChildItem -LiteralPath $packages -Filter '*.msix' | ForEach-Object { $_.FullName })) {
    $item = Get-Item -LiteralPath $file
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
    Write-Host ("{0}  {1,8:N1} MB  SHA256 {2}" -f $item.Name, ($item.Length / 1MB), $hash)
}
Write-Host ''
Write-Host "Upload this file in Partner Center: $upload"
if ($wack -and (Test-Path $wack)) {
    Write-Host "The App Certification Kit is installed: $wack"
} else {
    Write-Host 'The App Certification Kit is not installed; the Store runs the same checks after upload.'
}
