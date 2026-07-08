# KainoTranslator

[English](README.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | **Deutsch** | [Polski](README.pl.md)

Eine schlanke Windows-Desktop-App, die einen von Ihnen gewählten rechteckigen Bildschirmbereich
per OCR erkennt und direkt darüber eine live übersetzte Einblendung anzeigt — gedacht für
Spiel-Untertitel, Dialogfenster, Video-Untertitel oder beliebigen fremdsprachigen Text auf dem
Bildschirm.

- Erfassungsbereich per Tastenkombination auswählen, Live-Übersetzung per Tastenkombination
  ein-/ausschalten, Einstellungen per Tastenkombination öffnen.
- Erkennt Text mit **Windows OCR** (empfohlener Standard, in Windows integriert, keine
  Einrichtung nötig) oder wahlweise mit den älteren Engines **Tesseract** / **EasyOCR**.
- Übersetzt mit **DeepL**, **Google Translate**, **Yandex Translate** oder **Papago**.
- Starke Unterstützung für Englisch, Japanisch, Chinesisch (vereinfacht & traditionell),
  Koreanisch und Russisch, plus ca. 25 weitere Sprachen (Französisch, Deutsch, Spanisch,
  Italienisch, Portugiesisch, Niederländisch, Polnisch, Türkisch, Vietnamesisch, Thailändisch,
  Indonesisch, Arabisch, Hindi, Ukrainisch und die meisten weiteren europäischen Sprachen —
  vollständige Liste in `Models/LanguageCatalog.cs`).
- Optionale Proxy-Liste zur Rotation von Anfragen, hauptsächlich damit der kostenlose
  Google-Endpunkt bei starker Nutzung nicht per IP gedrosselt wird.
- Klickdurchlässige, für randlose Spiele geeignete Einblendung, die weder Fokus noch
  Eingaben blockiert.

> **Hinweis:** Die Benutzeroberfläche der App selbst ist derzeit ausschließlich auf Russisch.
> Tab-/Menünamen werden unten so angegeben, wie sie tatsächlich in der App erscheinen
> (Russisch), mit einer deutschen Übersetzung in Klammern bei der ersten Erwähnung.

## Voraussetzungen

- Windows 10 (2018 Update / 1809) oder Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) zum Erstellen (Build).
- Visual Studio 2022 (17.8+) ist der einfachste Weg zum Öffnen/Bauen/Ausführen, aber die
  .NET-CLI funktioniert ebenso.

## Erstellen & Ausführen

```
cd KainoTranslator
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

Oder öffnen Sie `ScreenTranslator.sln` in Visual Studio und drücken Sie F5. Die App hat kein
Hauptfenster — sie läuft aus der **Taskleiste** (unten rechts) heraus. Rechtsklick auf das
Symbol öffnet das Menü, oder verwenden Sie die unten aufgeführten Tastenkombinationen.

Dass beim Start kein Fenster erscheint, ist beabsichtigt; falls Sie das Taskleisten-Symbol nicht
sehen, prüfen Sie den Pfeil „Ausgeblendete Symbole einblenden" in der Taskleiste.

## Standard-Tastenkombinationen

| Aktion                                        | Standardkombination |
|-------------------------------------------------|-----------------|
| Erfassungsbereich auswählen                      | `Ctrl+Shift+A`  |
| Live-Übersetzung starten/stoppen                 | `Ctrl+Shift+S`  |
| Bereich einmalig übersetzen                       | `Ctrl+Shift+D`  |
| Einstellungen öffnen                              | `Ctrl+Shift+O`  |

Alle vier lassen sich im Tab **Горячие клавиши** (Tastenkombinationen) ändern — klicken Sie in
das Feld und drücken Sie die gewünschte Kombination (muss mindestens eine Zusatztaste wie Ctrl,
Alt, Shift oder Win enthalten). Falls eine Tastenkombination nicht registriert werden kann
(Meldung als Sprechblase im Infobereich), verwendet bereits eine andere App diese Kombination —
wählen Sie eine andere.

Mit der Tastenkombination „Bereich einmalig übersetzen" können Sie einen beliebigen
Bildschirmbereich auswählen und einmalig übersetzen (z. B. Text, der an anderer Stelle auf dem
Bildschirm erschienen ist), ohne den Haupterfassungsbereich zu verändern oder eine laufende
Live-Übersetzung zu unterbrechen.

## Erste Einrichtung

1. App starten (Taskleisten-Symbol erscheint).
2. `Ctrl+Shift+A` drücken und ein Rechteck über den zu übersetzenden Untertitel-/Dialogbereich
   ziehen.
3. `Ctrl+Shift+O` drücken, um die Einstellungen zu öffnen:
   - **Перевод** (Übersetzung) — OCR-Engine (standardmäßig Windows OCR), Quellsprache,
     Übersetzer, Zielsprache sowie ein API-Schlüssel für den gewählten Übersetzer (siehe unten).
   - Speichern.
4. `Ctrl+Shift+S` drücken, um die Live-Übersetzung zu starten. Der übersetzte Text erscheint in
   einer Einblendung nahe dem Erfassungsbereich.

## OCR-Engines

- **Windows OCR (empfohlen, Standard)** — kein Download nötig, aber für die gewählte Sprache
  muss die OCR-Komponente installiert sein: **Windows-Einstellungen > Zeit und Sprache >
  Sprache und Region > Sprache hinzufügen**, dann für diese Sprache **„Optische
  Zeichenerkennung"** in den Sprachoptionen aktivieren. Schnell und meist am genauesten bei
  sauberem UI-/Untertiteltext.
- **Tesseract (Legacy)** — vollständig offline. Laden Sie die `.traineddata`-Dateien für die
  benötigten Sprachen von https://github.com/tesseract-ocr/tessdata_fast herunter und legen Sie
  sie im Ordner ab, der im Tab **Прочее** (Sonstiges) unter „Ordner mit Sprachdateien
  (.traineddata)" eingestellt ist (Standard: `.\tessdata` neben der exe).
- **EasyOCR (Legacy)** — benötigt Python 3.9+ im PATH (oder Pfad zur exe im Tab **Прочее**,
  Sonstiges, angeben), außerdem einmalig `pip install easyocr` ausführen. Die App startet
  `Python\easyocr_worker.py`, welches das EasyOCR-Modell im Speicher hält und pro Zeile der
  Standardeingabe eine Erkennungsanfrage beantwortet — so wird das (langsame) Neuladen des
  Modells bei jedem Frame vermieden. GPU-Beschleunigung ist optional und erfordert einen
  CUDA-fähigen PyTorch-Build.

## Übersetzer & API-Schlüssel

| Übersetzer | Was Sie brauchen | Hinweise |
|---|---|---|
| **DeepL** | API-Schlüssel von deepl.com/pro-api (kostenlose Stufe vorhanden) | „Pro-Endpunkt verwenden" nur aktivieren, wenn Sie einen bezahlten Pro-Schlüssel besitzen — kostenlose Schlüssel müssen beim kostenlosen Endpunkt bleiben. |
| **Google Translate** | Nichts erforderlich | Funktioniert sofort über den kostenlosen Web-Endpunkt von Google. Für garantierte Verfügbarkeit/Kontingent statt des kostenlosen Endpunkts einen Cloud-Translation-API-Schlüssel hinzufügen. |
| **Yandex Translate** | API-Schlüssel + Folder ID von Yandex Cloud | Beide Felder sind erforderlich. |
| **Papago** | Client ID + Client Secret aus der NAVER-Cloud-Platform-Konsole | Papago unterstützt nur eine begrenzte Anzahl an Sprachpaaren, hauptsächlich über Koreanisch/Englisch/Japanisch/Chinesisch — es werden nur unterstützte Zielsprachen zur Auswahl angezeigt. |

## Proxys

Der Tab **Прочее** (Sonstiges) enthält ganz unten eine Proxy-Liste, ein Proxy pro Zeile:
```
http://1.2.3.4:8080
http://benutzer:passwort@1.2.3.4:8080
1.2.3.4:8080
```
Leer lassen für eine direkte Verbindung (Standard). Am nützlichsten beim kostenlosen
Google-Endpunkt, der keine offizielle Ratenbegrenzungsgarantie bietet und bei anhaltend
schnellem Abfragen Anfragen ablehnen kann — die Rotation über mehrere Proxys (standardmäßig
aktiv, sobald die Liste nicht leer ist) verteilt die Last auf unterschiedliche IPs.

## Verwendung mit Spielen

- **Spiel im Fenstermodus ohne Rahmen oder im Fenstermodus starten.** Die meisten Spiele lassen
  sich mit `Alt+Enter` oder über eine Anzeigemodus-Einstellung umschalten. Dies ist der
  wichtigste Tipp.
- **Exklusiver Vollbildmodus** kann sowohl bei der Bildschirmerfassung als auch bei der
  Einblendung zu Problemen führen, da manche Spiele im exklusiven Vollbildmodus den normalen
  Windows-Compositor (DWM) umgehen — das erfasste Bild kann schwarz erscheinen, oder die
  Einblendung wird nicht darüber gezeichnet. Fenstermodus/randloser Modus wird wie jedes normale
  Fenster über DWM gerendert, sodass Erfassung und Einblendung korrekt funktionieren. Neuere
  Windows-/DirectX-Versionen vermeiden zunehmend echten exklusiven Vollbildmodus, aber falls ein
  bestimmtes Spiel nur „Vollbild" anbietet, versuchen Sie Fenstermodus/randlos, wenn die
  Einblendung falsch aussieht.
- Die Einblendung ist **standardmäßig klickdurchlässig** (umschaltbar im Tab **Оформление**,
  Erscheinungsbild), sodass sie nie Mausklicks oder Tastatureingaben blockiert, die für das
  darunterliegende Spiel gedacht sind.
- Die App **berücksichtigt die DPI jedes Monitors einzeln**, sodass Erfassungsbereich und
  Position der Einblendung auch bei Multi-Monitor-Setups mit unterschiedlicher Skalierung
  korrekt bleiben, oder wenn Sie das Spiel nach der Bereichsauswahl auf einen anderen Monitor
  ziehen (Bereich neu auswählen, falls Sie zu einem Monitor mit anderer Auflösung/Skalierung
  wechseln).

## Erscheinungsbild der Einblendung

Im Tab **Оформление** (Erscheinungsbild) lässt sich einstellen:
- Position (Standard: oben mittig auf dem Bildschirm), Schriftgröße und maximale Breite
  (Standard 1200px).
- Text- und Hintergrundfarbe (Hex, z. B. `#FFFFFF`) mit Live-Vorschau, sowie
  Hintergrundtransparenz (Standard 20 %; 0 % = Hintergrund vollständig deckend, 100 % =
  vollständig durchsichtig).
- Entweder eine 1px-Kontur **oder** ein Schlagschatten hinter dem Text zur besseren Lesbarkeit
  auf beliebigem Hintergrund (nicht beides gleichzeitig) — Standard ist deaktiviert.
- Optionales automatisches Ausblenden: die Einblendung nach N Sekunden automatisch
  ausblenden, wenn sich der Text nicht mehr ändert.

## Hinweise zur Leistung

- Das Abfrageintervall (Standard 0,3 s, Tab **Перевод**, Übersetzung, in Sekunden angezeigt)
  bestimmt, wie oft der Erfassungsbereich erneut geprüft wird.
- „OCR/Übersetzung überspringen, wenn sich der Erfassungsbereich sichtbar nicht verändert hat"
  (standardmäßig aktiv) hasht eine verkleinerte Version jedes erfassten Frames und überspringt
  OCR sowie den Übersetzer-API-Aufruf vollständig, wenn sich nichts geändert hat — dadurch
  bleibt ein schnelles Abfrageintervall sowohl bei der CPU-Last als auch beim
  Übersetzungs-API-Verbrauch günstig, da die meisten Untertitel/Dialoge eine Sekunde oder länger
  unverändert bleiben.
- Erkannter Text muss (unter Ignorierung geringfügiger Leerzeichen-/Formatierungsunterschiede)
  bei zwei aufeinanderfolgenden Prüfungen gleich ausfallen bzw. zumindest sehr ähnlich sein,
  bevor er übersetzt wird — allerdings wird Text, der bereits nach der ersten Prüfung wieder
  verschwindet (z. B. kurze Cutscene-Einblendungen), trotzdem übersetzt, statt verworfen zu
  werden. Das verhindert, dass die angezeigte Übersetzung durch eine einzelne fehlerhafte
  OCR-Erkennung flackert/wechselt. Als Nebeneffekt werden Untertitel, die schrittweise auf dem
  Bildschirm eingeblendet werden (Schreibmaschineneffekt), erst übersetzt, sobald sie sich nicht
  mehr verändern, also sobald sie vollständig angezeigt sind — keine eigene Funktion, sondern
  eine natürliche Folge derselben Stabilitätsprüfung.
- Wiederholt erkannter, identischer Text überspringt ebenfalls einen unnötigen
  Übersetzungsaufruf.
- Die spürbare Verzögerung entsteht größtenteils durch OCR und die Netzwerk-Umlaufzeit zum
  Übersetzer; Windows OCR läuft lokal und ist von den drei Engines in der Regel am schnellsten
  — die beste Wahl, wenn sowohl Geschwindigkeit als auch Einfachheit wichtig sind.

## Projektstruktur

```
KainoTranslator/
  App.xaml(.cs)              - Taskleisten-Symbol, Tastenkombinationen, übergeordnete Steuerung
  TranslationEngine.cs        - Schleife: Erfassung -> OCR -> Übersetzung -> Einblendung
  Capture/                    - Bereichsauswahl-UI + Bildschirmerfassung + Frame-Hashing
  Ocr/                        - IOcrEngine + Implementierungen für Windows OCR / Tesseract / EasyOCR
  Translation/                - ITranslator + Implementierungen für DeepL / Google / Yandex / Papago, Proxy-Verwaltung
  Overlay/                    - stets im Vordergrund angezeigtes Einblendungsfenster
  Hotkeys/                    - Registrierung globaler Tastenkombinationen (RegisterHotKey)
  SettingsUi/                 - das Einstellungsfenster
  Models/                     - AppSettings (gespeichert unter %AppData%\KainoTranslator\settings.json), LanguageCatalog
  Python/easyocr_worker.py    - EasyOCR-Worker-Skript (nur bei Verwendung der EasyOCR-Engine nötig)
```

## Bekannte Einschränkungen / Fehlerbehebung

- Windows OCR erfordert, dass die OCR-Funktion der jeweiligen Sprache über die
  Windows-Einstellungen installiert ist — die App weist genau darauf hin, falls sie fehlt,
  statt stillschweigend zu scheitern.
- Der kostenlose Google-Endpunkt sowie die begrenzte Sprachpaar-Unterstützung von Papago sind
  oben beschrieben; die App zeigt klare Fehlermeldungen (per Sprechblase im Infobereich) an,
  statt bei Ablehnung einer Anfrage durch den Übersetzer stillschweigend zu scheitern.
- Falls eine Tastenkombination nicht auslöst, hat wahrscheinlich bereits eine andere Anwendung
  genau diese Kombination systemweit registriert — wählen Sie eine andere in den Einstellungen.
