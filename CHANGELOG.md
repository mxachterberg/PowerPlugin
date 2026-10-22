# Änderungen

Was sich von Version zu Version ändert. Beim Veröffentlichen wird der
Abschnitt einer Version zum Text des GitHub-Releases
(`.github/workflows/release.yml`) und zu den Neuerungen, die die App vor und
nach einem Update zeigt (`scripts/publish-update.js`, höchstens 12
Stichpunkte).

## 1.0.0 – 22.10.2026

Die erste Fassung mit Versionsnummer – und mit Updates aus der App heraus.

- **Leistung im Infobereich:** Die Gesamtleistung von CPU, Grafikkarte,
  Arbeitsspeicher, Laufwerken und allen weiteren Verbrauchern über 1 W steht
  als Zahl in der Taskleiste. Messintervall, Momentanwert oder Mittelwert und
  die Aktualisierung des Symbols sind einstellbar.
- **Statistik:** Tagesdurchschnitt, Peak, Monatsdurchschnitt und
  Jahresprognose, dazu der Verlauf der letzten 30 Tage und die Energie je
  Komponente.
- **Echte CPU-Werte:** Mit PawnIO und Administratorrechten misst PowerPlugin
  die CPU über ihren Package-Sensor. Was fehlt, zeigen das Badge („Grobe
  Schätzung · PawnIO fehlt“) und die Checkliste unter Sensorzugriff.
- **Immer mit Administratorrechten starten:** Windows fragt bei jedem Start
  nach den Rechten, der Autostart läuft über eine geplante Aufgabe ohne
  Rückfrage.
- **Installation nach C:\Program Files** mit `install.ps1`. Autostart und
  Administratorrechte ziehen mit um, wenn die alte Kopie verschwunden ist.
- **Updates:** PowerPlugin sucht beim Start und einmal am Tag nach einer
  neuen Fassung, zeigt die Neuerungen und installiert sie auf Knopfdruck. Die
  Update-Liste ist mit Ed25519 signiert, das Paket wird per SHA-256 geprüft.
  Unter Einstellungen → Updates lässt sich das abschalten oder von Hand
  anstoßen.
