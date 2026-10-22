<#
  PowerPlugin - Release bauen: Tests, Veroeffentlichung und das Paket fuer Updates.

    powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1

  Ergebnis in dist\: PowerPlugin-<Version>-win-x64.zip mit der veroeffentlichten
  Anwendung (die Dateien direkt im Zip, ohne Ordner darum). Dieses Paket laedt die
  App beim Update; publish-update.js signiert es anschliessend.

  Die Version steht in Directory.Build.props (<Version>).

  Mit -Previous entsteht zusaetzlich das kleine Update wie bei Clavichat:
  PowerPlugin-<Version>-delta-<Vorgaenger>.zip mit nur den Dateien, die sich
  gegenueber dem Paket des Vorgaengers geaendert haben, und delta.json mit den
  Dateien, die wegfallen. Installierte Fassungen genau dieses Vorgaengers laden
  nur das statt des ganzen Pakets.

  -SkipTests        Tests auslassen (etwa wenn sie gerade schon gelaufen sind)
  -Previous         Paket (Zip) der Vorgaengerversion
  -PreviousVersion  deren Version, z. B. 1.0.1
#>
param(
  [switch]$SkipTests,
  [string]$Previous,
  [string]$PreviousVersion
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

if ($Previous) {
  if ($PreviousVersion -notmatch '^\d+\.\d+\.\d+$') { throw "-PreviousVersion fehlt oder ist ungueltig: $PreviousVersion" }
  if (-not (Test-Path -LiteralPath $Previous)) { throw "Paket des Vorgaengers fehlt: $Previous" }

  $previousDir = Join-Path $dist "previous-$PreviousVersion"
  $deltaDir = Join-Path $dist "delta-$version"
  foreach ($dir in $previousDir, $deltaDir) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
  }
  [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $Previous).Path, $previousDir)
  New-Item -ItemType Directory -Path $deltaDir | Out-Null

  # Geaendert oder neu: nach SHA-256 verglichen, nicht nach Datum.
  $changed = 0
  foreach ($file in Get-ChildItem -LiteralPath $out -Recurse -File) {
    $relative = $file.FullName.Substring($out.Length + 1)
    $old = Join-Path $previousDir $relative
    if ((Test-Path -LiteralPath $old) -and
        (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
      continue
    }
    $target = Join-Path $deltaDir $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $changed++
  }

  $removed = @(Get-ChildItem -LiteralPath $previousDir -Recurse -File |
    ForEach-Object { $_.FullName.Substring($previousDir.Length + 1) } |
    Where-Object { -not (Test-Path -LiteralPath (Join-Path $out $_)) } |
    ForEach-Object { $_ -replace '\\', '/' })

  $description = [ordered]@{ from = $PreviousVersion; to = $version; removed = $removed }
  $json = ConvertTo-Json -InputObject $description -Depth 3
  [IO.File]::WriteAllText((Join-Path $deltaDir 'delta.json'), $json, (New-Object System.Text.UTF8Encoding($false)))

  $deltaZip = Join-Path $dist "PowerPlugin-$version-delta-$PreviousVersion.zip"
  if (Test-Path -LiteralPath $deltaZip) { Remove-Item -LiteralPath $deltaZip -Force }
  [IO.Compression.ZipFile]::CreateFromDirectory($deltaDir, $deltaZip, [IO.Compression.CompressionLevel]::Optimal, $false)

  $deltaSize = (Get-Item -LiteralPath $deltaZip).Length / 1MB
  Write-Output ("Kleines Update ab {0}: {1} Dateien geaendert, {2} entfernt ({3:0.00} MB)" -f $PreviousVersion, $changed, $removed.Count, $deltaSize)
}
