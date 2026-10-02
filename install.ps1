<#
.SYNOPSIS
    Baut PowerPlugin und installiert es nach C:\Program Files\PowerPlugin.

.DESCRIPTION
    Program Files ist nur mit Administratorrechten beschreibbar. Das zählt, weil PowerPlugin auf
    Wunsch beim Anmelden ohne Rückfrage mit Administratorrechten startet: Läge die exe in einem
    Ordner, den jedes Programm des Benutzerkontos verändern darf, könnte ein solches Programm sie
    austauschen und bekäme beim nächsten Anmelden Administratorrechte geschenkt.

    Das Skript ist zugleich der Weg für Updates: nach "git pull" einfach erneut ausführen.
    Einstellungen und Verlauf liegen unter %LOCALAPPDATA%\PowerPlugin und bleiben erhalten.
    Autostart und "Immer mit Administratorrechten starten" zieht PowerPlugin beim ersten Start
    aus dem neuen Ordner selbst um.

.PARAMETER Destination
    Zielordner. Standard: C:\Program Files\PowerPlugin

.PARAMETER NoStart
    PowerPlugin nach der Installation nicht starten.

.EXAMPLE
    In einem Terminal mit Administratorrechten, im Ordner des Repositorys:

        powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $env:ProgramFiles 'PowerPlugin'),
    [switch] $NoStart
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal] $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bitte in einem Terminal mit Administratorrechten ausführen (Rechtsklick auf Terminal > Als Administrator ausführen).'
}

$exeName = 'PowerPlugin.exe'
$destinationExe = Join-Path $Destination $exeName

# Der Zielordner wird vor dem Kopieren geleert, damit keine Dateien einer älteren Version
# zurückbleiben. Das darf nie einen Ordner treffen, der nicht PowerPlugin gehört.
if ((Test-Path $Destination) -and
    (Get-ChildItem -LiteralPath $Destination -Force | Select-Object -First 1) -and
    -not (Test-Path -LiteralPath $destinationExe)) {
    throw "$Destination ist nicht leer und enthält kein PowerPlugin. Abbruch, damit nichts Fremdes gelöscht wird."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) ('PowerPlugin-' + [Guid]::NewGuid().ToString('N'))

try {
    Write-Host 'PowerPlugin wird gebaut ...'
    dotnet publish (Join-Path $PSScriptRoot 'src\PowerPlugin.App') -c Release -o $staging --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish ist fehlgeschlagen (Code $LASTEXITCODE)."
    }

    # Eine laufende Instanz hält ihre Dateien offen. Messwerte landen alle paar Sekunden in der
    # Datenbank; verloren gehen höchstens die letzten Sekunden.
    $running = @(Get-Process -Name 'PowerPlugin' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Host 'Laufendes PowerPlugin wird beendet ...'
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $Destination) {
        Get-ChildItem -LiteralPath $Destination -Force | Remove-Item -Recurse -Force
    }
    else {
        New-Item -ItemType Directory -Path $Destination | Out-Null
    }

    Write-Host "Installation nach $Destination ..."
    Copy-Item -Path (Join-Path $staging '*') -Destination $Destination -Recurse -Force
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Die frühere Kopie aus "dotnet publish -o publish". Bliebe sie liegen, zeigte ein versehentlicher
# Start wieder auf einen Ordner, den jedes Programm verändern darf.
$oldCopy = Join-Path $PSScriptRoot 'publish'
if (Test-Path -LiteralPath (Join-Path $oldCopy $exeName)) {
    Remove-Item -LiteralPath $oldCopy -Recurse -Force
    Write-Host "Alte Kopie entfernt: $oldCopy"
}

# Eintrag im Startmenü für alle Benutzer. Ein Taskleisten-Pin auf die alte Kopie muss von Hand
# neu angeheftet werden - das lässt Windows nicht per Skript zu.
$shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'PowerPlugin.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $destinationExe
$shortcut.WorkingDirectory = $Destination
$shortcut.Description = 'Leistungsaufnahme und Energiestatistik'
$shortcut.Save()
Write-Host "Startmenü-Eintrag: $shortcutPath"

if (-not $NoStart) {
    # Aus diesem Terminal heraus startet PowerPlugin mit Administratorrechten und kann so die
    # geplante Aufgabe ohne weitere Rückfrage auf den neuen Ordner umstellen.
    Write-Host 'PowerPlugin wird gestartet ...'
    Start-Process -FilePath $destinationExe -ArgumentList '--show' -WorkingDirectory $Destination
}

Write-Host 'Fertig.'
