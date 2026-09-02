# PowerPlugin

Ein Strommessgerät für Windows 11, das ohne zusätzliche Hardware auskommt: PowerPlugin erfasst
laufend die Leistungsaufnahme aller relevanten Komponenten, zeigt die Summe als Zahl direkt im
Infobereich der Taskleiste an und führt daraus eine Verbrauchsstatistik.

```
┌──────────────────────────────────────────────────────────────────────────┐
│  128 W   Gesamtaufnahme an der Steckdose                    PowerPlugin  │
│          [3 von 9 per Sensor] [Mittlere Genauigkeit]                     │
├──────────────────────────────────────────────────────────────────────────┤
│ TAGESDURCHSCHNITT │ PEAK      │ MONATSDURCHSCHNITT │ JAHRESPROGNOSE      │
│ 112 W             │ 341 W     │ 118 W              │ 412 kWh             │
│ 0,84 kWh · 0,29 € │ Allzeit…  │ 24,6 kWh · 8,61 €  │ 144,20 € pro Jahr   │
└──────────────────────────────────────────────────────────────────────────┘
```

## Was das Programm misst

| Komponente | Quelle |
| --- | --- |
| Prozessor | Package-Leistungssensor (Intel RAPL / AMD SMU), sonst Modell aus Auslastung und TDP |
| Grafikkarte | Board-Power-Sensor über NVML bzw. den AMD-Treiber, sonst Modell aus Auslastung und TDP |
| Arbeitsspeicher | Modell aus Anzahl, Größe und Typ der Module (DDR3/DDR4/DDR5/LPDDR) |
| Datenträger | Je Laufwerk aus Bus (NVMe/SATA/USB), Medium (SSD/HDD) und aktueller Aktivität |
| Mainboard und Chipsatz | Grundlastmodell für Chipsatz, Spannungswandler, USB, Audio und Netzwerk |
| Lüfter | Aus den Drehzahlsensoren des Super-I/O-Chips |
| Interner Bildschirm | Nur bei Notebooks, abhängig von der eingestellten Helligkeit |
| Netzteil | Wandlungsverluste aus dem eingestellten Wirkungsgrad |

Verbraucher unter **1 Watt** werden nicht einzeln aufgeführt, sondern zu einem Sammelposten
addiert – der Gesamtwert bleibt dadurch vollständig, die Liste aber übersichtlich. Die Schwelle
lässt sich in den Einstellungen ändern.

Externe Monitore tauchen bewusst **nicht** auf: Sie hängen an einer eigenen Steckdose und werden
nicht vom PC versorgt.

### Wie genau ist das?

PowerPlugin unterscheidet drei Fälle und zeigt sie im Fenster als Kennzeichnung an:

* **Sensor** – ein echter Messwert der Hardware.
* **Geschätzt** – aus der Auslastung über das Modell berechnet.
* **Modell** – ein Erfahrungswert, etwa die Grundlast des Mainboards.

Läuft ein Notebook im Akkubetrieb, liefert die ACPI-Batterie die tatsächliche Gesamtleistung des
Systems. In diesem Fall werden die geschätzten Anteile so skaliert, dass die Aufschlüsselung
exakt zu dieser Messung passt; Sensorwerte bleiben unangetastet. Das ist der genaueste Modus.

Auf einem Desktop ohne Administratorrechte fehlt der CPU-Leistungssensor. Das Programm weist
darauf hin und bietet einen Neustart mit erhöhten Rechten an.

## Voraussetzungen

* Windows 10 (1809) oder Windows 11, x64
* [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) – oder ein
  self-contained Build, siehe unten
* Für die CPU-Leistungssensoren: Start als Administrator (optional, das Programm läuft auch ohne)

## Bauen und starten

```powershell
git clone https://github.com/bachusmaximus/powerplugin.git
cd powerplugin

dotnet build -c Release
dotnet test

# Startbereite Anwendung erzeugen (.NET-Runtime muss installiert sein)
dotnet publish src/PowerPlugin.App -c Release -r win-x64 --self-contained false -o publish

# Alternativ: alles in einer Datei, ohne installierte .NET-Runtime
dotnet publish src/PowerPlugin.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o publish
```

Danach `publish\PowerPlugin.exe` starten. Beim ersten Start öffnet sich das Statistikfenster,
anschließend läuft das Programm still im Infobereich weiter.

> Die Projekte lassen sich auch auf Linux- oder macOS-Buildagenten kompilieren und testen –
> `EnableWindowsTargeting` ist gesetzt und die Kernlogik ist plattformunabhängig. Ausführen
> lässt sich die Anwendung selbst nur unter Windows.

## Bedienung

* **Linksklick auf das Symbol** oder Doppelklick öffnet die Statistik.
* **Rechtsklick** öffnet ein Menü mit dem aktuellen Wert, dem Autostart-Schalter und „Beenden“.
* Das **Fenster schließen** beendet das Programm nicht, sondern legt es zurück in die Taskleiste.
  Dieses Verhalten lässt sich in den Einstellungen umstellen.

Die Farbe des Symbols folgt der Last: grün bis 80 W, gelb bis 200 W, darüber rot. Beide Grenzen
sind einstellbar, ebenso ob das Symbol Watt, den heutigen Verbrauch in kWh oder die heutigen
Kosten anzeigt.

## Die Statistik

| Kennzahl | Bedeutung |
| --- | --- |
| **Tagesdurchschnitt** | Mittlere Leistung des heutigen Tages, gemittelt über die tatsächlich gemessene Zeit |
| **Peak** | Höchster Momentanwert – heute und über die gesamte Aufzeichnung |
| **Monatsdurchschnitt** | Mittlere Leistung und Verbrauch des laufenden Kalendermonats |
| **Jahresprognose** | Hochrechnung auf 365,25 Tage, inklusive Stromkosten |

Zwei Arten von Durchschnitt kommen dabei vor, und der Unterschied ist wichtig:

* **Ø Leistung** ist der Mittelwert *während der PC lief*. Die Frage „wie viel zieht die Kiste,
  wenn ich sie benutze?“
* **Ø kWh pro Tag** verteilt die gemessene Energie auf *alle* Kalendertage seit der ersten
  Aufzeichnung – Tage, an denen der Rechner ausgeschaltet war, zählen als null. Nur so ergibt
  die Jahresprognose einen realistischen Wert.

Solange noch kein voller Tag aufgezeichnet ist, wird der bisherige Tag auf 24 Stunden
hochgerechnet. Das Fenster weist unter der Jahresprognose aus, auf wie vielen Daten sie beruht.

## Kalibrieren

Wer ein Steckdosen-Messgerät hat, kann das Modell in wenigen Minuten deutlich genauer machen:

1. PC in den Leerlauf bringen und beide Werte vergleichen.
2. Die Differenz über **Grundlast Mainboard** ausgleichen (Einstellungen → Schätzmodell).
3. Unter Volllast erneut vergleichen und gegebenenfalls **CPU-TDP** und
   **Netzteil-Wirkungsgrad** nachziehen.

Komponenten mit echtem Sensor bleiben davon unberührt – korrigiert wird nur der geschätzte Teil.

## Daten

Alles liegt in `%LOCALAPPDATA%\PowerPlugin`:

| Datei | Inhalt |
| --- | --- |
| `history.db` | SQLite-Datenbank mit Minutenwerten und stündlicher Aufschlüsselung je Komponente |
| `settings.json` | Einstellungen inklusive aller Modellkoeffizienten |
| `powerplugin.log` | Protokoll für die Fehlersuche |

Eine Minute belegt eine Zeile; ein Jahr Dauerbetrieb sind rund 500.000 Zeilen und wenige
Dutzend Megabyte. Standardmäßig werden Werte nach 400 Tagen gelöscht.

**Portabler Betrieb:** Liegt eine Datei `portable.txt` neben der `PowerPlugin.exe`, wandert der
Datenordner in das Unterverzeichnis `Data` neben der Anwendung.

## Aufbau des Projekts

```
src/
  PowerPlugin.Core/      net8.0          Modell, Schätzlogik, Speicherung, Statistik
    Estimation/          ComponentPowerEstimator und alle Koeffizienten
    Storage/             SQLite-Ablage, Energieintegration in Minutenpakete
    Statistics/          Tages-, Monats- und Jahresberechnung
    Monitoring/          Messschleife
  PowerPlugin.Windows/   net8.0-windows  Sensorzugriff: LibreHardwareMonitor, WMI, Registry
  PowerPlugin.App/       net8.0-windows  WPF-Oberfläche und Taskleistensymbol
tests/
  PowerPlugin.Tests/     net8.0          Tests für Schätzmodell, Integration und Statistik
```

Die gesamte Rechenlogik liegt in `PowerPlugin.Core` und hängt an keiner Windows-API. Der
Sensorzugriff ist hinter `IHardwareTelemetryProvider` gekapselt, damit das Modell mit
synthetischer Hardware getestet werden kann.

Die Oberfläche ist bewusst in C# statt in XAML geschrieben. Dadurch bleibt die gesamte
Codebasis auf jedem Buildagenten übersetzbar und die Stildefinitionen liegen an einer Stelle
(`Ui/Theme.cs`).

## Technische Hinweise

* **Energieintegration:** Jeder Messwert gilt bis zum nächsten (Zero-Order-Hold). Lücken, die
  deutlich länger sind als das Messintervall – Standby, Ruhezustand, beendetes Programm – werden
  auf ein Intervall begrenzt, damit eine Nacht im Standby nicht als Verbrauch erscheint.
* **Doppelzählung:** Eine integrierte Grafikeinheit teilt sich das Leistungsbudget mit der CPU.
  Liegt ein Package-Sensor vor, wird die iGPU deshalb nicht zusätzlich ausgewiesen. Ebenso
  werden die Einzelsensoren der CPU-Kerne ignoriert, weil sie im Package-Wert enthalten sind.
* **Taskleistensymbol:** Die Zahl wird bei jeder Änderung neu gezeichnet. Das GDI-Handle des
  erzeugten Symbols wird sofort wieder freigegeben, damit der Prozess keine Handles verliert.
* **Nur eine Instanz:** Ein zweiter Start meldet sich beim laufenden Prozess, holt dessen
  Fenster nach vorn und beendet sich – zwei Instanzen würden sich um die Datenbank streiten.

## Verwendete Bibliotheken

* [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) – Sensorzugriff (MPL 2.0)
* [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) – Verlaufsdatenbank
* System.Management – WMI-Abfragen für die Hardwareerkennung
