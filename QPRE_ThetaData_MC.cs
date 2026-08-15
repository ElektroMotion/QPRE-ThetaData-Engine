using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;
using PowerLanguage;
using Newtonsoft.Json;

namespace PowerLanguage.Indicator {
    // Strongly-typed class for JSON deserialization
    public class GEXLevels {
        [JsonProperty("Symbol")]
        public string Symbol { get; set; }

        [JsonProperty("CallWall")]
        public double CallWall { get; set; }

        [JsonProperty("PutWall")]
        public double PutWall { get; set; }

        [JsonProperty("ZeroGamma")]
        public double ZeroGamma { get; set; }

        [JsonProperty("CallDelta")]
        public double CallDelta { get; set; }

        [JsonProperty("PutDelta")]
        public double PutDelta { get; set; }

        [JsonProperty("TotalVanna")]
        public double TotalVanna { get; set; }

        [JsonProperty("TotalCharm")]
        public double TotalCharm { get; set; }
    }

    public class QPRE_ThetaData_MC : IndicatorObject {
        public QPRE_ThetaData_MC(object _ctx) : base(_ctx) {}

        private readonly string baseDir = @"C:\PYGEX";
        private string symbolPath;
        private string jsonPath;

        private string lastSentSymbol = "";
        private DateTime lastReadTime = DateTime.MinValue;

        private volatile bool isReading = false;
        private readonly object dataLock = new object();
        private GEXLevels activeLevels = null;
        
        private readonly Dictionary<string, ITrendLineObject> levelLines = new Dictionary<string, ITrendLineObject>();
        private ITextObject infoPanel;

        protected override void Create() {
            symbolPath = Path.Combine(baseDir, "active_symbol.txt");
            jsonPath = Path.Combine(baseDir, "mc_levels.json");
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
        }

        protected override void Destroy() {
            ClearOldGraphics();
        }

        protected override void CalcBar() {
            if (Bars == null || Bars.Count == 0) return;

            NotifySymbolToPython();
            if (Environment.IsAutoLoop && !Environment.IsFinalBarCalculate) return;

            if (!isReading) {
                isReading = true;
                Task.Run(() => FetchLevelsAsync());
            }

            RenderGraphics();
        }

        private void NotifySymbolToPython() {
            string currentSymbol = Bars.Symbol;
            if (currentSymbol != lastSentSymbol) {
                try {
                    File.WriteAllText(symbolPath, currentSymbol);
                    if (lastSentSymbol != "" && lastSentSymbol != currentSymbol) {
                        ClearOldGraphics();
                    }
                    lastSentSymbol = currentSymbol;
                } catch (IOException) {}
            }
        }

        private void ClearOldGraphics() {
            foreach (var line in levelLines.Values) {
                try { line?.Delete(); } catch {}
            }
            levelLines.Clear();
            
            if (infoPanel != null) {
                try { infoPanel.Delete(); } catch {}
                infoPanel = null;
            }

            lock (dataLock) {
                activeLevels = null;
            }
        }

        private void FetchLevelsAsync() {
            try {
                if (File.Exists(jsonPath)) {
                    DateTime fileMod = File.GetLastWriteTime(jsonPath);
                    if (fileMod > lastReadTime) {
                        lastReadTime = fileMod;
                        string jsonText = File.ReadAllText(jsonPath);
                        
                        try {
                            var parsed = JsonConvert.DeserializeObject<GEXLevels>(jsonText);

                            if (parsed != null) {
                                lock (dataLock) { 
                                    activeLevels = parsed; 
                                }
                            }
                        } catch (JsonException ex) {
                            // Log JSON parsing error but continue
                            System.Diagnostics.Debug.WriteLine($"JSON Parse Error: {ex.Message}");
                        }
                    }
                }
            } catch (IOException) { } 
            finally { isReading = false; }
        }

        private void RenderGraphics() {
            GEXLevels currentLevels;
            lock (dataLock) {
                if (activeLevels == null) return;
                currentLevels = activeLevels;
            }

            if (Bars.Count == 0) return;
            DateTime currentTime = Bars.Time[0];

            // Dictionary to hold levels for rendering
            var levelDict = new Dictionary<string, double> {
                { "CallWall", currentLevels.CallWall },
                { "PutWall", currentLevels.PutWall },
                { "ZeroGamma", currentLevels.ZeroGamma },
                { "CallDelta", currentLevels.CallDelta },
                { "PutDelta", currentLevels.PutDelta }
            };

            // Render trend lines for GEX and DEX levels
            foreach (var kvp in levelDict) {
                if (kvp.Value == 0) continue; // Skip empty levels

                if (!levelLines.TryGetValue(kvp.Key, out var line) || line == null) {
                    ChartPoint p1 = new ChartPoint(currentTime, kvp.Value);
                    ChartPoint p2 = new ChartPoint(currentTime.AddMinutes(15), kvp.Value);
                    
                    line = DrwTrendLine.Create(p1, p2);
                    
                    // Color assignment
                    if (kvp.Key == "CallWall") line.Color = Color.Red;
                    else if (kvp.Key == "PutWall") line.Color = Color.LimeGreen;
                    else if (kvp.Key == "ZeroGamma") line.Color = Color.Gold;
                    else if (kvp.Key == "CallDelta") line.Color = Color.Cyan;
                    else if (kvp.Key == "PutDelta") line.Color = Color.Magenta;

                    line.Size = 2;
                    line.ExtRight = true;
                    levelLines[kvp.Key] = line;
                } else {
                    // Update existing line position
                    if (Math.Abs(line.StartPoint.Price - kvp.Value) > 0.001) {
                        line.StartPoint = new ChartPoint(currentTime, kvp.Value);
                        line.EndPoint = new ChartPoint(currentTime.AddMinutes(15), kvp.Value);
                    }
                }
            }

            // Update metrics panel with Vanna and Charm
            string panelText = $"VANNA: {currentLevels.TotalVanna:N0}\nCHARM: {currentLevels.TotalCharm:N0}";
            
            // Position text above the last bar
            double textPriceLevel = Bars.High[0] + (Bars.TrueRange[0] * 0.5);

            if (infoPanel == null) {
                infoPanel = DrwText.Create(new ChartPoint(currentTime, textPriceLevel), panelText);
                infoPanel.Color = Color.White;
                infoPanel.BGColor = Color.DarkSlateGray;
                infoPanel.HStyle = ETextStyleH.Left;
                infoPanel.VStyle = ETextStyleV.Bottom;
            } else {
                infoPanel.Location = new ChartPoint(currentTime, textPriceLevel);
                infoPanel.Text = panelText;
            }
        }
    }
}
