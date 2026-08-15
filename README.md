# QPRE ThetaData Engine

GEX (Gamma Exposure) calculation engine with Python backend and C# charting indicator for real-time options Greeks analysis.

## Files

### `thetadata_gex_engine.py`
Python service that connects to ThetaData API and calculates options Greeks metrics.

**Key Corrections:**
- ✅ **True Zero Gamma Calculation**: Now finds the strike with gamma closest to zero by:
  - Computing gamma exposure per strike: `OI × Gamma`
  - Grouping and summing across all strikes
  - Identifying the strike with minimum absolute gamma value
  - Previously: Simple average of call/put walls (incorrect)

- ✅ **Improved DEX Calculation**: Removed redundant multiplication, uses `OI × Delta` directly

- ✅ **Better Metrics**: Properly weights Vanna and Charm by OI

**Output JSON:**
```json
{
  "Symbol": "AAPL",
  "CallWall": 175.5,
  "PutWall": 174.2,
  "ZeroGamma": 174.85,
  "CallDelta": 176.0,
  "PutDelta": 173.5,
  "TotalVanna": 12450.5,
  "TotalCharm": -3200.75
}
```

### `QPRE_ThetaData_MC.cs`
PowerLanguage C# indicator for displaying GEX levels on charts.

**Key Corrections:**
- ✅ **Strong Type Deserialization**: Uses `GEXLevels` class instead of `Dictionary<string, double>`
  - Properly handles mixed data types (string Symbol + numeric values)
  - Prevents JSON parsing failures

- ✅ **Robust Error Handling**: 
  - Try-catch for JSON deserialization
  - Debug logging for failures
  - Graceful degradation

- ✅ **Thread-Safe Data**: 
  - Stores typed `GEXLevels` object instead of dictionary
  - Maintains existing lock mechanisms

**Rendered Levels:**
- 🔴 **CallWall** (Red) - Concentration of call open interest
- 🟢 **PutWall** (LimeGreen) - Concentration of put open interest  
- 🟡 **ZeroGamma** (Gold) - Strike with zero net gamma (inflection point)
- 🔵 **CallDelta** (Cyan) - Maximum call delta exposure
- 🟣 **PutDelta** (Magenta) - Maximum put delta exposure

**Metrics Panel:**
- Displays TotalVanna and TotalCharm above the current candle
- Updates in real-time as data refreshes

## Architecture

```
Python Service               File I/O           C# Indicator
┌──────────────────┐        ┌───────────┐      ┌─────────────┐
│ ThetaData API    │ ──────→│mc_levels  │ ──→  │   Chart     │
│ compute_gex()    │        │.json      │      │ RenderGFX() │
└──────────────────┘        └───────────┘      └─────────────┘
      ↑                           ↑
      │                           └── Atomic writes (tmp → final)
      │
   Reads active_symbol.txt
```

## Configuration

**Python (`thetadata_gex_engine.py`):**
```python
THETA_HOST = "http://127.0.0.1:25510"
BASE_DIR = r"C:\PYGEX"
SYMBOL_FILE = "active_symbol.txt"
EXPORT_PATH = "mc_levels.json"
REFRESH_INTERVAL = 30  # seconds
```

**C# (`QPRE_ThetaData_MC.cs`):**
```csharp
baseDir = @"C:\PYGEX"
symbolPath = "active_symbol.txt"
jsonPath = "mc_levels.json"
```

## Usage

1. **Start Python Engine:**
   ```bash
   python thetadata_gex_engine.py
   ```

2. **Add C# Indicator to Chart:**
   - Copy `QPRE_ThetaData_MC.cs` to your PowerLanguage indicators folder
   - Add indicator to chart
   - Engine automatically reads/writes symbol via shared files

3. **Monitor Output:**
   - Python console shows real-time calculations
   - Chart displays GEX levels with color coding
   - Metrics panel updates with Vanna/Charm values

## Improvements Over Original

| Issue | Original | Fixed |
|-------|----------|-------|
| Zero Gamma | Simple average (wrong) | Gamma-weighted strike selection ✓ |
| JSON Parsing | Dictionary<string, double> (fails) | Typed GEXLevels class ✓ |
| Error Handling | Silent failures | Debug logging + graceful degradation ✓ |
| Type Safety | No validation | Strong typing + null checks ✓ |
| DEX Calculation | Unclear multipliers | Consistent OI×Delta formula ✓ |

## Requirements

- Python 3.7+: `pandas`, `requests`
- C#: PowerLanguage, Newtonsoft.Json (Json.NET)
- ThetaData local API running on `http://127.0.0.1:25510`
