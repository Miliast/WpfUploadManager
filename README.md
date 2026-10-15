# Upload Manager (WPF)

Desktop-App für Windows: Links wählst du Dateien in einem **Datei-Explorer (TreeView)** aus, rechts weist du sie **vier Upload-Feldern** zu. Jedes Feld prüft Dateityp und Größe. Hochgeladen wird erst, wenn alle Pflichtfelder gültig belegt sind.

Technik: **.NET 10 · WPF · MVVM (CommunityToolkit.Mvvm) · Fluent-Design (WPF-UI) · Generic Host (DI, Konfiguration, Logging) · xUnit**

## Schnellstart

Voraussetzungen: Windows 10/11 und das [.NET 10 SDK](https://dotnet.microsoft.com/download). Visual Studio 2022 (17.14+) oder 2026 bzw. Rider sind optional.

```powershell
git clone <repo-url>
cd WpfUploadManager
dotnet run --project src/UploadManager.App
```

In Visual Studio öffnest du `WpfUploadManager.slnx` und startest `UploadManager.App`.

Standardmäßig läuft die App im **Simulationsmodus**: Uploads werden nur simuliert und es werden keine Daten gesendet. Das zeigt auch die Statusleiste an.

## Funktionen

| Bereich | Was die App bietet |
|---|---|
| Explorer (links) | Ordner öffnen (Strg+O), Unterordner werden erst beim Aufklappen geladen, Aktualisieren (F5) behält aufgeklappte Ordner bei, Filter „Nur passende Dateitypen“ |
| Zuweisen | Drag & Drop aus dem Explorer oder aus Windows, Doppelklick/Enter (wählt automatisch das passende Feld), „Durchsuchen …“, „Markierte übernehmen“ |
| Upload-Felder (rechts) | Status je Feld (leer, bereit, ungültig, wird hochgeladen, hochgeladen), Fehlertext direkt am Feld, Pflicht- und optionale Felder, Fortschritt pro Datei |
| Upload | Startet erst, wenn alle Pflichtfelder gültig sind (Strg+Enter). Abbrechen mit Esc. Vor dem Senden werden alle Dateien noch einmal geprüft. |
| Komfort | Hell/Dunkel-Umschalter, Mica-Hintergrund, 1- oder 2-spaltiges Layout je nach Breite, Snackbar-Meldungen. Letzter Ordner, Design und Filter bleiben gespeichert. |

## Konfiguration

Alles steht in `src/UploadManager.App/appsettings.json`. Fehlerhafte Werte werden beim Start erkannt und als Meldung angezeigt.

```jsonc
{
  "Upload": {
    "UseSimulation": true,                                // false = echter HTTP-Upload
    "Endpoint": "https://localhost:5001/api/uploads",     // Pflicht, wenn UseSimulation = false
    "Timeout": "00:10:00",
    "Slots": [
      {
        "Key": "document",                // Feldname im multipart-Request (eindeutig)
        "Title": "Hauptdokument",
        "Description": "Das zentrale Dokument …",
        "AllowedExtensions": [ ".pdf", ".docx" ],  // leer = alle Dateitypen
        "MaxSizeMb": 25,
        "IsRequired": true
      }
      // … weitere Felder
    ]
  }
}
```

Die vier mitgelieferten Felder (Hauptdokument, Datentabelle, Konfiguration, Anhang) sind **Platzhalter**. Passe Titel, Beschreibung und Dateitypen an deine Dateien an. Mehr oder weniger als vier Felder funktionieren ebenfalls.

Benutzereinstellungen liegen unter `%LOCALAPPDATA%\WpfUploadManager\settings.json`.

### Backend-Vertrag (HTTP-Modus)

Mit `"UseSimulation": false` schickt `HttpUploadService` **einen** `POST`-Request vom Typ `multipart/form-data` an `Endpoint`. Jede Datei ist ein eigener Teil, dessen Name dem Slot-`Key` entspricht:

```
POST /api/uploads
Content-Type: multipart/form-data; boundary=...

--...
Content-Disposition: form-data; name="document"; filename="antrag.pdf"
--...
Content-Disposition: form-data; name="data"; filename="werte.xlsx"
...
```

Jeder `2xx`-Status gilt als Erfolg. Bei allen anderen Statuscodes zeigt die App eine Fehlermeldung, und die Dateien bleiben für einen neuen Versuch bereit.

## Architektur

```
WpfUploadManager.slnx
├── src/
│   ├── UploadManager.Core/          # UI-unabhängig, net10.0, vollständig testbar
│   │   ├── Configuration/           # UploadOptions, Slot-Definitionen, Options-Validierung
│   │   ├── IO/                      # Dateisystem-Enumeration, Progress-Stream
│   │   ├── Services/                # Interfaces + Upload (simuliert / HTTP), Einstellungen
│   │   ├── Validation/              # FileValidator (Typ, Größe, Existenz)
│   │   └── ViewModels/              # MainViewModel, FileExplorerViewModel, UploadSlotViewModel …
│   └── UploadManager.App/           # WPF, net10.0-windows
│       ├── App.xaml(.cs)            # Composition Root: Generic Host, DI, globale Fehlerbehandlung
│       ├── Views/                   # MainWindow, FileExplorerView, UploadSlotView
│       ├── Behaviors/               # FileDrop (Drag & Drop als Attached Behavior)
│       ├── Converters/              # Sichtbarkeit, responsives Spaltenlayout
│       ├── Services/                # WPF-Implementierungen (Dialoge, Snackbar, Theme)
│       └── Resources/Styles.xaml    # Gemeinsame Styles (nutzen WPF-UI-Theme-Ressourcen)
└── tests/
    └── UploadManager.Core.Tests/    # xUnit v3 für Validierung, ViewModels und Upload-Ablauf
```

Wichtige Entscheidungen:

- **ViewModels liegen im Core-Projekt und hängen nicht von WPF ab.** Deshalb laufen die Tests auf jedem Betriebssystem, auch in CI ohne Windows-Desktop.
- **MVVM mit Source Generators** (`[ObservableProperty]` auf partiellen Properties, `[RelayCommand]`). In den Views steht nur reine UI-Logik, z. B. das Starten eines Drag-Vorgangs im Baum.
- **Abstraktionen für Dialoge, Benachrichtigungen, Theme und Upload.** So lassen sich die ViewModels mit Fakes testen und das Backend austauschen.
- **Ein Request für alle Dateien**, weil die Dateien fachlich zusammengehören. Wenn jede Datei einzeln gesendet werden soll, reicht eine neue `IUploadService`-Implementierung.
- **Central Package Management** (`Directory.Packages.props`), Nullable aktiviert und Warnungen als Fehler (`Directory.Build.props`).

## Entwicklung

```powershell
dotnet build                 # gesamte Solution
dotnet test                  # Unit-Tests
dotnet publish src/UploadManager.App -c Release -r win-x64 --self-contained false -o publish
```

Das Core-Projekt und die Tests lassen sich auch unter Linux und macOS bauen und testen. Die App selbst läuft nur unter Windows.

Die CI (GitHub Actions, `.github/workflows/ci.yml`) baut die App auf `windows-latest`, führt die Tests aus und stellt die fertige App als Artefakt `UploadManager-win-x64` bereit. Dependabot hält NuGet-Pakete und Actions aktuell.

## Mögliche nächste Schritte

- Die Platzhalter-Felder in `appsettings.json` durch die echten Dateien ersetzen
- Backend-Endpunkt anbinden, ggf. mit Authentifizierung (z. B. Bearer-Token über einen `DelegatingHandler` am `HttpClient`)
- Datei-Logging (z. B. Serilog) und ein Installer (MSIX/Velopack)
- Lokalisierung über `.resx`, falls weitere Sprachen nötig sind
