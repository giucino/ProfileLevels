# Profile Levels (Composite) — ATAS Indikator

Eigenentwickelter ATAS-Indikator (C#) für Futures (NQ/MNQ, ES/MES). Zieht
**volumenprofil-basierte S/R-Zonen** aus dem **Composite der letzten N Sessions**.
Für Zeitcharts (M5/M15). **Rein informativ — kein Entry-Signal.**

> Teil eines mehrstufigen Projekts (Modul 2 — Auktions-Zonen). Gegenstück zum
> `LevelMarker` (Pro-Bar-Orderflow auf dem Tick-Chart).

## Was er zeichnet

Aus einem zusammengefassten Volumenprofil der letzten N Tage:

- **HVN** — High Volume Nodes (Akzeptanz / Magnetzonen)
- **LVN** — Low Volume Nodes (dünne Zonen / mögliche S/R beim Pullback)
- **Naked/Virgin POC** — vPOC einer früheren Session, der seither **nicht**
  berührt wurde; verschwindet automatisch, sobald der Preis ihn antippt

Alle als stehende, persistente horizontale Linien.

> **Hinweis:** vPOC, VAH und VAL zeichnet der Trader bei Bedarf selbst (eigenes
> Profil-Tool) — daher bewusst **nicht** in diesem Indikator. So bleibt die
> Kalibrierung auf HVN/LVN fokussiert.

## Roter Faden: Akzeptanz vs. Ablehnung

- **HVN** = akzeptiert (Magnet, Fair Value)
- **LVN** = abgelehnt (schneller Durchlauf, Pullback-Verteidigung)
- **Naked POC** = unerledigte Akzeptanzzone (magnetisches Ziel)

## Einstellungen (Kurzüberblick)

| Gruppe | Einstellung |
|---|---|
| Profil | Sessions (Composite-Tage), HVN-/LVN-Schwelle %, Glättung, Tagesgrenze (Std/Min), Max. HVN/LVN je Typ, Mindestabstand |
| Anzeige | HVN, LVN, Naked POC (je an/aus), Max. Naked POCs |
| Darstellung | Linienbreite, Labels, Schriftgröße |
| Farben | je Level-Typ |

**Kalibrierung:** „Max. HVN/LVN je Typ" begrenzt die Anzahl (stärkste zuerst),
„Mindestabstand" verschmilzt dicht beieinander liegende Knoten zu einer Zone,
„HVN-/LVN-Schwelle %" steuert, wie wählerisch ein Knoten qualifizieren muss.

## Build & Installation

- TargetFramework `net10.0-windows`, ATAS-DLLs per HintPath referenziert.
- `dotnet build -c Release`, dann `ProfileLevels.dll` nach `%APPDATA%\ATAS\Indicators\` kopieren.
- ATAS neu starten bzw. Indikatorliste aktualisieren.

## Hinweise / Stand

- **Tagesgrenze konfigurierbar** (Default 0:00 ≈ CME-Index-Futures-Rollover in DE-Lokalzeit).
- HVN/LVN-Schwellen und Mindestabstand sind Kalibriersache am echten Chart.

## Lizenz / Hinweis

Private Eigenentwicklung auf Basis allgemein verfügbarer Volumenprofil-/Auktions-
konzepte. Kein Nachbau kommerzieller Fremdprodukte. **Kein Handelssignal, keine
Anlageberatung** — Nutzung auf eigenes Risiko.
