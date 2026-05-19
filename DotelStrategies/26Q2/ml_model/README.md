# ES Regime Model

Modelo local para evaluar el estado de mercado de velas de 1 minuto del ES:

- `range`
- `trend`

El dataset enriquecido separa dos salidas:

- `current_regime`: lectura actual de rango/tendencia usando ADX, pendiente EMA, separacion EMA, R2 de tendencia y suavizado de 5 velas.
- `target_name`: `stable` o `change_soon`, segun si el estado actual cambia dentro de la ventana futura.

El entrenamiento usa el export de NinjaTrader:

`C:\Users\joalr\Documents\# Data Exportada de Ninja\ES 03-25.Last.txt`

## Definicion del objetivo

Para cada vela se calcula si el regimen actual cambia dentro de los proximos 30 minutos:

`target = change_soon` cuando `current_regime[t + 1 : t + 30]` contiene un estado distinto al actual.

La etiqueta se asigna asi:

- `change_soon`: el estado cambia dentro de la ventana.
- `stable`: el estado no cambia dentro de la ventana.

## Archivos

- `train_es_regime_model.py`: genera indicadores, entrena y guarda el modelo.
- `predict_es_regime.py`: carga el modelo y predice las ultimas velas de un export.
- `es_regime_gui.py`: interfaz grafica para cargar fuentes, ejecutar el modelo, ver graficos y reentrenar.
- `run_gui.ps1`: lanzador de PowerShell para abrir la interfaz.
- `es_regime_model.npz`: modelo entrenado.
- `es_regime_model.json`: mismo modelo en formato legible para integracion externa.
- `es_regime_metrics.json`: metricas y ultima prediccion del entrenamiento.
- `ES_03-25_enriched_regime_dataset.csv`: dataset enriquecido con features y etiqueta.

## Uso

Interfaz grafica:

```powershell
powershell -ExecutionPolicy Bypass -File .\ml_model\run_gui.ps1
```

La interfaz permite:

- Elegir otro export de NinjaTrader (`.txt` o `.csv` con el formato exportado).
- Elegir otro modelo `.npz`.
- Cambiar horizonte y umbral ATR antes de reentrenar.
- Activar o desactivar indicadores visibles.
- Ver grafico de precio con fondo coloreado por regimen actual.
- Ver riesgo de cambio de estado y ultimas velas en tabla.
- Ejecutar el entrenamiento en un CMD interno y guardar un resumen JSON.

```powershell
& "C:\Users\joalr\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" .\ml_model\train_es_regime_model.py --data "C:\Users\joalr\Documents\# Data Exportada de Ninja\ES 03-25.Last.txt" --out-dir .\ml_model
```

```powershell
& "C:\Users\joalr\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" .\ml_model\predict_es_regime.py --data "C:\Users\joalr\Documents\# Data Exportada de Ninja\ES 03-25.Last.txt"
```

Este modelo no predice direccion. Estima si el estado actual (`range` o `trend`) esta cerca de cambiar. Es investigativo y, con este unico export de ES 03-25, no debe usarse como senal unica para operar sin validacion adicional, walk-forward testing y control de riesgo.
