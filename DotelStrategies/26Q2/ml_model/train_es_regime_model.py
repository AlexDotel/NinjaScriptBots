from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd


FEATURE_COLUMNS = [
    "ret_1",
    "ret_3",
    "ret_5",
    "ret_15",
    "body_atr",
    "range_atr",
    "volume_z",
    "ema_fast_slope_atr",
    "ema_slow_slope_atr",
    "ema_spread_atr",
    "atr_pct",
    "adx_14",
    "rsi_14",
    "bb_width_atr",
    "close_pos_20",
    "realized_vol_20",
    "trend_r2_30",
]

CLASS_NAMES = ["stable", "change_soon"]


def load_ninja_export(path: Path) -> pd.DataFrame:
    df = pd.read_csv(
        path,
        sep=";",
        header=None,
        names=["timestamp", "open", "high", "low", "close", "volume"],
    )
    df["timestamp"] = pd.to_datetime(df["timestamp"], format="%Y%m%d %H%M%S")
    return df.sort_values("timestamp").reset_index(drop=True)


def ema(series: pd.Series, span: int) -> pd.Series:
    return series.ewm(span=span, adjust=False).mean()


def rsi(close: pd.Series, period: int = 14) -> pd.Series:
    delta = close.diff()
    gain = delta.clip(lower=0).ewm(alpha=1 / period, adjust=False).mean()
    loss = (-delta.clip(upper=0)).ewm(alpha=1 / period, adjust=False).mean()
    rs = gain / loss.replace(0, np.nan)
    return 100 - (100 / (1 + rs))


def adx(df: pd.DataFrame, period: int = 14) -> pd.Series:
    high = df["high"]
    low = df["low"]
    close = df["close"]
    up_move = high.diff()
    down_move = -low.diff()
    plus_dm = pd.Series(np.where((up_move > down_move) & (up_move > 0), up_move, 0.0), index=df.index)
    minus_dm = pd.Series(np.where((down_move > up_move) & (down_move > 0), down_move, 0.0), index=df.index)
    tr = true_range(df)
    atr_value = tr.ewm(alpha=1 / period, adjust=False).mean()
    plus_di = 100 * plus_dm.ewm(alpha=1 / period, adjust=False).mean() / atr_value.replace(0, np.nan)
    minus_di = 100 * minus_dm.ewm(alpha=1 / period, adjust=False).mean() / atr_value.replace(0, np.nan)
    dx = 100 * (plus_di - minus_di).abs() / (plus_di + minus_di).replace(0, np.nan)
    return dx.ewm(alpha=1 / period, adjust=False).mean()


def true_range(df: pd.DataFrame) -> pd.Series:
    prev_close = df["close"].shift(1)
    ranges = pd.concat(
        [
            df["high"] - df["low"],
            (df["high"] - prev_close).abs(),
            (df["low"] - prev_close).abs(),
        ],
        axis=1,
    )
    return ranges.max(axis=1)


def rolling_trend_r2(close: pd.Series, window: int = 30) -> pd.Series:
    x = np.arange(window, dtype=float)
    x = (x - x.mean()) / x.std()

    def score(values: np.ndarray) -> float:
        y = values.astype(float)
        y_std = y.std()
        if y_std == 0:
            return 0.0
        y = (y - y.mean()) / y_std
        corr = float(np.dot(x, y) / window)
        return corr * corr

    return close.rolling(window).apply(score, raw=True)


def add_features_and_labels(df: pd.DataFrame, horizon: int, threshold_atr: float) -> pd.DataFrame:
    out = df.copy()
    close = out["close"]
    tr = true_range(out)
    out["atr_14"] = tr.ewm(alpha=1 / 14, adjust=False).mean()
    out["ema_fast"] = ema(close, 12)
    out["ema_slow"] = ema(close, 48)
    out["ret_1"] = close.pct_change(1)
    out["ret_3"] = close.pct_change(3)
    out["ret_5"] = close.pct_change(5)
    out["ret_15"] = close.pct_change(15)
    out["body_atr"] = (out["close"] - out["open"]) / out["atr_14"].replace(0, np.nan)
    out["range_atr"] = (out["high"] - out["low"]) / out["atr_14"].replace(0, np.nan)
    vol_mean = out["volume"].rolling(60).mean()
    vol_std = out["volume"].rolling(60).std()
    out["volume_z"] = (out["volume"] - vol_mean) / vol_std.replace(0, np.nan)
    out["ema_fast_slope_atr"] = (out["ema_fast"] - out["ema_fast"].shift(5)) / out["atr_14"].replace(0, np.nan)
    out["ema_slow_slope_atr"] = (out["ema_slow"] - out["ema_slow"].shift(15)) / out["atr_14"].replace(0, np.nan)
    out["ema_spread_atr"] = (out["ema_fast"] - out["ema_slow"]) / out["atr_14"].replace(0, np.nan)
    out["atr_pct"] = out["atr_14"] / close
    out["adx_14"] = adx(out, 14)
    out["rsi_14"] = rsi(close, 14)
    rolling_std = close.rolling(20).std()
    out["bb_width_atr"] = (4 * rolling_std) / out["atr_14"].replace(0, np.nan)
    low_20 = out["low"].rolling(20).min()
    high_20 = out["high"].rolling(20).max()
    out["close_pos_20"] = (close - low_20) / (high_20 - low_20).replace(0, np.nan)
    out["realized_vol_20"] = out["ret_1"].rolling(20).std()
    out["trend_r2_30"] = rolling_trend_r2(close, 30)

    future_move_atr = (close.shift(-horizon) - close) / out["atr_14"].replace(0, np.nan)
    out["future_move_atr"] = future_move_atr
    out["target"] = 0
    out.loc[future_move_atr < -threshold_atr, "target"] = 1
    out.loc[future_move_atr > threshold_atr, "target"] = 2
    out["target_name"] = out["target"].map(dict(enumerate(CLASS_NAMES)))
    trend_strength = out["adx_14"].fillna(0)
    trend_shape = out["trend_r2_30"].fillna(0)
    trend_slope = out["ema_slow_slope_atr"].fillna(0)
    is_trend = (trend_strength >= 20) & ((trend_shape >= 0.20) | (trend_slope.abs() >= 0.50))
    out["current_regime"] = "range"
    out.loc[is_trend, "current_regime"] = "trend"
    out["raw_regime"] = out["current_regime"]
    trend_vote = (out["raw_regime"] == "trend").rolling(5, min_periods=1).mean()
    out["current_regime"] = np.where(trend_vote >= 0.60, "trend", "range")
    minutes_to_change = np.full(len(out), np.nan)
    next_regime = np.full(len(out), None, dtype=object)
    regimes = out["current_regime"].to_numpy()
    for i in range(len(regimes)):
        end = min(i + horizon + 1, len(regimes))
        future = np.flatnonzero(regimes[i + 1 : end] != regimes[i])
        if len(future):
            minutes = int(future[0] + 1)
            minutes_to_change[i] = minutes
            next_regime[i] = regimes[i + minutes]
    out["minutes_to_regime_change"] = minutes_to_change
    out["next_regime"] = next_regime
    has_full_horizon = np.arange(len(out)) + horizon < len(out)
    out["target"] = np.where(np.isfinite(minutes_to_change), 1.0, 0.0)
    out.loc[~has_full_horizon, "target"] = np.nan
    out["target_name"] = out["target"].map({0.0: "stable", 1.0: "change_soon"})
    return out


def standardize_train_test(x_train: np.ndarray, x_test: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    mean = x_train.mean(axis=0)
    std = x_train.std(axis=0)
    std[std == 0] = 1.0
    return (x_train - mean) / std, (x_test - mean) / std, mean, std


def softmax(logits: np.ndarray) -> np.ndarray:
    shifted = logits - logits.max(axis=1, keepdims=True)
    exp = np.exp(shifted)
    return exp / exp.sum(axis=1, keepdims=True)


def train_multinomial_logistic(
    x: np.ndarray,
    y: np.ndarray,
    class_weights: np.ndarray,
    epochs: int = 1800,
    lr: float = 0.08,
    l2: float = 0.002,
) -> tuple[np.ndarray, np.ndarray]:
    n_rows, n_features = x.shape
    n_classes = len(CLASS_NAMES)
    weights = np.zeros((n_features, n_classes), dtype=float)
    bias = np.zeros(n_classes, dtype=float)
    y_one_hot = np.eye(n_classes)[y]
    sample_weights = class_weights[y]

    for _ in range(epochs):
        probs = softmax(x @ weights + bias)
        error = (probs - y_one_hot) * sample_weights[:, None]
        grad_w = (x.T @ error) / n_rows + l2 * weights
        grad_b = error.mean(axis=0)
        weights -= lr * grad_w
        bias -= lr * grad_b

    return weights, bias


def evaluate(x: np.ndarray, y: np.ndarray, weights: np.ndarray, bias: np.ndarray) -> dict:
    probs = softmax(x @ weights + bias)
    pred = probs.argmax(axis=1)
    confusion = np.zeros((len(CLASS_NAMES), len(CLASS_NAMES)), dtype=int)
    for actual, predicted in zip(y, pred):
        confusion[actual, predicted] += 1

    per_class = {}
    for idx, name in enumerate(CLASS_NAMES):
        tp = confusion[idx, idx]
        fp = confusion[:, idx].sum() - tp
        fn = confusion[idx, :].sum() - tp
        precision = tp / (tp + fp) if (tp + fp) else 0.0
        recall = tp / (tp + fn) if (tp + fn) else 0.0
        per_class[name] = {"precision": precision, "recall": recall, "support": int(confusion[idx, :].sum())}

    return {
        "accuracy": float((pred == y).mean()),
        "confusion_matrix": confusion.tolist(),
        "per_class": per_class,
    }


def transition_detection_metrics(probs: np.ndarray, y: np.ndarray, thresholds: list[float]) -> dict:
    change_prob = probs[:, 1]
    rows = {}
    for threshold in thresholds:
        pred = change_prob >= threshold
        actual = y == 1
        tp = int(np.sum(pred & actual))
        fp = int(np.sum(pred & ~actual))
        fn = int(np.sum(~pred & actual))
        tn = int(np.sum(~pred & ~actual))
        precision = tp / (tp + fp) if (tp + fp) else 0.0
        recall = tp / (tp + fn) if (tp + fn) else 0.0
        false_alarm_rate = fp / (fp + tn) if (fp + tn) else 0.0
        rows[f"{threshold:.2f}"] = {
            "precision": precision,
            "recall": recall,
            "false_alarm_rate": false_alarm_rate,
            "tp": tp,
            "fp": fp,
            "fn": fn,
            "tn": tn,
        }
    return rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, default=Path("ml_model"))
    parser.add_argument("--horizon", type=int, default=30)
    parser.add_argument("--threshold-atr", type=float, default=1.50)
    args = parser.parse_args()

    args.out_dir.mkdir(parents=True, exist_ok=True)
    raw = load_ninja_export(args.data)
    enriched = add_features_and_labels(raw, args.horizon, args.threshold_atr)
    dataset = enriched.dropna(subset=FEATURE_COLUMNS + ["target"]).reset_index(drop=True)

    x = dataset[FEATURE_COLUMNS].to_numpy(dtype=float)
    y = dataset["target"].to_numpy(dtype=int)
    split_idx = int(len(dataset) * 0.80)
    x_train, x_test = x[:split_idx], x[split_idx:]
    y_train, y_test = y[:split_idx], y[split_idx:]
    x_train_std, x_test_std, mean, std = standardize_train_test(x_train, x_test)

    counts = np.bincount(y_train, minlength=len(CLASS_NAMES)).astype(float)
    class_weights = counts.sum() / (len(CLASS_NAMES) * np.maximum(counts, 1))
    weights, bias = train_multinomial_logistic(x_train_std, y_train, class_weights)

    train_metrics = evaluate(x_train_std, y_train, weights, bias)
    test_metrics = evaluate(x_test_std, y_test, weights, bias)
    train_probs = softmax(x_train_std @ weights + bias)
    test_probs = softmax(x_test_std @ weights + bias)
    latest_features = ((dataset[FEATURE_COLUMNS].iloc[[-1]].to_numpy(dtype=float) - mean) / std)
    latest_probs = softmax(latest_features @ weights + bias)[0]

    model_path = args.out_dir / "es_regime_model.npz"
    np.savez(
        model_path,
        weights=weights,
        bias=bias,
        mean=mean,
        std=std,
        feature_columns=np.array(FEATURE_COLUMNS),
        class_names=np.array(CLASS_NAMES),
        horizon=np.array([args.horizon]),
        threshold_atr=np.array([args.threshold_atr]),
    )
    json_model = {
        "class_names": CLASS_NAMES,
        "feature_columns": FEATURE_COLUMNS,
        "weights": weights.tolist(),
        "bias": bias.tolist(),
        "mean": mean.tolist(),
        "std": std.tolist(),
        "horizon": args.horizon,
        "threshold_atr": args.threshold_atr,
        "notes": "Transition model. current_regime is the present market state; prob_change_soon estimates whether that state changes inside the configured horizon.",
    }
    (args.out_dir / "es_regime_model.json").write_text(json.dumps(json_model, indent=2), encoding="utf-8")

    metrics = {
        "source_file": str(args.data),
        "rows_raw": int(len(raw)),
        "rows_model": int(len(dataset)),
        "start": str(raw["timestamp"].min()),
        "end": str(raw["timestamp"].max()),
        "horizon_minutes": args.horizon,
        "threshold_atr": args.threshold_atr,
        "target_definition": f"1 when current_regime changes within the next {args.horizon} bars; 0 otherwise.",
        "class_distribution_model_rows": dataset["target_name"].value_counts().to_dict(),
        "train": train_metrics,
        "test": test_metrics,
        "transition_threshold_metrics_train": transition_detection_metrics(train_probs, y_train, [0.40, 0.50, 0.60, 0.70]),
        "transition_threshold_metrics_test": transition_detection_metrics(test_probs, y_test, [0.40, 0.50, 0.60, 0.70]),
        "latest_bar": {
            "timestamp": str(dataset["timestamp"].iloc[-1]),
            "close": float(dataset["close"].iloc[-1]),
            "current_regime": str(dataset["current_regime"].iloc[-1]),
            "prediction": CLASS_NAMES[int(latest_probs.argmax())],
            "prob_change_soon": float(latest_probs[1]),
            "minutes_to_regime_change": None
            if pd.isna(dataset["minutes_to_regime_change"].iloc[-1])
            else int(dataset["minutes_to_regime_change"].iloc[-1]),
            "next_regime": None if pd.isna(dataset["next_regime"].iloc[-1]) else str(dataset["next_regime"].iloc[-1]),
            "probabilities": {name: float(latest_probs[i]) for i, name in enumerate(CLASS_NAMES)},
        },
    }
    (args.out_dir / "es_regime_metrics.json").write_text(json.dumps(metrics, indent=2), encoding="utf-8")
    dataset.to_csv(args.out_dir / "ES_03-25_enriched_regime_dataset.csv", index=False)
    print(json.dumps(metrics, indent=2))


if __name__ == "__main__":
    main()
