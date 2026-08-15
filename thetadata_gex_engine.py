import os
import json
import time
import warnings
import requests
import pandas as pd

warnings.filterwarnings("ignore", category=FutureWarning, module="pandas")
warnings.filterwarnings("ignore", category=UserWarning)

# ============ PEGA TU API KEY AQUÍ ============
THETA_API_KEY = "tu_api_key_aqui"  # ← REEMPLAZA "tu_api_key_aqui" CON TU VERDADERA KEY
# ============================================

THETA_HOST = "http://127.0.0.1:25510"
BASE_DIR = r"C:\PYGEX"
SYMBOL_FILE = os.path.join(BASE_DIR, "active_symbol.txt")
EXPORT_PATH = os.path.join(BASE_DIR, "mc_levels.json")

class ThetaDataGEXEngine:
    def __init__(self, host: str = THETA_HOST, api_key: str = THETA_API_KEY):
        self.host = host
        self.api_key = api_key

    def compute_gex_levels(self, symbol: str) -> dict:
        try:
            clean_symbol = symbol.split('.')[0].split(' ')[0].upper()
            url = f"{self.host}/v2/bulk_snapshot/option/greeks"
            
            # Preparar headers con API Key
            headers = {}
            if self.api_key and self.api_key != "tu_api_key_aqui":
                headers["Authorization"] = f"Bearer {self.api_key}"
            
            response = requests.get(url, params={"root": clean_symbol, "exp": "0"}, headers=headers, timeout=5)
            if response.status_code != 200: 
                return {}

            data = response.json()
            if not data or "response" not in data or not isinstance(data["response"], list): 
                return {}

            records = []
            for item in data["response"]:
                contract = item.get("contract", {})
                ticks_list = item.get("ticks", [])
                ticks = ticks_list[0] if isinstance(ticks_list, list) and len(ticks_list) > 0 else {}
                
                strike = contract.get("strike", 0) / 1000.0
                right = contract.get("right")
                
                if strike > 0 and right in ["C", "P"]:
                    records.append({
                        "strike": strike,
                        "right": right,
                        "oi": ticks.get("open_interest", 0) or 0,
                        "volume": ticks.get("volume", 0) or 0,
                        "delta": ticks.get("delta", 0.0) or 0.0,
                        "gamma": ticks.get("gamma", 0.0) or 0.0,
                        "vanna": ticks.get("vanna", 0.0) or 0.0,
                        "charm": ticks.get("charm", 0.0) or 0.0
                    })

            df = pd.DataFrame(records)
            if df.empty: 
                return {}

            calls = df[df['right'] == 'C'].copy()
            puts = df[df['right'] == 'P'].copy()

            # --- GEX (Call/Put Walls) ---
            calls['weight'] = calls['oi'] + calls['volume']
            puts['weight'] = puts['oi'] + puts['volume']

            call_wall = float(calls.groupby('strike')['weight'].sum().idxmax()) if not calls.empty and calls['weight'].sum() > 0 else 0.0
            put_wall = float(puts.groupby('strike')['weight'].sum().idxmax()) if not puts.empty and puts['weight'].sum() > 0 else 0.0
            
            # --- ZERO GAMMA (Fixed: True Gamma Accumulation) ---
            calls['gamma_exposure'] = calls['oi'] * calls['gamma']
            puts['gamma_exposure'] = puts['oi'] * puts['gamma']
            
            call_gamma_grouped = calls.groupby('strike')['gamma_exposure'].sum()
            put_gamma_grouped = puts.groupby('strike')['gamma_exposure'].sum()
            
            all_strikes = pd.concat([call_gamma_grouped, put_gamma_grouped]).groupby(level=0).sum()
            
            if not all_strikes.empty:
                zero_gamma = float(all_strikes.abs().idxmin())
            else:
                zero_gamma = 0.0

            # --- DEX (Delta Exposure Walls) ---
            calls['dex'] = calls['oi'] * calls['delta']
            puts['dex'] = puts['oi'] * puts['delta']

            call_dex_grouped = calls.groupby('strike')['dex'].sum()
            put_dex_grouped = puts.groupby('strike')['dex'].sum()

            call_delta = float(call_dex_grouped.idxmax()) if not call_dex_grouped.empty and call_dex_grouped.max() > 0 else 0.0
            put_delta = float(put_dex_grouped.idxmin()) if not put_dex_grouped.empty and put_dex_grouped.min() < 0 else 0.0

            # --- VANNA & CHARM (System Metrics) ---
            total_vanna = round((df['oi'] * df['vanna']).sum(), 2)
            total_charm = round((df['oi'] * df['charm']).sum(), 2)

            return {
                "Symbol": clean_symbol,
                "CallWall": call_wall,
                "PutWall": put_wall,
                "ZeroGamma": zero_gamma,
                "CallDelta": call_delta,
                "PutDelta": put_delta,
                "TotalVanna": total_vanna,
                "TotalCharm": total_charm
            }
        except Exception as e:
            return {}

def export_atomic(path: str, data: dict):
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        tmp_path = path + ".tmp"
        with open(tmp_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
        os.replace(tmp_path, path)
    except PermissionError:
        pass 
    except Exception:
        pass

if __name__ == "__main__":
    engine = ThetaDataGEXEngine()
    last_symbol, last_calc_time = "", 0
    REFRESH_INTERVAL = 30 

    print("🚀 Motor QPRE-ThetaData (v3) + DEX/Vanna/Charm iniciado.")

    while True:
        try:
            current_time = time.time()
            if os.path.exists(SYMBOL_FILE):
                with open(SYMBOL_FILE, "r", encoding="utf-8") as f:
                    active_symbol = f.read().strip()

                if active_symbol and (active_symbol != last_symbol or (current_time - last_calc_time) > REFRESH_INTERVAL):
                    levels = engine.compute_gex_levels(active_symbol)

                    if levels:
                        export_atomic(EXPORT_PATH, levels)
                        print(f"[{time.strftime('%H:%M:%S')}] {active_symbol} | CW: {levels['CallWall']} | PW: {levels['PutWall']} | ZG: {levels['ZeroGamma']} | Vanna: {levels['TotalVanna']}")

                    last_symbol = active_symbol
                    last_calc_time = current_time
        except PermissionError:
            pass 
        except Exception:
            pass 

        time.sleep(1)
