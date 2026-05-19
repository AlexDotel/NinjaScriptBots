from __future__ import annotations

import json
import subprocess
import sys
import threading
from pathlib import Path
from tkinter import BOTH, END, LEFT, RIGHT, X, Y, BooleanVar, DoubleVar, IntVar, StringVar, filedialog, messagebox, ttk
import tkinter as tk

import numpy as np
import pandas as pd

from train_es_regime_model import FEATURE_COLUMNS, add_features_and_labels, load_ninja_export, softmax


APP_DIR = Path(__file__).resolve().parent
DEFAULT_DATA_DIR = Path(r"C:\Users\joalr\Documents\# Data Exportada de Ninja")
DEFAULT_MODEL = APP_DIR / "es_regime_model.npz"

INDICATOR_CONFIG = {
    "ADX 14": "adx_14",
    "EMA spread / ATR": "ema_spread_atr",
    "EMA slow slope / ATR": "ema_slow_slope_atr",
    "RSI 14": "rsi_14",
    "ATR %": "atr_pct",
    "Trend R2 30": "trend_r2_30",
    "Bollinger width / ATR": "bb_width_atr",
    "Volume Z": "volume_z",
}


class RegimeGui(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("ES Regime Analyzer")
        self.geometry("1320x860")
        self.minsize(1120, 740)

        self.data_file = StringVar(value=str(self._default_data_file()))
        self.model_file = StringVar(value=str(DEFAULT_MODEL))
        self.horizon = IntVar(value=30)
        self.threshold_atr = DoubleVar(value=1.5)
        self.rows_to_plot = IntVar(value=260)
        self.status = StringVar(value="Listo")
        self.indicators = {name: BooleanVar(value=name in {"ADX 14", "EMA spread / ATR", "Trend R2 30", "RSI 14"}) for name in INDICATOR_CONFIG}
        self.latest_dataset: pd.DataFrame | None = None
        self.latest_probs: np.ndarray | None = None
        self.latest_classes: list[str] = []

        self._build_ui()

    def _default_data_file(self) -> Path:
        candidates = sorted(DEFAULT_DATA_DIR.glob("*.txt")) if DEFAULT_DATA_DIR.exists() else []
        return candidates[0] if candidates else DEFAULT_DATA_DIR

    def _build_ui(self) -> None:
        self.columnconfigure(0, weight=0)
        self.columnconfigure(1, weight=1)
        self.rowconfigure(0, weight=1)

        sidebar = ttk.Frame(self, padding=12)
        sidebar.grid(row=0, column=0, sticky="ns")
        sidebar.columnconfigure(0, weight=1)

        ttk.Label(sidebar, text="Fuente de datos").grid(row=0, column=0, sticky="w")
        data_entry = ttk.Entry(sidebar, textvariable=self.data_file, width=42)
        data_entry.grid(row=1, column=0, sticky="ew", pady=(4, 6))
        ttk.Button(sidebar, text="Elegir export", command=self.choose_data_file).grid(row=2, column=0, sticky="ew")

        ttk.Label(sidebar, text="Modelo").grid(row=3, column=0, sticky="w", pady=(14, 0))
        ttk.Entry(sidebar, textvariable=self.model_file, width=42).grid(row=4, column=0, sticky="ew", pady=(4, 6))
        ttk.Button(sidebar, text="Elegir modelo", command=self.choose_model_file).grid(row=5, column=0, sticky="ew")

        settings = ttk.LabelFrame(sidebar, text="Parametros", padding=10)
        settings.grid(row=6, column=0, sticky="ew", pady=(14, 0))
        settings.columnconfigure(1, weight=1)
        ttk.Label(settings, text="Horizonte min").grid(row=0, column=0, sticky="w")
        ttk.Spinbox(settings, from_=5, to=240, increment=5, textvariable=self.horizon, width=8).grid(row=0, column=1, sticky="e")
        ttk.Label(settings, text="Umbral ATR ref.").grid(row=1, column=0, sticky="w", pady=(8, 0))
        ttk.Spinbox(settings, from_=0.25, to=5.0, increment=0.25, textvariable=self.threshold_atr, width=8).grid(row=1, column=1, sticky="e", pady=(8, 0))
        ttk.Label(settings, text="Velas grafico").grid(row=2, column=0, sticky="w", pady=(8, 0))
        ttk.Spinbox(settings, from_=80, to=1500, increment=20, textvariable=self.rows_to_plot, width=8).grid(row=2, column=1, sticky="e", pady=(8, 0))

        indicators_box = ttk.LabelFrame(sidebar, text="Indicadores visibles", padding=10)
        indicators_box.grid(row=7, column=0, sticky="ew", pady=(14, 0))
        for idx, (name, var) in enumerate(self.indicators.items()):
            ttk.Checkbutton(indicators_box, text=name, variable=var).grid(row=idx, column=0, sticky="w", pady=1)

        ttk.Button(sidebar, text="Ejecutar analisis", command=self.run_analysis_threaded).grid(row=8, column=0, sticky="ew", pady=(16, 0))
        ttk.Button(sidebar, text="Entrenar modelo con esta fuente", command=self.train_model_threaded).grid(row=9, column=0, sticky="ew", pady=(6, 0))
        ttk.Button(sidebar, text="Abrir carpeta de resultados", command=self.open_results_folder).grid(row=10, column=0, sticky="ew", pady=(6, 0))

        self.summary = tk.Text(sidebar, height=14, width=42, wrap="word")
        self.summary.grid(row=11, column=0, sticky="nsew", pady=(14, 0))
        sidebar.rowconfigure(11, weight=1)

        main = ttk.Frame(self, padding=(0, 12, 12, 12))
        main.grid(row=0, column=1, sticky="nsew")
        main.columnconfigure(0, weight=1)
        main.rowconfigure(1, weight=1)

        header = ttk.Frame(main)
        header.grid(row=0, column=0, sticky="ew", pady=(0, 10))
        header.columnconfigure(1, weight=1)
        ttk.Label(header, textvariable=self.status).grid(row=0, column=0, sticky="w")
        ttk.Button(header, text="Guardar resumen", command=self.save_summary).grid(row=0, column=2, sticky="e")

        panes = ttk.PanedWindow(main, orient=tk.VERTICAL)
        panes.grid(row=1, column=0, sticky="nsew")

        chart_frame = ttk.Frame(panes)
        chart_frame.rowconfigure(0, weight=3)
        chart_frame.rowconfigure(1, weight=1)
        chart_frame.columnconfigure(0, weight=1)
        panes.add(chart_frame, weight=4)

        self.price_canvas = tk.Canvas(chart_frame, height=410, bg="#fbfbf8", highlightthickness=1, highlightbackground="#d8d8d0")
        self.price_canvas.grid(row=0, column=0, sticky="nsew")
        self.indicator_canvas = tk.Canvas(chart_frame, height=170, bg="#ffffff", highlightthickness=1, highlightbackground="#d8d8d0")
        self.indicator_canvas.grid(row=1, column=0, sticky="nsew", pady=(8, 0))

        bottom = ttk.PanedWindow(panes, orient=tk.HORIZONTAL)
        panes.add(bottom, weight=2)

        left_bottom = ttk.Frame(bottom)
        left_bottom.columnconfigure(0, weight=1)
        left_bottom.rowconfigure(1, weight=1)
        ttk.Label(left_bottom, text="Estado, riesgo de cambio y ultimas velas").grid(row=0, column=0, sticky="w")
        self.table = ttk.Treeview(left_bottom, columns=("time", "close", "regime", "risk", "prediction", "minutes", "next"), show="headings", height=9)
        headings = {
            "time": "Hora",
            "close": "Close",
            "regime": "Regimen actual",
            "risk": "Riesgo cambio",
            "prediction": "Estado futuro",
            "minutes": "Cambio real",
            "next": "Siguiente regimen",
        }
        for col, title in headings.items():
            self.table.heading(col, text=title)
            self.table.column(col, width=110, anchor="center")
        self.table.grid(row=1, column=0, sticky="nsew")
        bottom.add(left_bottom, weight=3)

        right_bottom = ttk.Frame(bottom)
        right_bottom.columnconfigure(0, weight=1)
        right_bottom.rowconfigure(1, weight=1)
        ttk.Label(right_bottom, text="Salida CMD").grid(row=0, column=0, sticky="w")
        self.console = tk.Text(right_bottom, height=10, wrap="word", bg="#111111", fg="#e7e7e7", insertbackground="#ffffff")
        self.console.grid(row=1, column=0, sticky="nsew")
        bottom.add(right_bottom, weight=2)

    def choose_data_file(self) -> None:
        path = filedialog.askopenfilename(
            initialdir=str(DEFAULT_DATA_DIR if DEFAULT_DATA_DIR.exists() else Path.home()),
            title="Elegir export de NinjaTrader",
            filetypes=[("Ninja export", "*.txt *.csv"), ("Todos", "*.*")],
        )
        if path:
            self.data_file.set(path)

    def choose_model_file(self) -> None:
        path = filedialog.askopenfilename(
            initialdir=str(APP_DIR),
            title="Elegir modelo",
            filetypes=[("Modelo NPZ", "*.npz"), ("Todos", "*.*")],
        )
        if path:
            self.model_file.set(path)

    def open_results_folder(self) -> None:
        subprocess.Popen(["explorer", str(APP_DIR)])

    def run_analysis_threaded(self) -> None:
        threading.Thread(target=self.run_analysis, daemon=True).start()

    def train_model_threaded(self) -> None:
        threading.Thread(target=self.train_model, daemon=True).start()

    def set_status(self, text: str) -> None:
        self.after(0, lambda: self.status.set(text))

    def append_console(self, text: str) -> None:
        def write() -> None:
            self.console.insert(END, text + "\n")
            self.console.see(END)

        self.after(0, write)

    def run_analysis(self) -> None:
        try:
            self.set_status("Cargando datos y ejecutando modelo...")
            data_path = Path(self.data_file.get())
            model_path = Path(self.model_file.get())
            if not data_path.exists():
                raise FileNotFoundError(f"No existe la fuente de datos: {data_path}")
            if not model_path.exists():
                raise FileNotFoundError(f"No existe el modelo: {model_path}")

            self.append_console(f"> analizar {data_path}")
            model = np.load(model_path, allow_pickle=True)
            horizon = int(model["horizon"][0]) if "horizon" in model else self.horizon.get()
            threshold = float(model["threshold_atr"][0]) if "threshold_atr" in model else self.threshold_atr.get()
            class_names = [str(name) for name in model["class_names"]]

            raw = load_ninja_export(data_path)
            dataset = add_features_and_labels(raw, horizon, threshold)
            dataset = dataset.dropna(subset=FEATURE_COLUMNS).reset_index(drop=True)
            x = dataset[FEATURE_COLUMNS].to_numpy(dtype=float)
            x_std = (x - model["mean"]) / model["std"]
            probs = softmax(x_std @ model["weights"] + model["bias"])
            preds = probs.argmax(axis=1)
            dataset["model_prediction"] = [class_names[int(idx)] for idx in preds]
            for idx, name in enumerate(class_names):
                dataset[f"prob_{name}"] = probs[:, idx]
            if "change_soon" in class_names:
                dataset["prob_change_soon"] = probs[:, class_names.index("change_soon")]
            else:
                dataset["prob_change_soon"] = 0.0

            self.latest_dataset = dataset
            self.latest_probs = probs
            self.latest_classes = class_names
            self.after(0, self.render_results)
            self.set_status(f"Analisis completado: {len(dataset):,} velas")
        except Exception as exc:
            self.set_status("Error")
            self.append_console(f"ERROR: {exc}")
            self.after(0, lambda: messagebox.showerror("Error", str(exc)))

    def train_model(self) -> None:
        try:
            data_path = Path(self.data_file.get())
            if not data_path.exists():
                raise FileNotFoundError(f"No existe la fuente de datos: {data_path}")
            self.set_status("Entrenando modelo en CMD interno...")
            cmd = [
                sys.executable,
                str(APP_DIR / "train_es_regime_model.py"),
                "--data",
                str(data_path),
                "--out-dir",
                str(APP_DIR),
                "--horizon",
                str(self.horizon.get()),
                "--threshold-atr",
                str(self.threshold_atr.get()),
            ]
            self.append_console("> " + " ".join(f'"{part}"' if " " in part else part for part in cmd))
            proc = subprocess.run(cmd, capture_output=True, text=True, cwd=str(APP_DIR.parent))
            if proc.stdout:
                self.append_console(proc.stdout[-5000:])
            if proc.stderr:
                self.append_console(proc.stderr[-5000:])
            if proc.returncode != 0:
                raise RuntimeError(f"Entrenamiento fallido con codigo {proc.returncode}")
            self.model_file.set(str(APP_DIR / "es_regime_model.npz"))
            self.set_status("Modelo reentrenado")
            self.run_analysis()
        except Exception as exc:
            self.set_status("Error")
            self.append_console(f"ERROR: {exc}")
            self.after(0, lambda: messagebox.showerror("Error", str(exc)))

    def selected_indicator_columns(self) -> list[tuple[str, str]]:
        return [(name, col) for name, col in INDICATOR_CONFIG.items() if self.indicators[name].get()]

    def render_results(self) -> None:
        if self.latest_dataset is None:
            return
        df = self.latest_dataset
        latest = df.iloc[-1]
        selected = self.selected_indicator_columns()
        self.render_summary(df, latest)
        self.render_price_chart(df.tail(self.rows_to_plot.get()))
        self.render_indicator_chart(df.tail(self.rows_to_plot.get()), selected)
        self.render_table(df.tail(20))

    def render_summary(self, df: pd.DataFrame, latest: pd.Series) -> None:
        counts = df["current_regime"].value_counts(normalize=True).to_dict()
        pred_counts = df["model_prediction"].value_counts(normalize=True).to_dict()
        lines = [
            f"Archivo: {Path(self.data_file.get()).name}",
            f"Velas analizadas: {len(df):,}",
            f"Periodo: {df['timestamp'].iloc[0]} -> {df['timestamp'].iloc[-1]}",
            "",
            f"Ultimo close: {latest['close']:.2f}",
            f"Regimen actual: {latest['current_regime']}",
            f"Riesgo de cambio: {latest.get('prob_change_soon', 0):.1%}",
            f"Prediccion estado: {latest['model_prediction']}",
            f"Cambio real en ventana: {self.format_minutes_to_change(latest)}",
            "",
            "Lectura simple:",
            self.human_readable_take(latest),
            "",
            "Distribucion regimen actual:",
            f"Range {counts.get('range', 0):.1%} | Trend {counts.get('trend', 0):.1%}",
            "Distribucion riesgo:",
            f"Estable {pred_counts.get('stable', 0):.1%} | Cambio pronto {pred_counts.get('change_soon', 0):.1%}",
        ]
        self.summary.delete("1.0", END)
        self.summary.insert("1.0", "\n".join(lines))

    def human_readable_take(self, row: pd.Series) -> str:
        adx = float(row["adx_14"])
        change_prob = float(row.get("prob_change_soon", 0))
        if change_prob >= 0.70:
            return f"Alta probabilidad de cambio de estado dentro de la ventana. Regimen actual: {row['current_regime']}. ADX: {adx:.1f}."
        if change_prob >= 0.55:
            return f"Riesgo moderado de cambio de estado. Conviene vigilar ruptura, expansion de rango y volumen. ADX: {adx:.1f}."
        return f"El estado actual parece estable por ahora. Regimen actual: {row['current_regime']}. ADX: {adx:.1f}."

    def format_minutes_to_change(self, row: pd.Series) -> str:
        value = row.get("minutes_to_regime_change")
        if pd.isna(value):
            return "No dentro de la ventana"
        next_regime = row.get("next_regime")
        if pd.isna(next_regime):
            next_regime = "desconocido"
        return f"{int(value)} min -> {next_regime}"

    def render_price_chart(self, data: pd.DataFrame) -> None:
        canvas = self.price_canvas
        canvas.delete("all")
        w = max(canvas.winfo_width(), 900)
        h = max(canvas.winfo_height(), 320)
        pad_l, pad_r, pad_t, pad_b = 58, 18, 24, 34
        close = data["close"].to_numpy(dtype=float)
        if len(close) < 2:
            return
        y_min, y_max = float(close.min()), float(close.max())
        if y_min == y_max:
            y_min -= 1
            y_max += 1
        plot_w = w - pad_l - pad_r
        plot_h = h - pad_t - pad_b

        def x_at(i: int) -> float:
            return pad_l + plot_w * i / (len(close) - 1)

        def y_at(value: float) -> float:
            return pad_t + plot_h * (1 - (value - y_min) / (y_max - y_min))

        color_map = {"range": "#f3f0df", "trend": "#dff1e5", "trend_down": "#dff1e5", "trend_up": "#dff1e5"}
        regimes = data["current_regime"].tolist()
        start = 0
        for i in range(1, len(regimes) + 1):
            if i == len(regimes) or regimes[i] != regimes[start]:
                canvas.create_rectangle(x_at(start), pad_t, x_at(max(i - 1, start)), pad_t + plot_h, fill=color_map.get(regimes[start], "#eeeeee"), outline="")
                start = i

        for grid in range(5):
            y = pad_t + plot_h * grid / 4
            value = y_max - (y_max - y_min) * grid / 4
            canvas.create_line(pad_l, y, w - pad_r, y, fill="#deded8")
            canvas.create_text(8, y, text=f"{value:.2f}", anchor="w", fill="#555555", font=("Segoe UI", 9))

        points = []
        for i, value in enumerate(close):
            points.extend([x_at(i), y_at(float(value))])
        canvas.create_line(points, fill="#1f2933", width=2)
        canvas.create_text(pad_l, 12, text="Precio con fondo por regimen actual", anchor="w", fill="#222222", font=("Segoe UI", 10, "bold"))
        last_time = str(data["timestamp"].iloc[-1])
        canvas.create_text(w - pad_r, h - 14, text=last_time, anchor="e", fill="#555555", font=("Segoe UI", 9))

    def render_indicator_chart(self, data: pd.DataFrame, selected: list[tuple[str, str]]) -> None:
        canvas = self.indicator_canvas
        canvas.delete("all")
        w = max(canvas.winfo_width(), 900)
        h = max(canvas.winfo_height(), 150)
        pad_l, pad_r, pad_t, pad_b = 58, 18, 22, 28
        if not selected:
            canvas.create_text(w / 2, h / 2, text="Selecciona uno o mas indicadores", fill="#666666", font=("Segoe UI", 11))
            return
        plot_w = w - pad_l - pad_r
        plot_h = h - pad_t - pad_b
        colors = ["#0f766e", "#c2410c", "#2563eb", "#7c3aed", "#b45309", "#be123c", "#4d7c0f", "#334155"]

        def normalize(values: np.ndarray) -> np.ndarray:
            clean = values.astype(float)
            lo, hi = np.nanpercentile(clean, [3, 97])
            if not np.isfinite(lo) or not np.isfinite(hi) or lo == hi:
                lo, hi = np.nanmin(clean), np.nanmax(clean)
            if lo == hi:
                return np.full_like(clean, 0.5)
            return np.clip((clean - lo) / (hi - lo), 0, 1)

        canvas.create_line(pad_l, pad_t + plot_h / 2, w - pad_r, pad_t + plot_h / 2, fill="#e2e2dc")
        legend_x = pad_l
        for idx, (name, col) in enumerate(selected):
            values = normalize(data[col].to_numpy(dtype=float))
            points = []
            for i, value in enumerate(values):
                x = pad_l + plot_w * i / max(len(values) - 1, 1)
                y = pad_t + plot_h * (1 - value)
                points.extend([x, y])
            color = colors[idx % len(colors)]
            if len(points) >= 4:
                canvas.create_line(points, fill=color, width=2)
            canvas.create_rectangle(legend_x, 7, legend_x + 10, 17, fill=color, outline="")
            canvas.create_text(legend_x + 14, 12, text=name, anchor="w", fill="#333333", font=("Segoe UI", 9))
            legend_x += 125

    def render_table(self, data: pd.DataFrame) -> None:
        for item in self.table.get_children():
            self.table.delete(item)
        for _, row in data.iloc[::-1].iterrows():
            self.table.insert(
                "",
                END,
                values=(
                    str(row["timestamp"]),
                    f"{row['close']:.2f}",
                    row["current_regime"],
                    f"{row.get('prob_change_soon', 0):.1%}",
                    row["model_prediction"],
                    self.format_minutes_to_change(row),
                    "" if pd.isna(row.get("next_regime")) else row.get("next_regime"),
                ),
            )

    def save_summary(self) -> None:
        if self.latest_dataset is None:
            messagebox.showinfo("Sin resultados", "Ejecuta un analisis primero.")
            return
        out_path = APP_DIR / "last_gui_analysis_summary.json"
        latest = self.latest_dataset.iloc[-1]
        payload = {
            "data_file": self.data_file.get(),
            "model_file": self.model_file.get(),
            "timestamp": str(latest["timestamp"]),
            "close": float(latest["close"]),
            "current_regime": str(latest["current_regime"]),
            "prediction": str(latest["model_prediction"]),
            "prob_change_soon": float(latest.get("prob_change_soon", 0)),
            "minutes_to_regime_change": None
            if pd.isna(latest.get("minutes_to_regime_change"))
            else int(latest.get("minutes_to_regime_change")),
            "next_regime": None if pd.isna(latest.get("next_regime")) else str(latest.get("next_regime")),
            "probabilities": {
                "stable": float(latest.get("prob_stable", 0)),
                "change_soon": float(latest.get("prob_change_soon", 0)),
            },
            "selected_indicators": [name for name, _ in self.selected_indicator_columns()],
        }
        out_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        messagebox.showinfo("Guardado", f"Resumen guardado en:\n{out_path}")


if __name__ == "__main__":
    app = RegimeGui()
    app.mainloop()
