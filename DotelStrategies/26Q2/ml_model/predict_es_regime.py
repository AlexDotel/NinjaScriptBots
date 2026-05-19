from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd

from train_es_regime_model import FEATURE_COLUMNS, add_features_and_labels, load_ninja_export, softmax


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--model", type=Path, default=Path(__file__).with_name("es_regime_model.npz"))
    parser.add_argument("--last-rows", type=int, default=5)
    args = parser.parse_args()

    model = np.load(args.model, allow_pickle=True)
    horizon = int(model["horizon"][0])
    threshold_atr = float(model["threshold_atr"][0])
    class_names = [str(name) for name in model["class_names"]]

    raw = load_ninja_export(args.data)
    enriched = add_features_and_labels(raw, horizon, threshold_atr)
    dataset = enriched.dropna(subset=FEATURE_COLUMNS).reset_index(drop=True)
    latest = dataset.tail(args.last_rows).copy()
    x = latest[FEATURE_COLUMNS].to_numpy(dtype=float)
    x_std = (x - model["mean"]) / model["std"]
    probs = softmax(x_std @ model["weights"] + model["bias"])
    preds = probs.argmax(axis=1)

    rows = []
    for i, (_, row) in enumerate(latest.iterrows()):
        rows.append(
            {
                "timestamp": str(row["timestamp"]),
                "close": float(row["close"]),
                "current_regime": str(row["current_regime"]),
                "prediction": class_names[int(preds[i])],
                "probabilities": {name: float(probs[i, idx]) for idx, name in enumerate(class_names)},
                "prob_change_soon": float(probs[i, class_names.index("change_soon")])
                if "change_soon" in class_names
                else None,
                "adx_14": float(row["adx_14"]),
                "ema_spread_atr": float(row["ema_spread_atr"]),
                "minutes_to_regime_change": None
                if np.isnan(row["minutes_to_regime_change"])
                else int(row["minutes_to_regime_change"]),
                "next_regime": None if pd.isna(row["next_regime"]) else str(row["next_regime"]),
                "future_move_atr": None if np.isnan(row["future_move_atr"]) else float(row["future_move_atr"]),
            }
        )

    print(json.dumps(rows, indent=2))


if __name__ == "__main__":
    main()
