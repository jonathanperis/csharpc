# Install a csharpc win-x64 release from GitHub Releases.
# Downloads the zip + SHA256SUMS, verifies, extracts to %LOCALAPPDATA%\csharpc and
# adds that directory to the user PATH.
#
#   irm https://raw.githubusercontent.com/jonathanperis/csharpc/main/install.ps1 | iex
#   .\install.ps1 [-Version v0.1.0-spike]
#
# After install you still need LLVM-MinGW (clang + lld) on PATH; libLLVM-18.dll is bundled.
param(
    [string]$Version = "",
    [string]$Repo = "jonathanperis/csharpc",
    [string]$InstallDir = ""
)

$ErrorActionPreference = "Stop"
if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "csharpc"
}

$rid = "win-x64"
$api = "https://api.github.com/repos/$Repo/releases"
if (-not $Version) {
    $releases = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "csharpc-install" }
    if (-not $releases -or $releases.Count -eq 0) { throw "no releases found at $Repo" }
    $tag = $releases[0].tag_name
} else {
    $tag = $Version
    if (-not $tag.StartsWith("v")) { $tag = "v$tag" }
}
$ver = $tag.TrimStart("v")
$asset = "csharpc-$ver-$rid.zip"
$base = "https://github.com/$Repo/releases/download/$tag"
$tmpdir = Join-Path ([System.IO.Path]::GetTempPath()) ("csharpc-install-" + [guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Path $tmpdir | Out-Null
try {
    Write-Host "install.ps1: downloading $asset ($tag)"
    $zip = Join-Path $tmpdir $asset
    $sums = Join-Path $tmpdir "SHA256SUMS"
    Invoke-WebRequest -Uri "$base/$asset" -OutFile $zip -UseBasicParsing
    Invoke-WebRequest -Uri "$base/SHA256SUMS" -OutFile $sums -UseBasicParsing

    $expected = (Get-Content $sums | Where-Object { $_ -match [regex]::Escape($asset) } | Select-Object -First 1)
    if (-not $expected) { throw "SHA256SUMS has no entry for $asset" }
    $expectedHash = ($expected -split '\s+')[0].ToLowerInvariant()
    $actualHash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
    if ($expectedHash -ne $actualHash) {
        throw "checksum mismatch for $asset (expected $expectedHash, got $actualHash)"
    }

    if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
    Expand-Archive -Path $zip -DestinationPath $tmpdir
    $bundle = Get-ChildItem -Directory $tmpdir | Where-Object { $_.Name -like "csharpc-*" } | Select-Object -First 1
    if (-not $bundle) { throw "archive had no csharpc-* directory" }
    Copy-Item -Path (Join-Path $bundle.FullName "*") -Destination $InstallDir -Recurse

    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath -notlike "*$InstallDir*") {
        [Environment]::SetEnvironmentVariable("Path", "$InstallDir;$userPath", "User")
        $env:Path = "$InstallDir;$env:Path"
    }

    Write-Host "install.ps1: installed $tag to $InstallDir"
    Write-Host "install.ps1: note: install LLVM-MinGW and put its bin\ on PATH (clang + lld). libLLVM is bundled."
    Write-Host "install.ps1: done. Open a new shell and try: csharpc hello.cs -o hello"
}
finally {
    Remove-Item -Recurse -Force $tmpdir -ErrorAction SilentlyContinue
}
