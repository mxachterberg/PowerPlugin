<#
  PowerPlugin - Release bauen: Tests, Veroeffentlichung und das Paket fuer Updates.

    powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1

  Ergebnis in dist\: PowerPlugin-<Version>-win-x64.zip mit der veroeffentlichten
  Anwendung (die Dateien direkt im Zip, ohne Ordner darum). Dieses Paket laedt die
  App beim Update; publish-update.js signiert es anschliessend.

  Die Version steht in Directory.Build.props (<Version>).

  -SkipTests  Tests auslassen (etwa wenn sie gerade schon gelaufen sind)
#>
param(
  [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$propsText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Directory.Build.props')
if ($propsText -notmatch '<Version>(\d+\.\d+\.\d+)</Version>') { throw 'Keine Version (x.y.z) in Directory.Build.props.' }
$version = $Matches[1]

$dist = Join-Path $root 'dist'
$out = Join-Path $dist "PowerPlugin-$version"
$zip = Join-Path $dist "PowerPlugin-$version-win-x64.zip"

if (-not $SkipTests) {
  dotnet test (Join-Path $root 'PowerPlugin.sln') -c Release --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Die Tests sind fehlgeschlagen.' }
}

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\PowerPlugin.App') -c Release -r win-x64 --self-contained false -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen (Code $LASTEXITCODE)." }
if (-not (Test-Path -LiteralPath (Join-Path $out 'PowerPlugin.exe'))) { throw 'PowerPlugin.exe fehlt in der Ausgabe.' }

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

$size = (Get-Item -LiteralPath $zip).Length / 1MB
Write-Output ("PowerPlugin {0}: {1} ({2:0.0} MB)" -f $version, $zip, $size)
