# Profile Levels (Composite) — ATAS Indikator

Eigenentwickelter ATAS-Indikator (C#) für Futures (NQ/MNQ, ES/MES). Zieht
**volumenprofil-basierte S/R-Linien** aus dem **Composite der letzten N Sessions**.
Für Zeitcharts (M5/M15). **Rein informativ — kein Entry-Signal.**

> Teil eines mehrstufigen Projekts (Modul 2 — Auktions-Zonen). Gegenstück zum
> `LevelMarker` (Pro-Bar-Orderflow auf dem Tick-Chart).

## Was er zeichnet

Aus einem zusammengefassten Volumenprofil der letzten N Tage:

- **vPOC** — Point of Control (Preis mit dem meisten Volumen)
- **VAH/VAL** — Value-Area-Grenzen (Standard-70%-Methode)
- **HVN** — High Volume Nodes (Akzeptanz / Magnetzonen)
- **LVN** — Low Volume Nodes (dünne Zonen / mögliche S/R beim Pullback)
- **Naked/Virgin POC** — vPOC einer früheren Session, der seither **nicht**
  berührt wurde; verschwindet automatisch, sobald der Preis ihn antippt

Alle als stehende, persistente horizontale Linien.

## Roter Faden: Akzeptanz vs. Ablehnung

- **HVN / vPOC** = akzeptiert (Magnet, Fair Value)
- **LVN** = abgelehnt (schneller Durchlauf, Pullback-Verteidigung)
- **Naked POC** = unerledigte Akzeptanzzone (magnetisches Ziel)

## Einstellungen (Kurzüberblick)

| Gruppe | Einstellung |
|---|---|
| Profil | Sessions (Composite-Tage), HVN-/LVN-Schwelle %, Glättung |
| Anzeige | vPOC, VAH/VAL, HVN, LVN, Naked POC (je an/aus), Max. Naked POCs |
| Darstellung | Linienbreite, Labels, Schriftgröße |
| Farben | je Level-Typ |

## Build & Installation

- TargetFramework `net10.0-windows`, ATAS-DLLs per HintPath referenziert.
- `dotnet build -c Release`, dann `ProfileLevels.dll` nach `%APPDATA%\ATAS\Indicators\` kopieren.
- ATAS neu starten bzw. Indikatorliste aktualisieren.

## Hinweise / Stand

- **v1-Vereinfachung:** Tagesgrenze = Kalendertag (Mitternacht), noch nicht die
  echte Futures-Session-Grenze (~23:00 DE / Globex-Start). Bei Bedarf umstellbar.
- Naked POC und HVN/LVN-Schwellen sind Kalibriersache am echten Chart.

## Lizenz / Hinweis

Private Eigenentwicklung auf Basis allgemein verfügbarer Volumenprofil-/Auktions-
konzepte. Kein Nachbau kommerzieller Fremdprodukte. **Kein Handelssignal, keine
Anlageberatung** — Nutzung auf eigenes Risiko.
