using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Linq;
using ATAS.Indicators;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace ProfileLevels
{
    [DisplayName("Profile Levels (Composite)")]
    [HelpLink("https://giucino.github.io/ProfileLevels/ProfileLevels_Doku.html")]
    [Description("Stufe 2 / Modul 2 — zieht volumenprofil-basierte S/R-Linien aus dem Composite " +
                 "der letzten N Sessions: HVN (Akzeptanz/Magnet), LVN (duenne Zonen) und " +
                 "Naked/Virgin POC (unberuehrte Vortages-POCs als Magnet-Ziele). " +
                 "Fuer Zeitcharts (M5/M15) gedacht. Rein informativ, kein Signal.")]
    public class ProfileLevels : Indicator
    {
        // ─────────────────────────────────────────────────────────────────
        //  EINSTELLUNGEN — Profil
        // ─────────────────────────────────────────────────────────────────
        private int _sessions = 5;             // Composite ueber die letzten N Trading-Days (inkl. heute)
        private int _hvnRatioPct = 70;         // HVN-Schwelle als % des Profil-Maximums
        private int _lvnRatioPct = 20;         // LVN-Schwelle als % des Profil-Maximums
        private int _smoothing = 3;            // Glaettung in Ticks

        private int _dayStartHour = 0;         // Tagesgrenze (Rollover-Stunde) - Default Mitternacht
        private int _dayStartMinute = 0;
        private int _maxNodes = 5;             // max. HVN bzw. LVN je Typ (staerkste zuerst)
        private decimal _minSep = 0m;          // Mindestabstand zwischen Linien gleichen Typs (Punkte)

        // ─────────────────────────────────────────────────────────────────
        //  EINSTELLUNGEN — was anzeigen
        // ─────────────────────────────────────────────────────────────────
        private bool _showHvn = true;
        private bool _showLvn = true;
        private bool _showNakedPoc = true;
        private int _maxNakedPocs = 20;

        // ─────────────────────────────────────────────────────────────────
        //  EINSTELLUNGEN — Darstellung
        // ─────────────────────────────────────────────────────────────────
        private int _lineWidth = 2;
        private bool _showLabels = true;
        private int _fontSize = 11;

        private Color _colorHvn = Color.FromArgb(225, 235, 150, 45);   // HVN
        private Color _colorLvn = Color.FromArgb(225, 90, 165, 235);   // LVN
        private Color _colorNakedPoc = Color.FromArgb(235, 230, 90, 200);  // naked/virgin POC

        // ─────────────────────────────────────────────────────────────────
        //  STATE
        // ─────────────────────────────────────────────────────────────────
        private enum LevelKind { Hvn, Lvn, NakedPoc }

        private readonly struct Line
        {
            public readonly decimal Price;
            public readonly LevelKind Kind;
            public readonly int OriginBar;   // -1 = volle Breite (HVN/LVN); sonst Ray ab diesem Bar (nPOC)
            public Line(decimal price, LevelKind kind, int originBar = -1)
            { Price = price; Kind = kind; OriginBar = originBar; }
        }

        // Naked/Virgin POC: POC eines abgeschlossenen Tages, der seither nicht beruehrt wurde.
        private readonly struct NakedPoc
        {
            public readonly decimal Price;
            public readonly DateTime Date;
            public readonly int OriginBar;   // Bar, an dem der nPOC entstand (Session-Ende)
            public NakedPoc(decimal price, DateTime date, int originBar)
            { Price = price; Date = date; OriginBar = originBar; }
        }

        // Abgeschlossene Tagesprofile (aelteste zuerst) + das aktuelle Tagesprofil.
        private readonly List<Dictionary<decimal, decimal>> _history = new();
        private Dictionary<decimal, decimal> _current = new();
        private DateTime _curDate = DateTime.MinValue;
        private int _curSessionStartBar = -1;   // Bar, an dem die aktuelle Session begann

        private readonly List<NakedPoc> _nakedPocs = new();
        private readonly List<Line> _lines = new();

        private int _lastProcessedBar = -1;
        private decimal _tickEstimate = 0m;

        private RenderFont _font = null!;

        // ─────────────────────────────────────────────────────────────────
        //  PROPERTIES — Profil
        // ─────────────────────────────────────────────────────────────────
        [Display(Name = "Sessions (Composite-Tage)", GroupName = "Profil", Order = 1,
            Description = "Ueber so viele Kalendertage (inkl. heute) wird das Volumenprofil zusammengefasst. " +
                          "Mehr = groessere, traegere Zonen.")]
        [Range(1, 60)]
        public int Sessions { get => _sessions; set { _sessions = Math.Max(1, value); RecalculateValues(); } }

        [Display(Name = "HVN-Schwelle (% vom Max)", GroupName = "Profil", Order = 2,
            Description = "Ein Knoten gilt als HVN, wenn sein geglaettetes Volumen ueber diesem Prozentsatz des " +
                          "Profil-Maximums liegt. Hoeher = nur die dicksten Knoten.")]
        [Range(10, 99)]
        public int HvnRatioPct { get => _hvnRatioPct; set { _hvnRatioPct = Math.Clamp(value, 10, 99); RecalculateValues(); } }

        [Display(Name = "LVN-Schwelle (% vom Max)", GroupName = "Profil", Order = 3,
            Description = "Ein Tal gilt als LVN, wenn sein geglaettetes Volumen unter diesem Prozentsatz des " +
                          "Profil-Maximums liegt. Niedriger = nur sehr duenne Zonen.")]
        [Range(1, 90)]
        public int LvnRatioPct { get => _lvnRatioPct; set { _lvnRatioPct = Math.Clamp(value, 1, 90); RecalculateValues(); } }

        [Display(Name = "Glaettung (Ticks)", GroupName = "Profil", Order = 4,
            Description = "Glaettungsbreite des Profils in Ticks vor der HVN/LVN-Suche. Hoeher = ruhiger.")]
        [Range(0, 30)]
        public int Smoothing { get => _smoothing; set { _smoothing = Math.Clamp(value, 0, 30); RecalculateValues(); } }

        [Display(Name = "Tagesgrenze Stunde", GroupName = "Profil", Order = 5,
            Description = "Uhrzeit (Stunde), zu der ein neuer Profil-Tag beginnt = der Trading-Day-Rollover. " +
                          "Default 0 (Mitternacht): bei einem Chart in DE-Lokalzeit entspricht das ~dem CME-Index-" +
                          "Futures-Rollover (18:00 ET). An die Zeitzone deines Charts anpassen, falls noetig.")]
        [Range(0, 23)]
        public int DayStartHour { get => _dayStartHour; set { _dayStartHour = Math.Clamp(value, 0, 23); RecalculateValues(); } }

        [Display(Name = "Tagesgrenze Minute", GroupName = "Profil", Order = 6,
            Description = "Minute der Tagesgrenze (zusammen mit 'Tagesgrenze Stunde').")]
        [Range(0, 59)]
        public int DayStartMinute { get => _dayStartMinute; set { _dayStartMinute = Math.Clamp(value, 0, 59); RecalculateValues(); } }

        [Display(Name = "Max. HVN/LVN je Typ", GroupName = "Profil", Order = 7,
            Description = "Wie viele HVN bzw. LVN maximal gezeichnet werden. Behalten werden die STAERKSTEN " +
                          "(HVN = groesstes Volumen, LVN = duennste Taeler). Niedrig = nur die dominanten Zonen.")]
        [Range(1, 50)]
        public int MaxNodes { get => _maxNodes; set { _maxNodes = Math.Max(1, value); RecalculateValues(); } }

        [Display(Name = "Mindestabstand (Punkte)", GroupName = "Profil", Order = 8,
            Description = "Zwei Linien gleichen Typs (HVN/LVN/Naked POC) duerfen nicht naeher als dieser " +
                          "Preisabstand liegen - die staerkere bzw. juengere bleibt. Fasst dicht beieinander " +
                          "liegende Knoten zu einer Zone zusammen. 0 = aus. Fuer NQ ~50-80, fuer ES ~15-25.")]
        [Range(0, 100000)]
        public decimal MinSeparation { get => _minSep; set { _minSep = Math.Max(0m, value); RecalculateValues(); } }

        // ─────────────────────────────────────────────────────────────────
        //  PROPERTIES — Anzeige
        // ─────────────────────────────────────────────────────────────────
        [Display(Name = "HVN anzeigen", GroupName = "Anzeige", Order = 12,
            Description = "High Volume Nodes (Akzeptanz/Magnet-Zonen).")]
        public bool ShowHvn { get => _showHvn; set { _showHvn = value; RedrawChart(); } }

        [Display(Name = "LVN anzeigen", GroupName = "Anzeige", Order = 13,
            Description = "Low Volume Nodes (duenne Zonen / moegliche S/R beim Pullback).")]
        public bool ShowLvn { get => _showLvn; set { _showLvn = value; RedrawChart(); } }

        [Display(Name = "Naked POC anzeigen", GroupName = "Anzeige", Order = 14,
            Description = "vPOC einer frueheren Session, der seither NICHT wieder beruehrt wurde (starkes Magnet-Ziel). " +
                          "Verschwindet automatisch, sobald der Preis ihn antippt.")]
        public bool ShowNakedPoc { get => _showNakedPoc; set { _showNakedPoc = value; RedrawChart(); } }

        [Display(Name = "Max. Naked POCs", GroupName = "Anzeige", Order = 15,
            Description = "Wie viele der juengsten unberuehrten Vortages-POCs maximal gehalten werden.")]
        [Range(1, 200)]
        public int MaxNakedPocs { get => _maxNakedPocs; set { _maxNakedPocs = Math.Max(1, value); RecalculateValues(); } }

        // ─────────────────────────────────────────────────────────────────
        //  PROPERTIES — Darstellung
        // ─────────────────────────────────────────────────────────────────
        [Display(Name = "Linienbreite", GroupName = "Darstellung", Order = 20,
            Description = "Dicke der Linien in Pixeln (1-20).")]
        [Range(1, 20)]
        public int LineWidth { get => _lineWidth; set { _lineWidth = Math.Clamp(value, 1, 20); RedrawChart(); } }

        [Display(Name = "Labels anzeigen", GroupName = "Darstellung", Order = 21,
            Description = "Blendet die Text-Labels (HVN, LVN, nPOC) ein/aus.")]
        public bool ShowLabels { get => _showLabels; set { _showLabels = value; RedrawChart(); } }

        [Display(Name = "Schriftgroesse", GroupName = "Darstellung", Order = 22,
            Description = "Schriftgroesse der Labels.")]
        [Range(8, 24)]
        public int FontSize { get => _fontSize; set { _fontSize = Math.Clamp(value, 8, 24); BuildFonts(); RedrawChart(); } }

        [Display(Name = "Farbe HVN", GroupName = "Farben", Order = 32)]
        public Color ColorHvn { get => _colorHvn; set { _colorHvn = value; RedrawChart(); } }

        [Display(Name = "Farbe LVN", GroupName = "Farben", Order = 33)]
        public Color ColorLvn { get => _colorLvn; set { _colorLvn = value; RedrawChart(); } }

        [Display(Name = "Farbe Naked POC", GroupName = "Farben", Order = 34)]
        public Color ColorNakedPoc { get => _colorNakedPoc; set { _colorNakedPoc = value; RedrawChart(); } }

        // ─────────────────────────────────────────────────────────────────
        //  CTOR
        // ─────────────────────────────────────────────────────────────────
        public ProfileLevels() : base(true)
        {
            EnableCustomDrawing = true;
            DrawAbovePrice = true;
            DataSeries[0].IsHidden = true;

            // Pflicht fuer persistentes Custom-Drawing (siehe CLAUDE.md).
            SubscribeToDrawingEvents(DrawingLayouts.Historical | DrawingLayouts.Final);

            BuildFonts();
        }

        private void BuildFonts()
        {
            _font = new RenderFont("Consolas", _fontSize);
        }

        // ─────────────────────────────────────────────────────────────────
        //  HAUPTBERECHNUNG
        // ─────────────────────────────────────────────────────────────────
        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0)
                ResetState();

            int lastClosed = CurrentBar - 2;
            bool advanced = false;
            while (_lastProcessedBar < lastClosed)
            {
                _lastProcessedBar++;
                ProcessClosedBar(_lastProcessedBar);
                advanced = true;
            }

            // Level nur neu rechnen, wenn ein Bar geschlossen hat (nicht jeden Tick).
            if (advanced)
            {
                RecomputeLevels();
                RedrawChart();
            }
        }

        private void ResetState()
        {
            _history.Clear();
            _current = new Dictionary<decimal, decimal>();
            _curDate = DateTime.MinValue;
            _nakedPocs.Clear();
            _lines.Clear();
            _curSessionStartBar = -1;
            _lastProcessedBar = -1;
            _tickEstimate = 0m;
        }

        private void ProcessClosedBar(int bar)
        {
            var c = GetCandle(bar);
            if (c == null)
                return;

            if (_tickEstimate <= 0m)
                UpdateTickEstimate(c);

            // Tageswechsel -> aktuelles Tagesprofil einfrieren.
            var date = SessionDate(c.Time);
            if (date != _curDate)
            {
                if (_curDate != DateTime.MinValue && _current.Count > 0)
                {
                    _history.Add(_current);
                    while (_history.Count > _sessions - 1)
                        _history.RemoveAt(0);

                    // POC des abgeschlossenen Tages -> Naked-POC-Kandidat (noch unberuehrt).
                    decimal dayPoc = PocOf(_current);
                    if (dayPoc > 0m)
                    {
                        // Ursprung = LETZTER Bar der Session, dessen Range den POC einschloss
                        // -> der Ray startet dort, wo der Preis zuletzt auf dem POC war
                        // (nicht am Session-Schluss, der an Trendtagen weit weg liegt).
                        int origin = FindPocOriginBar(_curSessionStartBar, bar - 1, dayPoc);
                        _nakedPocs.Add(new NakedPoc(dayPoc, _curDate, origin));
                        while (_nakedPocs.Count > _maxNakedPocs)
                            _nakedPocs.RemoveAt(0);
                    }
                }
                _current = new Dictionary<decimal, decimal>();
                _curDate = date;
                _curSessionStartBar = bar;
            }

            // Footprint in das aktuelle Tagesprofil aggregieren.
            bool any = false;
            foreach (var pv in c.GetAllPriceLevels())
            {
                any = true;
                _current[pv.Price] = (_current.TryGetValue(pv.Price, out var v) ? v : 0m) + pv.Volume;
            }
            if (!any && c.Volume > 0m)
                _current[c.Close] = (_current.TryGetValue(c.Close, out var v2) ? v2 : 0m) + c.Volume;

            // Naked POCs, die dieser (spaetere) Bar preislich beruehrt, sind getestet -> entfernen.
            if (_nakedPocs.Count > 0)
                _nakedPocs.RemoveAll(np => np.Date < date && c.Low <= np.Price && np.Price <= c.High);
        }

        // ─────────────────────────────────────────────────────────────────
        //  LEVEL AUS DEM COMPOSITE
        // ─────────────────────────────────────────────────────────────────
        private void RecomputeLevels()
        {
            _lines.Clear();

            var comp = new Dictionary<decimal, decimal>();
            foreach (var dp in _history)
                foreach (var kv in dp)
                    comp[kv.Key] = (comp.TryGetValue(kv.Key, out var v) ? v : 0m) + kv.Value;
            foreach (var kv in _current)
                comp[kv.Key] = (comp.TryGetValue(kv.Key, out var v2) ? v2 : 0m) + kv.Value;

            if (comp.Count < 5)
                return;

            // Kontinuierliches, geglaettetes Profil fuer HVN/LVN.
            decimal tick = _tickEstimate > 0m ? _tickEstimate : InferTick(comp.Keys);
            decimal minP = comp.Keys.Min();
            decimal maxP = comp.Keys.Max();
            double[]? sm = null;
            int n = 0;
            if (tick > 0m)
            {
                n = (int)Math.Round((double)((maxP - minP) / tick)) + 1;
                if (n >= 5 && n <= 20000)
                {
                    var vol = new double[n];
                    foreach (var kv in comp)
                    {
                        int idx = (int)Math.Round((double)((kv.Key - minP) / tick));
                        if (idx >= 0 && idx < n) vol[idx] += (double)kv.Value;
                    }
                    int w = _smoothing;
                    sm = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        if (w <= 0) { sm[i] = vol[i]; continue; }
                        double s = 0; int cnt = 0;
                        for (int j = Math.Max(0, i - w); j <= Math.Min(n - 1, i + w); j++) { s += vol[j]; cnt++; }
                        sm[i] = s / cnt;
                    }
                }
            }

            // Alle Typen IMMER bauen; Sichtbarkeit + Farbe werden erst beim Zeichnen
            // aus den aktuellen Einstellungen aufgeloest -> Haken/Farben wirken sofort
            // (ohne Neuberechnung). Reihenfolge = Zeichen-Reihenfolge (Wichtiges zuletzt).
            if (sm != null)
            {
                AddLvnLines(sm, n, minP, tick);
                AddHvnLines(sm, n, minP, tick);
            }
            var nakedPicked = new List<decimal>();
            foreach (var np in _nakedPocs.OrderByDescending(x => x.Date))  // juengste zuerst
            {
                if (_minSep > 0m && nakedPicked.Any(p => Math.Abs(p - np.Price) < _minSep)) continue;
                nakedPicked.Add(np.Price);
                _lines.Add(new Line(np.Price, LevelKind.NakedPoc, np.OriginBar));   // Ray ab Ursprung
            }
        }

        private static decimal PocOf(Dictionary<decimal, decimal> hist)
        {
            decimal poc = 0m, maxVol = -1m;
            foreach (var kv in hist)
                if (kv.Value > maxVol) { maxVol = kv.Value; poc = kv.Key; }
            return poc;
        }

        // Letzter Bar im Bereich [startBar, endBar], dessen High/Low den POC-Preis
        // einschloss -> dort war der Preis zuletzt auf dem POC (Ray-Start des nPOC).
        private int FindPocOriginBar(int startBar, int endBar, decimal poc)
        {
            if (startBar < 0) startBar = 0;
            for (int b = endBar; b >= startBar; b--)
            {
                var cc = GetCandle(b);
                if (cc != null && cc.Low <= poc && poc <= cc.High)
                    return b;
            }
            return endBar;   // Fallback: Session-Schluss
        }

        // Ordnet einen Zeitpunkt dem Trading-Day zu, der bei der Tagesgrenze beginnt.
        // Bars vor der Grenze gehoeren noch zum vorherigen Trading-Day.
        private DateTime SessionDate(DateTime t)
        {
            var start = new TimeSpan(_dayStartHour, _dayStartMinute, 0);
            return t.TimeOfDay >= start ? t.Date : t.Date.AddDays(-1);
        }

        private void AddLine(decimal price, LevelKind kind)
            => _lines.Add(new Line(price, kind));

        // Waehlt aus Kandidaten die staerksten (max. _maxNodes) und haelt dabei den
        // Mindestabstand ein -> dicht beieinander liegende Knoten verschmelzen zu einem.
        private void AddNodesWithSeparation(List<(decimal price, double vol)> cand, bool strongestHigh, LevelKind kind)
        {
            var ordered = strongestHigh
                ? cand.OrderByDescending(x => x.vol)
                : cand.OrderBy(x => x.vol);

            var picked = new List<decimal>();
            foreach (var c in ordered)
            {
                if (picked.Count >= _maxNodes) break;
                if (_minSep > 0m && picked.Any(p => Math.Abs(p - c.price) < _minSep)) continue;
                picked.Add(c.price);
                AddLine(c.price, kind);
            }
        }

        private bool IsVisible(LevelKind k) => k switch
        {
            LevelKind.Hvn => _showHvn,
            LevelKind.Lvn => _showLvn,
            LevelKind.NakedPoc => _showNakedPoc,
            _ => true
        };

        private Color ColorOf(LevelKind k) => k switch
        {
            LevelKind.Hvn => _colorHvn,
            LevelKind.Lvn => _colorLvn,
            LevelKind.NakedPoc => _colorNakedPoc,
            _ => Color.White
        };

        private static string LabelOf(LevelKind k) => k switch
        {
            LevelKind.Hvn => "HVN",
            LevelKind.Lvn => "LVN",
            LevelKind.NakedPoc => "nPOC",
            _ => ""
        };

        // HVN = zusammenhaengender Lauf UEBER der Schwelle -> Peak je Lauf.
        // Es werden nur die staerksten _maxNodes Peaks (groesstes Volumen) behalten.
        private void AddHvnLines(double[] sm, int n, decimal minP, decimal tick)
        {
            double maxV = sm.Max();
            if (maxV <= 0) return;
            double thr = _hvnRatioPct / 100.0 * maxV;

            var cand = new List<(decimal price, double vol)>();
            int i = 0;
            while (i < n)
            {
                if (sm[i] < thr) { i++; continue; }
                int rs = i;
                while (i < n && sm[i] >= thr) i++;
                int re = i - 1;

                int mx = rs; double mv = sm[rs];
                for (int j = rs; j <= re; j++) if (sm[j] > mv) { mv = sm[j]; mx = j; }

                decimal price = minP + (decimal)mx * tick;
                cand.Add((price, mv));
            }
            AddNodesWithSeparation(cand, true, LevelKind.Hvn);
        }

        // LVN = zusammenhaengender Lauf UNTER der Schwelle, echtes Tal -> tiefster Punkt.
        // Es werden nur die ausgepraegtesten _maxNodes Taeler (geringstes Volumen) behalten.
        private void AddLvnLines(double[] sm, int n, decimal minP, decimal tick)
        {
            double maxV = sm.Max();
            if (maxV <= 0) return;
            double thr = _lvnRatioPct / 100.0 * maxV;

            var cand = new List<(decimal price, double vol)>();
            int i = 0;
            while (i < n)
            {
                if (sm[i] >= thr) { i++; continue; }
                int rs = i;
                while (i < n && sm[i] < thr) i++;
                int re = i - 1;

                int mn = rs; double mv = sm[rs];
                for (int j = rs; j <= re; j++) if (sm[j] < mv) { mv = sm[j]; mn = j; }

                double leftMax = 0, rightMax = 0;
                for (int j = 0; j < rs; j++) leftMax = Math.Max(leftMax, sm[j]);
                for (int j = re + 1; j < n; j++) rightMax = Math.Max(rightMax, sm[j]);
                if (leftMax <= mv || rightMax <= mv) continue;   // nur echte Taeler

                decimal price = minP + (decimal)mn * tick;
                cand.Add((price, mv));
            }
            AddNodesWithSeparation(cand, false, LevelKind.Lvn);
        }

        private void UpdateTickEstimate(IndicatorCandle c)
        {
            var prices = c.GetAllPriceLevels().Select(p => p.Price).OrderBy(p => p).ToList();
            for (int i = 1; i < prices.Count; i++)
            {
                var d = prices[i] - prices[i - 1];
                if (d > 0m && (_tickEstimate <= 0m || d < _tickEstimate))
                    _tickEstimate = d;
            }
        }

        private static decimal InferTick(IEnumerable<decimal> prices)
        {
            var sorted = prices.OrderBy(p => p).ToList();
            decimal tick = 0m;
            for (int i = 1; i < sorted.Count; i++)
            {
                var d = sorted[i] - sorted[i - 1];
                if (d > 0m && (tick <= 0m || d < tick)) tick = d;
            }
            return tick;
        }

        // ─────────────────────────────────────────────────────────────────
        //  RENDER (durchgehende horizontale S/R-Linien)
        // ─────────────────────────────────────────────────────────────────
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (_font == null || _lines.Count == 0)
                return;
            if (ChartInfo?.PriceChartContainer is not { } cont)
                return;

            var region = cont.Region;
            int lastBar = CurrentBar - 1;
            if (lastBar < 0)
                return;

            foreach (var ln in _lines)
            {
                if (!IsVisible(ln.Kind))
                    continue;

                int y;
                try { y = cont.GetYByPrice(ln.Price, false); }
                catch { continue; }

                int x1 = region.Left;          // HVN/LVN: volle Breite
                int x2 = region.Right;

                if (ln.OriginBar >= 0)
                {
                    // Naked POC: Ray ab dem Ursprungs-Bar nach rechts.
                    try
                    {
                        int ob = Math.Min(Math.Max(ln.OriginBar, 0), lastBar);
                        int xo = cont.GetXByBar(ob, false);
                        if (xo > region.Right) continue;   // Ursprung rechts ausserhalb -> nichts sichtbar
                        x1 = Math.Max(xo, region.Left);
                    }
                    catch { x1 = region.Left; }
                }

                var color = ColorOf(ln.Kind);
                context.DrawLine(new RenderPen(color, _lineWidth), x1, y, x2, y);

                if (_showLabels)
                {
                    try
                    {
                        string label = LabelOf(ln.Kind);
                        var sz = context.MeasureString(label, _font);
                        // Label rechts (knapp vor der Preisachse).
                        int labelX = region.Right - sz.Width - 3;
                        context.DrawString(label, _font, color, labelX, y - sz.Height - 1);
                    }
                    catch { /* Label diesmal weglassen */ }
                }
            }
        }
    }
}
