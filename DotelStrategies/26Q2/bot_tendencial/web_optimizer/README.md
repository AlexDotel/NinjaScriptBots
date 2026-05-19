# EMA Optimizer Web

Interfaz web local para cargar un historico de NinjaTrader o CSV y buscar las mejores combinaciones de EMA rapida/lenta, SL y T en ticks.

## Uso

Ejecuta:

```powershell
python .\ema_optimizer_web.py
```

Luego abre:

```text
http://127.0.0.1:8765
```

Tambien puedes usar `Lanzar_EMA_Optimizer.bat`.

## Formato aceptado

- Export NinjaTrader sin cabecera separado por `;`:
  `timestamp;open;high;low;close;volume`
- CSV con cabeceras compatibles:
  `timestamp,open,high,low,close,volume`

## Logica del backtest

- Entrada long: cruce alcista de EMA rapida sobre EMA lenta.
- Entrada short: cruce bajista.
- Entrada simulada en la apertura de la vela siguiente.
- Salida por SL, T o cruce contrario.
- Si SL y T tocan en la misma vela, se asume SL primero.
- SL y T aceptan rangos min/max/step.
- Opcionalmente limita T a un porcentaje minimo/maximo del SL para reducir combinaciones.
- Opcionalmente limita las EMAs para probar solo combinaciones donde la rapida sea menor que la lenta.
- Se puede exigir un minimo de trades para validar un backtest.
- Se puede exigir un minimo de ticks promedio por trade.
- Calcula resultados en cash usando dinero por tick y comision all-in por trade.
- Muestra la curva de equity del mejor resultado.
- Permite seleccionar cualquiera de los top 10 y dibujar su equity curve.
- Permite seleccionar cualquiera de los top 10 y ver ticks netos por hora de salida.
- Muestra barra de progreso, tiempo transcurrido y tiempo restante estimado.
- Permite pausar, continuar y detener la optimizacion.
- Ranking por Retorno / Max DDW, luego net ticks, profit factor, numero de trades y drawdown.
