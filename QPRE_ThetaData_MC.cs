using System;
using System.Drawing;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using PowerLanguage;

namespace PowerLanguage.Indicator {

    // ── Lightweight data container ─────────────────────────────────────────────
    internal class GEXLevels {
        public string Symbol    { get; set; }
        public double CallWall  { get; set; }
        public double PutWall   { get; set; }
        public double ZeroGamma { get; set; }
        public double CallDelta { get; set; }
        public double PutDelta  { get; set; }
        public double TotalVanna{ get; set; }
        public double TotalCharm{ get; set; }
    }

    // ── Per-strike accumulator ─────────────────────────────────────────────────
    internal class StrikeData {
        public double CallWeight, PutWeight;
        public double CallGammaExp, PutGammaExp;
        public double CallDex, PutDex;
        public double TotalVanna, TotalCharm;
        public long   TotalOI;
    }

    public class QPRE_ThetaData_MC : IndicatorObject {

        // ══════════════════════════════════════════════════════════════════════
        //  CONFIGURATION — PASTE YOUR API KEY HERE
        // ══════════════════════════════════════════════════════════════════════
        private const string THETA_API_KEY = "tu_api_key_aqui"; // ← REPLACE WITH YOUR KEY
        private const string THETA_HOST    = "http://127.0.0.1:25510";
        private const int    REFRESH_SEC   = 30;
        // ══════════════════════════════════════════════════════════════════════

        public QPRE_ThetaData_MC(object _ctx) : base(_ctx) { }

        // ── Per-instance HTTP client (avoids shared-state header races) ────────
        private HttpClient _http;

        // ── State ──────────────────────────────────────────────────────────────
        private readonly object  _dataLock    = new object();
        private int              _fetching    = 0; // 0 = idle, 1 = in-flight
        private GEXLevels        _activeLevels = null;
        private string           _lastSymbol  = "";
        private DateTime         _lastFetch   = DateTime.MinValue;

        // ── Drawing objects ───────────────────────────────────────────────────
        private readonly Dictionary<string, ITrendLineObject> _lines =
            new Dictionary<string, ITrendLineObject>();
        private ITextObject _panel;

        // ── Line colour map ───────────────────────────────────────────────────
        private static readonly Dictionary<string, Color> _colours =
            new Dictionary<string, Color> {
                { "CallWall",  Color.Red      },
                { "PutWall",   Color.LimeGreen },
                { "ZeroGamma", Color.Gold      },
                { "CallDelta", Color.Cyan      },
                { "PutDelta",  Color.Magenta   }
            };

        // ── Lifecycle ─────────────────────────────────────────────────────────
        protected override void Create() {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrEmpty(THETA_API_KEY) && THETA_API_KEY != "tu_api_key_aqui")
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", THETA_API_KEY);
        }

        protected override void Destroy() {
            ClearGraphics();
            try { _http?.Dispose(); } catch { }
        }

        // ── Main calculation loop ─────────────────────────────────────────────
        protected override void CalcBar() {
            if (Bars == null || Bars.Count == 0) return;
            if (Environment.IsAutoLoop && !Environment.IsFinalBarCalculate) return;

            string sym = Bars.Symbol;

            // Symbol changed → clear old drawings and force refresh
            if (sym != _lastSymbol) {
                ClearGraphics();
                _lastSymbol = sym;
                _lastFetch  = DateTime.MinValue;
            }

            // Kick off async fetch when due — atomically claim the fetch slot
            bool due = (DateTime.UtcNow - _lastFetch).TotalSeconds >= REFRESH_SEC;
            if (due && Interlocked.CompareExchange(ref _fetching, 1, 0) == 0) {
                _lastFetch = DateTime.UtcNow;
                string symCapture = sym;
                Task.Run(() => FetchAndCompute(symCapture));
            }

            RenderGraphics();
        }

        // ── API call + calculation (runs on thread-pool) ──────────────────────
        private async Task FetchAndCompute(string symbol) {
            try {
                string root = symbol.Split('.')[0].Split(' ')[0].ToUpper();
                string url  = string.Format("{0}/v2/bulk_snapshot/option/greeks?root={1}&exp=0",
                                THETA_HOST, Uri.EscapeDataString(root));

                HttpResponseMessage resp = await _http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                GEXLevels levels = ComputeFromJson(root, body);

                if (levels != null) {
                    lock (_dataLock) { _activeLevels = levels; }
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine("[QPRE] FetchAndCompute error: " + ex.Message);
            } finally {
                Interlocked.Exchange(ref _fetching, 0);
            }
        }

        // ── JSON parsing + Greeks computation (NuGet-free) ───────────────────
        //
        // The ThetaData response looks like:
        //   { "response": [ { "contract": {...}, "ticks": [{...}] }, ... ] }
        //
        // We iterate over each element in the top-level "response" array,
        // extract the "contract" and first "ticks" objects by balanced-brace
        // scanning, then pull individual field values with regex.
        private static GEXLevels ComputeFromJson(string symbol, string json) {
            try {
                var strikes = new Dictionary<double, StrikeData>();

                // Locate the "response" array
                int responseStart = json.IndexOf("\"response\"", StringComparison.Ordinal);
                if (responseStart < 0) return null;

                int arrayOpen = json.IndexOf('[', responseStart);
                if (arrayOpen < 0) return null;

                // Walk through each top-level element of the array
                int pos = arrayOpen + 1;
                int arrayClose = json.Length - 1;

                while (pos < arrayClose) {
                    // Skip whitespace / commas
                    while (pos < json.Length && (json[pos] == ',' || json[pos] == ' ' ||
                           json[pos] == '\r' || json[pos] == '\n' || json[pos] == '\t')) pos++;

                    if (pos >= json.Length || json[pos] == ']') break;
                    if (json[pos] != '{') { pos++; continue; }

                    // Extract the full element object by bracket matching
                    int elemEnd = FindMatchingBrace(json, pos);
                    if (elemEnd < 0) break;
                    string elem = json.Substring(pos, elemEnd - pos + 1);
                    pos = elemEnd + 1;

                    // Extract "contract" object
                    string contractStr = ExtractObject(elem, "contract");
                    if (string.IsNullOrEmpty(contractStr)) continue;

                    // Extract first element of "ticks" array
                    string ticksStr = ExtractFirstArrayObject(elem, "ticks");

                    double strikeRaw = ExtractDouble(contractStr, "strike");
                    string right     = ExtractString(contractStr, "right");

                    if (strikeRaw <= 0 || (right != "C" && right != "P")) continue;

                    double strike = strikeRaw / 1000.0;

                    long   oi    = (long)ExtractDouble(ticksStr ?? "", "open_interest");
                    long   vol   = (long)ExtractDouble(ticksStr ?? "", "volume");
                    double delta = ExtractDouble(ticksStr ?? "", "delta");
                    double gamma = ExtractDouble(ticksStr ?? "", "gamma");
                    double vanna = ExtractDouble(ticksStr ?? "", "vanna");
                    double charm = ExtractDouble(ticksStr ?? "", "charm");

                    if (!strikes.TryGetValue(strike, out StrikeData sd)) {
                        sd = new StrikeData();
                        strikes[strike] = sd;
                    }

                    double weight = oi + vol;

                    if (right == "C") {
                        sd.CallWeight   += weight;
                        sd.CallGammaExp += oi * gamma;
                        sd.CallDex      += oi * delta;
                    } else {
                        sd.PutWeight    += weight;
                        sd.PutGammaExp  += oi * gamma;
                        sd.PutDex       += oi * delta;
                    }

                    sd.TotalVanna += oi * vanna;
                    sd.TotalCharm += oi * charm;
                    sd.TotalOI    += oi;
                }

                if (strikes.Count == 0) return null;

                // ── Derive levels ──────────────────────────────────────────────
                double callWall = 0, putWall = 0, zeroGamma = 0,
                       callDelta = 0, putDelta = 0;
                double maxCallW    = double.MinValue, maxPutW  = double.MinValue;
                double minAbsGamma = double.MaxValue;
                double maxCallDex  = double.MinValue, minPutDex = double.MaxValue;
                double totalVanna  = 0, totalCharm = 0;

                foreach (var kvp in strikes) {
                    double     s  = kvp.Key;
                    StrikeData sd = kvp.Value;

                    if (sd.CallWeight > maxCallW) { maxCallW = sd.CallWeight; callWall = s; }
                    if (sd.PutWeight  > maxPutW)  { maxPutW  = sd.PutWeight;  putWall  = s; }

                    double netGamma = Math.Abs(sd.CallGammaExp + sd.PutGammaExp);
                    if (netGamma < minAbsGamma) { minAbsGamma = netGamma; zeroGamma = s; }

                    if (sd.CallDex > maxCallDex) { maxCallDex = sd.CallDex; callDelta = s; }
                    if (sd.PutDex  < minPutDex)  { minPutDex  = sd.PutDex;  putDelta  = s; }

                    totalVanna += sd.TotalVanna;
                    totalCharm += sd.TotalCharm;
                }

                return new GEXLevels {
                    Symbol     = symbol,
                    CallWall   = callWall,
                    PutWall    = putWall,
                    ZeroGamma  = zeroGamma,
                    CallDelta  = callDelta,
                    PutDelta   = putDelta,
                    TotalVanna = Math.Round(totalVanna, 2),
                    TotalCharm = Math.Round(totalCharm, 2)
                };
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine("[QPRE] ComputeFromJson error: " + ex.Message);
                return null;
            }
        }

        // ── Balanced-brace scanner ────────────────────────────────────────────
        // Returns the index of the closing '}' that matches the '{' at startIndex.
        private static int FindMatchingBrace(string s, int startIndex) {
            int depth = 0;
            for (int i = startIndex; i < s.Length; i++) {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        // Returns the content of the named JSON object field as a raw substring
        // (including the surrounding braces), or null if not found.
        private static string ExtractObject(string json, string key) {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int braceOpen = json.IndexOf('{', keyIdx);
            if (braceOpen < 0) return null;
            int braceClose = FindMatchingBrace(json, braceOpen);
            if (braceClose < 0) return null;
            return json.Substring(braceOpen, braceClose - braceOpen + 1);
        }

        // Returns the raw JSON string of the first object inside a named array.
        private static string ExtractFirstArrayObject(string json, string key) {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int arrOpen = json.IndexOf('[', keyIdx);
            if (arrOpen < 0) return null;
            int objOpen = json.IndexOf('{', arrOpen);
            if (objOpen < 0) return null;
            int objClose = FindMatchingBrace(json, objOpen);
            if (objClose < 0) return null;
            return json.Substring(objOpen, objClose - objOpen + 1);
        }

        // ── Minimal JSON field extractors ─────────────────────────────────────
        private static double ExtractDouble(string json, string key) {
            Match m = Regex.Match(json,
                "\"" + Regex.Escape(key) + @"""\s*:\s*(-?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)");
            if (m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
            return 0.0;
        }

        private static string ExtractString(string json, string key) {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + @"""\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        // ── Graphics ──────────────────────────────────────────────────────────
        private void RenderGraphics() {
            GEXLevels lv;
            lock (_dataLock) {
                if (_activeLevels == null) return;
                lv = _activeLevels;
            }

            if (Bars.Count == 0) return;
            DateTime now = Bars.Time[0];

            var levelMap = new Dictionary<string, double> {
                { "CallWall",  lv.CallWall  },
                { "PutWall",   lv.PutWall   },
                { "ZeroGamma", lv.ZeroGamma },
                { "CallDelta", lv.CallDelta },
                { "PutDelta",  lv.PutDelta  }
            };

            foreach (var kvp in levelMap) {
                if (kvp.Value == 0) continue;

                if (!_lines.TryGetValue(kvp.Key, out ITrendLineObject line) || line == null) {
                    ChartPoint p1 = new ChartPoint(now, kvp.Value);
                    ChartPoint p2 = new ChartPoint(now.AddMinutes(15), kvp.Value);
                    line = DrwTrendLine.Create(p1, p2);
                    line.Color    = _colours[kvp.Key];
                    line.Size     = 2;
                    line.ExtRight = true;
                    _lines[kvp.Key] = line;
                } else if (Math.Abs(line.StartPoint.Price - kvp.Value) > 0.001) {
                    line.StartPoint = new ChartPoint(now, kvp.Value);
                    line.EndPoint   = new ChartPoint(now.AddMinutes(15), kvp.Value);
                }
            }

            string txt = string.Format("VANNA: {0:N0}\nCHARM: {1:N0}", lv.TotalVanna, lv.TotalCharm);
            double priceLvl = Bars.High[0] + Bars.TrueRange[0] * 0.5;

            if (_panel == null) {
                _panel = DrwText.Create(new ChartPoint(now, priceLvl), txt);
                _panel.Color   = Color.White;
                _panel.BGColor = Color.DarkSlateGray;
                _panel.HStyle  = ETextStyleH.Left;
                _panel.VStyle  = ETextStyleV.Bottom;
            } else {
                _panel.Location = new ChartPoint(now, priceLvl);
                _panel.Text     = txt;
            }
        }

        private void ClearGraphics() {
            foreach (var line in _lines.Values) {
                try { line?.Delete(); } catch { }
            }
            _lines.Clear();

            if (_panel != null) {
                try { _panel.Delete(); } catch { }
                _panel = null;
            }

            lock (_dataLock) { _activeLevels = null; }
        }
    }
}

namespace PowerLanguage.Indicator {

    // ── Lightweight data container ─────────────────────────────────────────────
    internal class GEXLevels {
        public string Symbol    { get; set; }
        public double CallWall  { get; set; }
        public double PutWall   { get; set; }
        public double ZeroGamma { get; set; }
        public double CallDelta { get; set; }
        public double PutDelta  { get; set; }
        public double TotalVanna{ get; set; }
        public double TotalCharm{ get; set; }
    }

    // ── Per-strike accumulator ─────────────────────────────────────────────────
    internal class StrikeData {
        public double CallWeight, PutWeight;
        public double CallGammaExp, PutGammaExp;
        public double CallDex, PutDex;
        public double TotalVanna, TotalCharm;
        public long   TotalOI;
    }

    public class QPRE_ThetaData_MC : IndicatorObject {

        // ══════════════════════════════════════════════════════════════════════
        //  CONFIGURACIÓN — PEGA TU API KEY AQUÍ
        // ══════════════════════════════════════════════════════════════════════
        private const string THETA_API_KEY = "tu_api_key_aqui"; // ← REEMPLAZA CON TU KEY
        private const string THETA_HOST    = "http://127.0.0.1:25510";
        private const int    REFRESH_SEC   = 30;
        // ══════════════════════════════════════════════════════════════════════

        public QPRE_ThetaData_MC(object _ctx) : base(_ctx) { }

        // ── State ──────────────────────────────────────────────────────────────
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private readonly object  _dataLock     = new object();
        private volatile bool    _fetching      = false;
        private GEXLevels        _activeLevels  = null;
        private string           _lastSymbol    = "";
        private DateTime         _lastFetch     = DateTime.MinValue;

        // ── Drawing objects ───────────────────────────────────────────────────
        private readonly Dictionary<string, ITrendLineObject> _lines =
            new Dictionary<string, ITrendLineObject>();
        private ITextObject _panel;

        // ── Line colour map ───────────────────────────────────────────────────
        private static readonly Dictionary<string, Color> _colours =
            new Dictionary<string, Color> {
                { "CallWall",  Color.Red      },
                { "PutWall",   Color.LimeGreen },
                { "ZeroGamma", Color.Gold      },
                { "CallDelta", Color.Cyan      },
                { "PutDelta",  Color.Magenta   }
            };

        // ── Lifecycle ─────────────────────────────────────────────────────────
        protected override void Create() {
            _http.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrEmpty(THETA_API_KEY) && THETA_API_KEY != "tu_api_key_aqui")
                _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",
                    "Bearer " + THETA_API_KEY);
        }

        protected override void Destroy() {
            ClearGraphics();
        }

        // ── Main calculation loop ─────────────────────────────────────────────
        protected override void CalcBar() {
            if (Bars == null || Bars.Count == 0) return;
            if (Environment.IsAutoLoop && !Environment.IsFinalBarCalculate) return;

            string sym = Bars.Symbol;

            // Symbol changed → clear old drawings and force refresh
            if (sym != _lastSymbol) {
                ClearGraphics();
                _lastSymbol = sym;
                _lastFetch  = DateTime.MinValue;
            }

            // Kick off async fetch when due
            bool due = (DateTime.UtcNow - _lastFetch).TotalSeconds >= REFRESH_SEC;
            if (due && !_fetching) {
                _fetching  = true;
                _lastFetch = DateTime.UtcNow;
                string symCapture = sym;
                Task.Run(() => FetchAndCompute(symCapture));
            }

            RenderGraphics();
        }

        // ── API call + calculation (runs on thread-pool) ──────────────────────
        private async Task FetchAndCompute(string symbol) {
            try {
                string root = symbol.Split('.')[0].Split(' ')[0].ToUpper();
                string url  = $"{THETA_HOST}/v2/bulk_snapshot/option/greeks?root={Uri.EscapeDataString(root)}&exp=0";

                HttpResponseMessage resp = await _http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                GEXLevels levels = ComputeFromJson(root, body);

                if (levels != null) {
                    lock (_dataLock) { _activeLevels = levels; }
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine("[QPRE] FetchAndCompute error: " + ex.Message);
            } finally {
                _fetching = false;
            }
        }

        // ── JSON parsing + Greeks computation (NuGet-free) ───────────────────
        private static GEXLevels ComputeFromJson(string symbol, string json) {
            try {
                // Accumulate per-strike data
                var strikes = new Dictionary<double, StrikeData>();

                // Each option contract is an object with "contract" and "ticks" keys.
                // We scan for all contract blocks using regex on the raw JSON string.
                MatchCollection contractBlocks = Regex.Matches(json,
                    @"""contract""\s*:\s*\{([^}]+)\}.*?""ticks""\s*:\s*\[([^\]]*)\]",
                    RegexOptions.Singleline);

                foreach (Match m in contractBlocks) {
                    string contractStr = m.Groups[1].Value;
                    string ticksStr    = m.Groups[2].Value;

                    double strikeRaw = ExtractDouble(contractStr, "strike");
                    string right     = ExtractString(contractStr, "right");

                    if (strikeRaw <= 0 || (right != "C" && right != "P")) continue;

                    double strike = strikeRaw / 1000.0;

                    long   oi     = (long)ExtractDouble(ticksStr, "open_interest");
                    long   vol    = (long)ExtractDouble(ticksStr, "volume");
                    double delta  = ExtractDouble(ticksStr, "delta");
                    double gamma  = ExtractDouble(ticksStr, "gamma");
                    double vanna  = ExtractDouble(ticksStr, "vanna");
                    double charm  = ExtractDouble(ticksStr, "charm");

                    if (!strikes.TryGetValue(strike, out StrikeData sd)) {
                        sd = new StrikeData();
                        strikes[strike] = sd;
                    }

                    double weight = oi + vol;

                    if (right == "C") {
                        sd.CallWeight   += weight;
                        sd.CallGammaExp += oi * gamma;
                        sd.CallDex      += oi * delta;
                    } else {
                        sd.PutWeight    += weight;
                        sd.PutGammaExp  += oi * gamma;
                        sd.PutDex       += oi * delta;
                    }

                    sd.TotalVanna += oi * vanna;
                    sd.TotalCharm += oi * charm;
                    sd.TotalOI    += oi;
                }

                if (strikes.Count == 0) return null;

                // ── Derived levels ─────────────────────────────────────────────
                double callWall = 0, putWall = 0, zeroGamma = 0,
                       callDelta = 0, putDelta = 0;
                double maxCallW = double.MinValue, maxPutW = double.MinValue;
                double minAbsGamma = double.MaxValue;
                double maxCallDex = double.MinValue, minPutDex = double.MaxValue;
                double totalVanna = 0, totalCharm = 0;

                foreach (var kvp in strikes) {
                    double s  = kvp.Key;
                    StrikeData sd = kvp.Value;

                    if (sd.CallWeight > maxCallW) { maxCallW = sd.CallWeight; callWall = s; }
                    if (sd.PutWeight  > maxPutW)  { maxPutW  = sd.PutWeight;  putWall  = s; }

                    double netGamma = Math.Abs(sd.CallGammaExp + sd.PutGammaExp);
                    if (netGamma < minAbsGamma) { minAbsGamma = netGamma; zeroGamma = s; }

                    if (sd.CallDex > maxCallDex) { maxCallDex = sd.CallDex; callDelta = s; }
                    if (sd.PutDex  < minPutDex)  { minPutDex  = sd.PutDex;  putDelta  = s; }

                    totalVanna += sd.TotalVanna;
                    totalCharm += sd.TotalCharm;
                }

                return new GEXLevels {
                    Symbol     = symbol,
                    CallWall   = callWall,
                    PutWall    = putWall,
                    ZeroGamma  = zeroGamma,
                    CallDelta  = callDelta,
                    PutDelta   = putDelta,
                    TotalVanna = Math.Round(totalVanna, 2),
                    TotalCharm = Math.Round(totalCharm, 2)
                };
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine("[QPRE] ComputeFromJson error: " + ex.Message);
                return null;
            }
        }

        // ── Minimal JSON field extractors ─────────────────────────────────────
        private static double ExtractDouble(string json, string key) {
            Match m = Regex.Match(json,
                "\"" + Regex.Escape(key) + @"""\s*:\s*(-?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)");
            if (m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
            return 0.0;
        }

        private static string ExtractString(string json, string key) {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + @"""\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        // ── Graphics ──────────────────────────────────────────────────────────
        private void RenderGraphics() {
            GEXLevels lv;
            lock (_dataLock) {
                if (_activeLevels == null) return;
                lv = _activeLevels;
            }

            if (Bars.Count == 0) return;
            DateTime now = Bars.Time[0];

            var levelMap = new Dictionary<string, double> {
                { "CallWall",  lv.CallWall  },
                { "PutWall",   lv.PutWall   },
                { "ZeroGamma", lv.ZeroGamma },
                { "CallDelta", lv.CallDelta },
                { "PutDelta",  lv.PutDelta  }
            };

            foreach (var kvp in levelMap) {
                if (kvp.Value == 0) continue;

                if (!_lines.TryGetValue(kvp.Key, out ITrendLineObject line) || line == null) {
                    ChartPoint p1 = new ChartPoint(now, kvp.Value);
                    ChartPoint p2 = new ChartPoint(now.AddMinutes(15), kvp.Value);
                    line = DrwTrendLine.Create(p1, p2);
                    line.Color    = _colours[kvp.Key];
                    line.Size     = 2;
                    line.ExtRight = true;
                    _lines[kvp.Key] = line;
                } else if (Math.Abs(line.StartPoint.Price - kvp.Value) > 0.001) {
                    line.StartPoint = new ChartPoint(now, kvp.Value);
                    line.EndPoint   = new ChartPoint(now.AddMinutes(15), kvp.Value);
                }
            }

            string txt = string.Format("VANNA: {0:N0}\nCHARM: {1:N0}", lv.TotalVanna, lv.TotalCharm);
            double priceLvl = Bars.High[0] + Bars.TrueRange[0] * 0.5;

            if (_panel == null) {
                _panel = DrwText.Create(new ChartPoint(now, priceLvl), txt);
                _panel.Color   = Color.White;
                _panel.BGColor = Color.DarkSlateGray;
                _panel.HStyle  = ETextStyleH.Left;
                _panel.VStyle  = ETextStyleV.Bottom;
            } else {
                _panel.Location = new ChartPoint(now, priceLvl);
                _panel.Text     = txt;
            }
        }

        private void ClearGraphics() {
            foreach (var line in _lines.Values) {
                try { line?.Delete(); } catch { }
            }
            _lines.Clear();

            if (_panel != null) {
                try { _panel.Delete(); } catch { }
                _panel = null;
            }

            lock (_dataLock) { _activeLevels = null; }
        }
    }
}
