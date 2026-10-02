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

Fehlt der CPU-Leistungssensor, nennt das Programm unter *Einstellungen → Sensorzugriff* den
konkreten Grund: fehlende Administratorrechte (dann bietet es einen Neustart an), kein
installierter Hilfstreiber (siehe unten), oder eine Plattform ohne diese Register, etwa eine
virtuelle Maschine.

## Voraussetzungen

* Windows 10 (1809) oder Windows 11, x64
* [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) – oder ein
  self-contained Build, siehe unten
* Für echte CPU-Leistungswerte: [PawnIO](https://pawnio.eu) installiert **und** Start als
  Administrator. Beides ist optional – ohne läuft das Programm mit geschätzter CPU-Leistung.

### Kernel-Treiber und Virenscanner

Die Leistungsaufnahme der CPU steht in modellspezifischen Registern (Intel RAPL, AMD SMU). Kein
Windows-Programm kann die ohne Kernel-Treiber lesen – deshalb bringt jedes Tool, das echte
CPU-Watt anzeigt, einen mit.

**PowerPlugin bringt bewusst keinen mit.** Der Treiber wird nicht mitgeliefert, nicht entpackt und
nicht installiert. Ist [PawnIO](https://pawnio.eu) auf dem System vorhanden, nutzt die
Sensorbibliothek ihn; sonst schätzt das Modell die CPU aus der Auslastung. Alles andere –
Grafikkarte, Laufwerke, Arbeitsspeicher, Akku – braucht ohnehin keinen Treiber.

> **Hinweis für Nutzer früherer Builds:** Bis einschließlich Version 0.9.4 der Sensorbibliothek
> wurde beim Start als Administrator eine Datei `PowerPlugin.sys` neben der Anwendung angelegt.
> Das war **WinRing0**, ein Treiber, der jedem Aufrufer uneingeschränkten Zugriff auf
> modellspezifische Register und den physischen Speicher gibt. Genau deshalb meldet Microsoft
> Defender ihn als Bedrohung (`WinNT/Winring0`) – das ist **kein Fehlalarm**: eine installierte
> Kopie ist ein fertiges Werkzeug zur Rechteausweitung, unabhängig davon, welches Programm sie
> mitgebracht hat.
>
> Seit Version 0.9.6 nutzt die Bibliothek stattdessen PawnIO, das nur eng begrenzte, signierte
> Module ausführt statt beliebiger Register- und Speicherzugriffe. PowerPlugin löscht eine
> zurückgebliebene `PowerPlugin.sys` beim Start. Eine Ausnahme im Virenscanner ist **nicht**
> nötig und sollte nicht eingerichtet werden.

## Bauen und starten

```powershell
git clone https://github.com/mxachterberg/powerplugin.git
cd powerplugin

dotnet build -c Release
dotnet test

# Startbereite Anwendung erzeugen (.NET-Runtime muss installiert sein)
dotnet publish src/PowerPlugin.App -c Release -o publish

# Alternativ: alles in einer Datei, ohne installierte .NET-Runtime
dotnet publish src/PowerPlugin.App -c Release --self-contained true `
    -p:PublishSingleFile=true -o publish

# Für ARM-Geräte
dotnet publish src/PowerPlugin.App -c Release -r win-arm64 -o publish
```

> Die Anwendung baut standardmäßig für `win-x64`. Das ist nötig, weil die Sensorbibliothek ihre
> Implementierung nur unter `runtimes/<rid>/` ausliefert – ohne Runtime-Identifier landet sie gar
> nicht erst in der Ausgabe.

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

### Die Anzeige im Infobereich einstellen

Drei Werte bestimmen unabhängig voneinander, was das Symbol zeigt. Sie stehen unter
*Einstellungen → Anzeige im Infobereich*:

| Einstellung | Bedeutung | Standard |
| --- | --- | --- |
| **Messintervall** | Wie oft die Sensoren gelesen werden. Gilt auch für Statistik und Verbrauch. | 0,5 s |
| **Darstellung** | Momentanwert oder Durchschnitt über ein Zeitfenster | Durchschnitt |
| **Mittelungsfenster** | Länge des Zeitraums, über den gemittelt wird | 3 s |
| **Darstellungsintervall** | Wie oft die Anzeige neu berechnet wird | 0,5 s |

Weil Darstellungsintervall und Mittelungsfenster getrennt sind, lässt sich jede Kombination
einstellen:

* alle 5 Sekunden der Durchschnitt der letzten 5 Sekunden → Intervall 5, Fenster 5
* alle 3 Sekunden eine Momentaufnahme → Intervall 3, Darstellung *Momentanwert*
* jede Sekunde neu der Durchschnitt der letzten 5 Sekunden → Intervall 1, Fenster 5

Unter den Feldern steht ein Satz, der die eingestellte Kombination in Worten zurückgibt, etwa
„Alle 5 s wird der Mittelwert der letzten 5 s angezeigt. Das sind rund 10 Messwerte pro Anzeige.“
Zwei Kombinationen führen leicht in die Irre und werden dort ausdrücklich benannt: ein
Mittelungsfenster, das kaum länger ist als das Messintervall (glättet nichts), und ein
Darstellungsintervall, das kürzer ist als das Messintervall (die Anzeige wiederholt sich dann,
bis eine neue Messung vorliegt).

Ohne Glättung springt der Wert übrigens deutlich: Der Momentanwert eines PCs ändert sich zwischen
zwei Messungen um zweistellige Wattbeträge. Deshalb ist der Durchschnitt die Voreinstellung.

Die Farbe des Symbols folgt dem angezeigten Wert: grün bis 80 W, gelb bis 200 W, darüber rot.
Beide Grenzen sind einstellbar, ebenso ob das Symbol Watt, den heutigen Verbrauch in kWh oder
die heutigen Kosten anzeigt – bei kWh und Kosten wirken Messintervall und Mittelung nicht.

### Autostart

Unter *Einstellungen → Verhalten* stehen zwei Schalter:

| Schalter | Mechanismus | Rechte |
| --- | --- | --- |
| **Mit Windows starten** | Eintrag unter `HKCU\…\CurrentVersion\Run` | ohne erhöhte Rechte |
| **…dabei mit Administratorrechten** | geplante Aufgabe beim Anmelden | erhöht, ohne Rückfrage beim Start |

Der zweite Schalter ist nicht bloß Bequemlichkeit, sondern der einzige Weg, Autostart und
CPU-Sensoren zu kombinieren: **Windows startet aus dem Run-Schlüssel grundsätzlich keine Programme,
die erhöhte Rechte verlangen.** Der Explorer arbeitet diese Einträge unerhöht ab und überspringt
betroffene stillschweigend. Wer also der `PowerPlugin.exe` unter *Eigenschaften → Kompatibilität*
den Haken „als Administrator ausführen" gibt, schaltet damit unbemerkt den Autostart ab. Die
geplante Aufgabe hat dieses Problem nicht — sie startet das Programm selbst erhöht. Beim Umschalten
entfernt PowerPlugin den Kompatibilitätshaken, weil er dann überflüssig ist und nur weiter stören
würde.

Unter den Schaltern steht, was tatsächlich passiert — nicht, was eingestellt wurde. Denn ein
vorhandener Run-Eintrag heißt noch nicht, dass er auch ausgeführt wird: Windows kann ihn im
Task-Manager unter *Autostart-Apps* abgeschaltet haben, ohne den Eintrag selbst anzurühren. Beide
Fälle werden erkannt und benannt.

Das Einrichten der geplanten Aufgabe erfordert einmalig eine Bestätigung der Benutzerkontensteuerung.
Wird sie abgelehnt, bleibt der bisherige Autostart bestehen, statt dass gar keiner übrig bleibt.

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
  auf ein Intervall begrenzt, damit eine Nacht im Standby nicht als Verbrauch erscheint. Die
  Grenze liegt bei mindestens 10 Sekunden: bei einem Messintervall von 0,5 s wäre ein verzögerter
  Messwert sonst schon als Standby gewertet worden und seine Energie verlorengegangen.
* **Doppelzählung:** Eine integrierte Grafikeinheit teilt sich das Leistungsbudget mit der CPU.
  Liegt ein Package-Sensor vor, wird die iGPU deshalb nicht zusätzlich ausgewiesen. Ebenso
  werden die Einzelsensoren der CPU-Kerne ignoriert, weil sie im Package-Wert enthalten sind.
* **Taskleistensymbol:** Anzeigetakt und Messtakt sind entkoppelt – ein eigener Timer zeichnet
  das Symbol, der Wert kommt aus einer zeitgewichteten Mittelung über das Anzeigefenster. Zeitlich
  gewichtet und nicht als einfacher Mittelwert über die Messpunkte, damit drei schnell
  aufeinanderfolgende Messungen nicht schwerer wiegen als eine, die eine ganze Sekunde abdeckt.
  Der Momentanwert ist derselbe Codepfad mit einem Fenster der Länge null.
  Neu gezeichnet wird nur, wenn sich Text oder Farbe tatsächlich ändern. Das GDI-Handle des
  erzeugten Symbols wird sofort wieder freigegeben, damit der Prozess keine Handles verliert.
* **Nur eine Instanz:** Ein zweiter Start meldet sich beim laufenden Prozess, holt dessen
  Fenster nach vorn und beendet sich – zwei Instanzen würden sich um die Datenbank streiten.

## Verwendete Bibliotheken

* [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) – Sensorzugriff (MPL 2.0)
* [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) – Verlaufsdatenbank
* System.Management – WMI-Abfragen für die Hardwareerkennung
