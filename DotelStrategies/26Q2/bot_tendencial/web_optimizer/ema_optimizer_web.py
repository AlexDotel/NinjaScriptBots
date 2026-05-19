from __future__ import annotations

import cgi
import html
import io
import json
import math
import tempfile
import threading
import time
import uuid
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, urlparse

import numpy as np
import pandas as pd


HOST = "127.0.0.1"
PORT = 8765
JOBS: dict[str, "OptimizationJob"] = {}
JOBS_LOCK = threading.Lock()


PAGE = """<!doctype html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>EMA Optimizer</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f6f7f9;
      --panel: #ffffff;
      --ink: #18202a;
      --muted: #687385;
      --line: #d9dee7;
      --accent: #0f766e;
      --accent-dark: #115e59;
      --bad: #b42318;
      --good: #067647;
      --warn: #b54708;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", Arial, sans-serif;
      color: var(--ink);
      background: var(--bg);
    }
    header {
      padding: 18px 24px 12px;
      border-bottom: 1px solid var(--line);
      background: var(--panel);
    }
    h1 { margin: 0; font-size: 22px; font-weight: 650; letter-spacing: 0; }
    main {
      display: grid;
      grid-template-columns: 360px 1fr;
      gap: 18px;
      padding: 18px 24px 24px;
    }
    form, section {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 8px;
    }
    form { padding: 16px; align-self: start; }
    section { overflow: hidden; }
    fieldset {
      border: 0;
      padding: 0;
      margin: 0 0 18px;
    }
    legend {
      font-size: 13px;
      font-weight: 700;
      color: var(--muted);
      margin-bottom: 10px;
      text-transform: uppercase;
    }
    label {
      display: block;
      font-size: 13px;
      font-weight: 600;
      margin: 10px 0 5px;
    }
    input {
      width: 100%;
      height: 34px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 6px 9px;
      font: inherit;
      background: #fff;
    }
    input[type="file"] { height: auto; padding: 8px; }
    input[type="checkbox"] { width: 16px; height: 16px; }
    .row { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; }
    .two { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
    .checkline {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 12px 0 4px;
      font-size: 13px;
      font-weight: 600;
    }
    button {
      width: 100%;
      height: 38px;
      border: 0;
      border-radius: 6px;
      background: var(--accent);
      color: #fff;
      font-weight: 700;
      cursor: pointer;
    }
    button:hover { background: var(--accent-dark); }
    button:disabled { opacity: .65; cursor: wait; }
    .status {
      padding: 12px 14px;
      border-bottom: 1px solid var(--line);
      color: var(--muted);
      min-height: 44px;
      font-size: 14px;
    }
    .status.error { color: var(--bad); }
    .progress-panel {
      padding: 12px 14px;
      border-bottom: 1px solid var(--line);
      background: #ffffff;
    }
    .progress-track {
      height: 12px;
      border-radius: 999px;
      background: #e6eaf0;
      overflow: hidden;
    }
    .progress-fill {
      height: 100%;
      width: 0%;
      background: var(--accent);
      transition: width .2s ease;
    }
    .progress-meta {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 8px;
      margin-top: 10px;
      color: var(--muted);
      font-size: 13px;
    }
    .control-row {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
      margin-top: 10px;
    }
    .control-row button {
      height: 34px;
      background: #344054;
    }
    .control-row button.stop { background: var(--bad); }
    .config-row {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
      margin-top: 10px;
    }
    .config-row button {
      height: 34px;
      background: #344054;
    }
    .mini-button {
      width: auto;
      min-width: 76px;
      height: 28px;
      padding: 0 10px;
      background: #344054;
      font-size: 12px;
    }
    .toast-wrap {
      position: fixed;
      right: 18px;
      bottom: 18px;
      display: grid;
      gap: 10px;
      z-index: 10;
      max-width: min(420px, calc(100vw - 36px));
    }
    .toast {
      padding: 12px 14px;
      border-radius: 8px;
      border: 1px solid var(--line);
      background: #ffffff;
      color: var(--ink);
      box-shadow: 0 12px 28px rgba(16, 24, 40, .16);
      font-size: 14px;
      line-height: 1.35;
    }
    .toast.error { border-color: #f4b7b2; color: var(--bad); }
    .toast.warn { border-color: #f7c58a; color: var(--warn); }
    .toast.ok { border-color: #9ad8bd; color: var(--good); }
    .summary {
      display: grid;
      grid-template-columns: repeat(6, minmax(120px, 1fr));
      gap: 1px;
      background: var(--line);
      border-bottom: 1px solid var(--line);
    }
    .metric {
      background: #fbfcfd;
      padding: 12px;
    }
    .metric span {
      display: block;
      font-size: 12px;
      color: var(--muted);
      margin-bottom: 4px;
    }
    .metric strong { font-size: 18px; }
    .table-wrap { overflow: auto; max-height: calc(100vh - 244px); }
    .chart-panel {
      padding: 14px;
      border-bottom: 1px solid var(--line);
      background: #ffffff;
    }
    #equity-chart {
      display: block;
      width: 100%;
      height: 260px;
      border: 1px solid var(--line);
      border-radius: 6px;
      background: #fbfcfd;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      min-width: 1100px;
    }
    th, td {
      padding: 9px 10px;
      border-bottom: 1px solid var(--line);
      text-align: right;
      font-size: 13px;
      white-space: nowrap;
    }
    th {
      position: sticky;
      top: 0;
      background: #eef2f6;
      z-index: 1;
      color: #344054;
      font-weight: 700;
    }
    th:first-child, td:first-child { text-align: left; }
    .good { color: var(--good); font-weight: 700; }
    .bad { color: var(--bad); font-weight: 700; }
    .warn { color: var(--warn); font-weight: 700; }
    @media (max-width: 980px) {
      main { grid-template-columns: 1fr; padding: 12px; }
      .summary { grid-template-columns: repeat(2, 1fr); }
    }
  </style>
</head>
<body>
  <header><h1>Optimizador EMA Crossover</h1></header>
  <main>
    <form id="optimizer-form">
      <fieldset>
        <legend>Historico</legend>
        <label for="file">Archivo NinjaTrader / CSV</label>
        <input id="file" name="file" type="file" accept=".txt,.csv" required>
        <div class="row">
          <div>
            <label for="tick_size">Tick size</label>
            <input id="tick_size" name="tick_size" type="number" value="0.25" min="0.0001" step="0.0001">
          </div>
          <div>
            <label for="quantity">Cantidad</label>
            <input id="quantity" name="quantity" type="number" value="1" min="1" step="1">
          </div>
          <div>
            <label for="tick_value">Dinero por tick</label>
            <input id="tick_value" name="tick_value" type="number" value="12.50" min="0" step="0.01">
          </div>
        </div>
        <div>
          <label for="commission_per_trade">Comision all-in</label>
          <input id="commission_per_trade" name="commission_per_trade" type="number" value="0" min="0" step="0.01">
        </div>
      </fieldset>
      <fieldset>
        <legend>EMAs</legend>
        <label>EMA rapida</label>
        <div class="row">
          <input name="fast_min" type="number" value="5" min="1" step="1">
          <input name="fast_max" type="number" value="200" min="1" step="1">
          <input name="fast_step" type="number" value="5" min="1" step="1">
        </div>
        <label>EMA lenta</label>
        <div class="row">
          <input name="slow_min" type="number" value="5" min="2" step="1">
          <input name="slow_max" type="number" value="200" min="2" step="1">
          <input name="slow_step" type="number" value="5" min="1" step="1">
        </div>
        <label class="checkline"><input name="limit_fast_below_slow" type="checkbox" checked> Limitar rapida menor que lenta</label>
      </fieldset>
      <fieldset>
        <legend>Riesgo</legend>
        <label>SL ticks</label>
        <div class="row">
          <input name="stop_min" type="number" value="16" min="1" step="1">
          <input name="stop_max" type="number" value="30" min="1" step="1">
          <input name="stop_step" type="number" value="8" min="1" step="1">
        </div>
        <label>T ticks</label>
        <div class="row">
          <input name="target_min" type="number" value="16" min="1" step="1">
          <input name="target_max" type="number" value="128" min="1" step="1">
          <input name="target_step" type="number" value="8" min="1" step="1">
        </div>
        <label class="checkline"><input name="limit_target_pct" type="checkbox" checked> Limitar T como % del SL</label>
        <div class="two">
          <div>
            <label for="target_pct_min">T minimo % SL</label>
            <input id="target_pct_min" name="target_pct_min" type="number" value="50" min="1" step="1">
          </div>
          <div>
            <label for="target_pct_max">T maximo % SL</label>
            <input id="target_pct_max" name="target_pct_max" type="number" value="300" min="1" step="1">
          </div>
        </div>
        <div>
          <label for="min_trades">Minimo trades validos</label>
          <input id="min_trades" name="min_trades" type="number" value="100" min="0" step="1">
        </div>
        <div>
          <label for="min_avg_ticks">Minimo avg ticks</label>
          <input id="min_avg_ticks" name="min_avg_ticks" type="number" value="0" step="0.1">
        </div>
      </fieldset>
      <fieldset>
        <legend>Horario</legend>
        <div class="two">
          <div>
            <label for="start_time">Inicio HH:MM</label>
            <input id="start_time" name="start_time" type="time" value="00:00">
          </div>
          <div>
            <label for="end_time">Fin HH:MM</label>
            <input id="end_time" name="end_time" type="time" value="10:00">
          </div>
        </div>
      </fieldset>
      <button id="run-button" type="submit">Ejecutar optimizacion</button>
      <div class="config-row">
        <button id="save-config-button" type="button">Guardar config</button>
        <button id="load-config-button" type="button">Cargar config</button>
      </div>
      <input id="load-config-input" type="file" accept=".json" hidden>
    </form>
    <section>
      <div id="status" class="status">Carga un historico y ejecuta la busqueda.</div>
      <div class="progress-panel">
        <div class="progress-track"><div id="progress-fill" class="progress-fill"></div></div>
        <div class="progress-meta">
          <span id="progress-percent">0%</span>
          <span id="progress-count">0 / 0</span>
          <span id="elapsed-time">Transcurrido 00:00</span>
          <span id="remaining-time">Restante --:--</span>
        </div>
        <div class="control-row">
          <button id="pause-button" type="button" disabled>Pausar</button>
          <button id="stop-button" class="stop" type="button" disabled>Detener</button>
        </div>
      </div>
      <div id="summary" class="summary"></div>
      <div class="chart-panel">
        <canvas id="equity-chart" width="1200" height="320"></canvas>
      </div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>#</th><th>Ver</th><th>EMA rapida</th><th>EMA lenta</th><th>SL</th><th>T</th><th>Ret/DDW</th><th>Net ticks</th><th>Net cash</th><th>Profit factor</th>
              <th>Win %</th><th>Trades</th><th>Avg ticks</th><th>Max DD ticks</th>
              <th>Max DD cash</th><th>Comisiones</th><th>Longs</th><th>Shorts</th>
            </tr>
          </thead>
          <tbody id="rows"></tbody>
        </table>
      </div>
    </section>
  </main>
  <div id="toast-wrap" class="toast-wrap"></div>
  <script>
    const form = document.getElementById("optimizer-form");
    const button = document.getElementById("run-button");
    const statusBox = document.getElementById("status");
    const rows = document.getElementById("rows");
    const summary = document.getElementById("summary");
    const chart = document.getElementById("equity-chart");
    const ctx = chart.getContext("2d");
    const progressFill = document.getElementById("progress-fill");
    const progressPercent = document.getElementById("progress-percent");
    const progressCount = document.getElementById("progress-count");
    const elapsedTime = document.getElementById("elapsed-time");
    const remainingTime = document.getElementById("remaining-time");
    const pauseButton = document.getElementById("pause-button");
    const stopButton = document.getElementById("stop-button");
    const saveConfigButton = document.getElementById("save-config-button");
    const loadConfigButton = document.getElementById("load-config-button");
    const loadConfigInput = document.getElementById("load-config-input");
    const toastWrap = document.getElementById("toast-wrap");
    let activeJobId = null;
    let pollingTimer = null;
    let paused = false;
    let latestResults = [];

    function fmt(value, digits = 2) {
      if (value === null || value === undefined || Number.isNaN(value)) return "-";
      return Number(value).toFixed(digits);
    }
    function money(value) {
      if (value === null || value === undefined || Number.isNaN(value)) return "-";
      const sign = value < 0 ? "-" : "";
      return `${sign}$${Math.abs(Number(value)).toFixed(2)}`;
    }
    function cls(value) {
      if (value > 0) return "good";
      if (value < 0) return "bad";
      return "warn";
    }
    function metric(label, value) {
      return `<div class="metric"><span>${label}</span><strong>${value}</strong></div>`;
    }
    function showToast(message, type = "warn") {
      const toast = document.createElement("div");
      toast.className = `toast ${type}`;
      toast.textContent = message;
      toastWrap.appendChild(toast);
      setTimeout(() => {
        toast.style.opacity = "0";
        toast.style.transition = "opacity .25s ease";
        setTimeout(() => toast.remove(), 300);
      }, 6500);
    }
    function getConfig() {
      const config = {};
      for (const element of form.elements) {
        if (!element.name || element.type === "file") continue;
        if (element.type === "checkbox") config[element.name] = element.checked;
        else config[element.name] = element.value;
      }
      return config;
    }
    function applyConfig(config) {
      for (const [name, value] of Object.entries(config || {})) {
        const field = form.elements[name];
        if (!field) continue;
        if (field.type === "checkbox") field.checked = Boolean(value);
        else field.value = value;
      }
    }
    function fmtTime(seconds) {
      if (seconds === null || seconds === undefined || !Number.isFinite(seconds)) return "--:--";
      seconds = Math.max(0, Math.round(seconds));
      const h = Math.floor(seconds / 3600);
      const m = Math.floor((seconds % 3600) / 60);
      const s = seconds % 60;
      if (h > 0) return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
      return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
    }
    function resetProgress() {
      progressFill.style.width = "0%";
      progressPercent.textContent = "0%";
      progressCount.textContent = "0 / 0";
      elapsedTime.textContent = "Transcurrido 00:00";
      remainingTime.textContent = "Restante --:--";
      pauseButton.textContent = "Pausar";
      pauseButton.disabled = true;
      stopButton.disabled = true;
      paused = false;
    }
    function renderResults(payload) {
      latestResults = payload.results || [];
      if (!latestResults.length) {
        summary.innerHTML = "";
        rows.innerHTML = "";
        drawEquity([]);
        showToast("La optimizacion termino, pero ningun backtest cumple con los criterios seleccionados.", "warn");
        return;
      }
      const best = payload.results[0];
      if (best) {
        summary.innerHTML =
          metric("Mejor EMA", `${best.fast}/${best.slow}`) +
          metric("SL / T", `${best.stop_ticks}/${best.target_ticks}`) +
          metric("Ret/DDW", fmt(best.return_dd_ratio, 2)) +
          metric("Net cash", `<span class="${cls(best.net_cash)}">${money(best.net_cash)}</span>`) +
          metric("Net ticks", `<span class="${cls(best.net_ticks)}">${fmt(best.net_ticks, 1)}</span>`) +
          metric("Trades", best.trades);
        drawEquity(best.equity_curve);
      }
      rows.innerHTML = payload.results.map((r, idx) => `
        <tr>
          <td>${idx + 1}</td>
          <td>
            <button class="mini-button" type="button" data-equity-index="${idx}">Equity</button>
            <button class="mini-button" type="button" data-hour-index="${idx}">Horas</button>
          </td>
          <td>${r.fast}</td>
          <td>${r.slow}</td>
          <td>${r.stop_ticks}</td>
          <td>${r.target_ticks}</td>
          <td>${fmt(r.return_dd_ratio, 2)}</td>
          <td class="${cls(r.net_ticks)}">${fmt(r.net_ticks, 1)}</td>
          <td class="${cls(r.net_cash)}">${money(r.net_cash)}</td>
          <td>${fmt(r.profit_factor, 2)}</td>
          <td>${fmt(r.win_rate, 1)}%</td>
          <td>${r.trades}</td>
          <td>${fmt(r.avg_ticks, 2)}</td>
          <td class="bad">${fmt(r.max_drawdown_ticks, 1)}</td>
          <td class="bad">${money(r.max_drawdown_cash)}</td>
          <td>${money(r.total_commission)}</td>
          <td>${r.long_trades}</td>
          <td>${r.short_trades}</td>
        </tr>
      `).join("");
    }
    async function sendControl(action) {
      if (!activeJobId) return;
      await fetch("/control", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ job_id: activeJobId, action })
      });
    }
    async function pollProgress() {
      if (!activeJobId) return;
      try {
        const response = await fetch(`/progress?job_id=${encodeURIComponent(activeJobId)}`);
        const payload = await response.json();
        if (!response.ok) throw new Error(payload.error || "No se pudo leer el progreso");
        const pct = payload.total_tests > 0 ? Math.min(100, (payload.completed_tests / payload.total_tests) * 100) : 0;
        progressFill.style.width = `${pct.toFixed(1)}%`;
        progressPercent.textContent = `${pct.toFixed(1)}%`;
        progressCount.textContent = `${payload.completed_tests} / ${payload.total_tests}`;
        elapsedTime.textContent = `Transcurrido ${fmtTime(payload.elapsed_seconds)}`;
        remainingTime.textContent = `Restante ${fmtTime(payload.remaining_seconds)}`;
        statusBox.className = payload.status === "failed" ? "status error" : "status";
        statusBox.textContent = payload.message;
        if (payload.status === "failed") {
          showToast(`El backtest no se realizo: ${payload.message || payload.error || "error desconocido"}`, "error");
        }

        if (payload.status === "completed" || payload.status === "stopped" || payload.status === "failed") {
          clearInterval(pollingTimer);
          pollingTimer = null;
          button.disabled = false;
          pauseButton.disabled = true;
          stopButton.disabled = true;
          if (payload.status === "completed" || payload.status === "stopped") {
            renderResults(payload);
          }
          activeJobId = null;
        }
      } catch (error) {
        clearInterval(pollingTimer);
        pollingTimer = null;
        button.disabled = false;
        pauseButton.disabled = true;
        stopButton.disabled = true;
        statusBox.className = "status error";
        statusBox.textContent = error.message;
        showToast(`El backtest no se realizo: ${error.message}`, "error");
      }
    }
    function drawEquity(curve) {
      const rect = chart.getBoundingClientRect();
      const scale = window.devicePixelRatio || 1;
      chart.width = Math.max(600, Math.floor(rect.width * scale));
      chart.height = Math.floor(260 * scale);
      ctx.setTransform(scale, 0, 0, scale, 0, 0);
      const w = chart.width / scale;
      const h = chart.height / scale;
      ctx.clearRect(0, 0, w, h);
      ctx.fillStyle = "#fbfcfd";
      ctx.fillRect(0, 0, w, h);
      ctx.strokeStyle = "#d9dee7";
      ctx.lineWidth = 1;
      for (let i = 0; i <= 4; i++) {
        const y = 18 + ((h - 38) * i / 4);
        ctx.beginPath();
        ctx.moveTo(14, y);
        ctx.lineTo(w - 12, y);
        ctx.stroke();
      }
      if (!curve || curve.length < 2) {
        ctx.fillStyle = "#687385";
        ctx.font = "13px Segoe UI, Arial";
        ctx.fillText("Sin curva de equity disponible", 18, 34);
        return;
      }
      const min = Math.min(...curve);
      const max = Math.max(...curve);
      const span = Math.max(1, max - min);
      ctx.strokeStyle = "#0f766e";
      ctx.lineWidth = 2;
      ctx.beginPath();
      curve.forEach((value, idx) => {
        const x = 16 + ((w - 32) * idx / (curve.length - 1));
        const y = h - 20 - ((h - 42) * (value - min) / span);
        if (idx === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      });
      ctx.stroke();
      ctx.fillStyle = "#344054";
      ctx.font = "12px Segoe UI, Arial";
      ctx.fillText(`Equity mejor backtest | min ${fmt(min, 1)} ticks | max ${fmt(max, 1)} ticks`, 18, 18);
    }
    function drawHourlyBars(hourlyTicks) {
      const rect = chart.getBoundingClientRect();
      const scale = window.devicePixelRatio || 1;
      chart.width = Math.max(600, Math.floor(rect.width * scale));
      chart.height = Math.floor(260 * scale);
      ctx.setTransform(scale, 0, 0, scale, 0, 0);
      const w = chart.width / scale;
      const h = chart.height / scale;
      ctx.clearRect(0, 0, w, h);
      ctx.fillStyle = "#fbfcfd";
      ctx.fillRect(0, 0, w, h);

      const entries = Object.entries(hourlyTicks || {}).map(([hour, ticks]) => [Number(hour), Number(ticks)]);
      if (!entries.length) {
        ctx.fillStyle = "#687385";
        ctx.font = "13px Segoe UI, Arial";
        ctx.fillText("Sin ticks por hora disponibles", 18, 34);
        return;
      }
      entries.sort((a, b) => a[0] - b[0]);
      const maxAbs = Math.max(1, ...entries.map(([, ticks]) => Math.abs(ticks)));
      const left = 34;
      const right = 14;
      const top = 24;
      const bottom = 34;
      const plotW = w - left - right;
      const plotH = h - top - bottom;
      const zeroY = top + plotH / 2;
      const barGap = 4;
      const barW = Math.max(8, (plotW / entries.length) - barGap);

      ctx.strokeStyle = "#d9dee7";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(left, zeroY);
      ctx.lineTo(w - right, zeroY);
      ctx.stroke();

      entries.forEach(([hour, ticks], idx) => {
        const x = left + idx * (plotW / entries.length) + barGap / 2;
        const barH = (Math.abs(ticks) / maxAbs) * (plotH / 2 - 8);
        const y = ticks >= 0 ? zeroY - barH : zeroY;
        ctx.fillStyle = ticks >= 0 ? "#067647" : "#b42318";
        ctx.fillRect(x, y, barW, barH);
        ctx.fillStyle = "#344054";
        ctx.font = "11px Segoe UI, Arial";
        ctx.textAlign = "center";
        ctx.fillText(String(hour).padStart(2, "0"), x + barW / 2, h - 12);
      });

      ctx.textAlign = "left";
      ctx.fillStyle = "#344054";
      ctx.font = "12px Segoe UI, Arial";
      ctx.fillText("Ticks netos por hora de salida", 18, 18);
    }
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      rows.innerHTML = "";
      summary.innerHTML = "";
      drawEquity([]);
      resetProgress();
      statusBox.className = "status";
      statusBox.textContent = "Preparando optimizacion...";
      button.disabled = true;
      try {
        const response = await fetch("/start", { method: "POST", body: new FormData(form) });
        const payload = await response.json();
        if (!response.ok) throw new Error(payload.error || "Error desconocido");
        activeJobId = payload.job_id;
        pauseButton.disabled = false;
        stopButton.disabled = false;
        statusBox.textContent = "Optimizacion en curso...";
        pollingTimer = setInterval(pollProgress, 1000);
        await pollProgress();
      } catch (error) {
        statusBox.className = "status error";
        statusBox.textContent = error.message;
        showToast(`El backtest no se realizo: ${error.message}`, "error");
        button.disabled = false;
      } finally {
      }
    });
    saveConfigButton.addEventListener("click", () => {
      const blob = new Blob([JSON.stringify(getConfig(), null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = "ema_optimizer_config.json";
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      showToast("Configuracion guardada.", "ok");
    });
    loadConfigButton.addEventListener("click", () => loadConfigInput.click());
    loadConfigInput.addEventListener("change", async () => {
      const file = loadConfigInput.files && loadConfigInput.files[0];
      if (!file) return;
      try {
        const config = JSON.parse(await file.text());
        applyConfig(config);
        showToast("Configuracion cargada.", "ok");
      } catch (error) {
        showToast(`No se pudo cargar la configuracion: ${error.message}`, "error");
      } finally {
        loadConfigInput.value = "";
      }
    });
    pauseButton.addEventListener("click", async () => {
      if (!activeJobId) return;
      paused = !paused;
      pauseButton.textContent = paused ? "Continuar" : "Pausar";
      await sendControl(paused ? "pause" : "resume");
      await pollProgress();
    });
    stopButton.addEventListener("click", async () => {
      if (!activeJobId) return;
      await sendControl("stop");
      stopButton.disabled = true;
      pauseButton.disabled = true;
      await pollProgress();
    });
    rows.addEventListener("click", (event) => {
      const equityButton = event.target.closest("[data-equity-index]");
      const hourButton = event.target.closest("[data-hour-index]");
      if (!equityButton && !hourButton) return;
      const idx = Number((equityButton || hourButton).getAttribute(equityButton ? "data-equity-index" : "data-hour-index"));
      const row = latestResults[idx];
      if (!row) return;
      summary.innerHTML =
        metric("EMA", `${row.fast}/${row.slow}`) +
        metric("SL / T", `${row.stop_ticks}/${row.target_ticks}`) +
        metric("Ret/DDW", fmt(row.return_dd_ratio, 2)) +
        metric("Net cash", `<span class="${cls(row.net_cash)}">${money(row.net_cash)}</span>`) +
        metric("Net ticks", `<span class="${cls(row.net_ticks)}">${fmt(row.net_ticks, 1)}</span>`) +
        metric("Trades", row.trades);
      if (hourButton) drawHourlyBars(row.hourly_ticks);
      else drawEquity(row.equity_curve);
    });
    resetProgress();
  </script>
</body>
</html>
"""


@dataclass(frozen=True)
class OptimizationRequest:
    tick_size: float
    quantity: int
    tick_value: float
    commission_per_trade: float
    fast_min: int
    fast_max: int
    fast_step: int
    slow_min: int
    slow_max: int
    slow_step: int
    stop_min: int
    stop_max: int
    stop_step: int
    target_min: int
    target_max: int
    target_step: int
    limit_target_pct: bool
    target_pct_min: float
    target_pct_max: float
    min_trades: int
    min_avg_ticks: float
    limit_fast_below_slow: bool
    start_minutes: int
    end_minutes: int


def parse_number(value: Any, default: float) -> float:
    if value is None:
        return default
    text = value.decode("utf-8", errors="ignore") if isinstance(value, bytes) else str(value)
    text = text.strip()
    return default if not text else float(text)


def parse_int(value: Any, default: int) -> int:
    return int(round(parse_number(value, default)))


def parse_bool(value: Any) -> bool:
    if value is None:
        return False
    text = value.decode("utf-8", errors="ignore") if isinstance(value, bytes) else str(value)
    return text.lower() in ("1", "true", "on", "yes", "si")


def parse_time_to_minutes(value: Any, default: str) -> int:
    text = value.decode("utf-8", errors="ignore") if isinstance(value, bytes) else str(value or default)
    if not text:
        text = default
    hour, minute = text.split(":")[:2]
    return int(hour) * 60 + int(minute)


def parse_request(form: cgi.FieldStorage) -> OptimizationRequest:
    req = OptimizationRequest(
        tick_size=parse_number(form.getvalue("tick_size"), 0.25),
        quantity=parse_int(form.getvalue("quantity"), 1),
        tick_value=parse_number(form.getvalue("tick_value"), 12.50),
        commission_per_trade=parse_number(form.getvalue("commission_per_trade"), 0.0),
        fast_min=parse_int(form.getvalue("fast_min"), 5),
        fast_max=parse_int(form.getvalue("fast_max"), 40),
        fast_step=parse_int(form.getvalue("fast_step"), 1),
        slow_min=parse_int(form.getvalue("slow_min"), 20),
        slow_max=parse_int(form.getvalue("slow_max"), 120),
        slow_step=parse_int(form.getvalue("slow_step"), 2),
        stop_min=parse_int(form.getvalue("stop_min"), 16),
        stop_max=parse_int(form.getvalue("stop_max"), 40),
        stop_step=parse_int(form.getvalue("stop_step"), 4),
        target_min=parse_int(form.getvalue("target_min"), 24),
        target_max=parse_int(form.getvalue("target_max"), 80),
        target_step=parse_int(form.getvalue("target_step"), 4),
        limit_target_pct=parse_bool(form.getvalue("limit_target_pct")),
        target_pct_min=parse_number(form.getvalue("target_pct_min"), 100.0),
        target_pct_max=parse_number(form.getvalue("target_pct_max"), 300.0),
        min_trades=parse_int(form.getvalue("min_trades"), 30),
        min_avg_ticks=parse_number(form.getvalue("min_avg_ticks"), 0.0),
        limit_fast_below_slow=parse_bool(form.getvalue("limit_fast_below_slow")),
        start_minutes=parse_time_to_minutes(form.getvalue("start_time"), "09:30"),
        end_minutes=parse_time_to_minutes(form.getvalue("end_time"), "16:00"),
    )
    if req.tick_size <= 0:
        raise ValueError("Tick size debe ser mayor que 0.")
    if req.quantity <= 0:
        raise ValueError("Cantidad debe ser mayor que 0.")
    if req.tick_value < 0 or req.commission_per_trade < 0:
        raise ValueError("Dinero por tick y comision no pueden ser negativos.")
    if req.fast_step <= 0 or req.slow_step <= 0 or req.stop_step <= 0 or req.target_step <= 0:
        raise ValueError("Los steps deben ser mayores que 0.")
    if req.fast_min > req.fast_max or req.slow_min > req.slow_max:
        raise ValueError("El minimo de EMA no puede ser mayor que el maximo.")
    if req.stop_min > req.stop_max or req.target_min > req.target_max:
        raise ValueError("El minimo de SL/T no puede ser mayor que el maximo.")
    if req.stop_min <= 0 or req.target_min <= 0:
        raise ValueError("SL y T deben ser mayores que 0.")
    if req.limit_target_pct and (req.target_pct_min <= 0 or req.target_pct_max <= 0):
        raise ValueError("Los porcentajes de T/SL deben ser mayores que 0.")
    if req.limit_target_pct and req.target_pct_min > req.target_pct_max:
        raise ValueError("El porcentaje minimo T/SL no puede ser mayor que el maximo.")
    if req.min_trades < 0:
        raise ValueError("Minimo trades no puede ser negativo.")
    return req


def load_history(file_bytes: bytes, filename: str) -> pd.DataFrame:
    text = file_bytes.decode("utf-8-sig", errors="ignore")
    first_line = next((line for line in text.splitlines() if line.strip()), "")
    separator = ";" if first_line.count(";") >= first_line.count(",") else ","
    has_header = any(token.lower() in first_line.lower() for token in ("timestamp", "date", "time", "open", "close"))

    if has_header:
        df = pd.read_csv(io.StringIO(text), sep=separator)
        df.columns = [str(col).strip().lower() for col in df.columns]
        if "timestamp" not in df.columns:
            date_col = next((col for col in df.columns if col in ("date", "datetime", "time")), None)
            if date_col is None:
                raise ValueError("No encontre columna timestamp/date/time en el CSV.")
            df = df.rename(columns={date_col: "timestamp"})
    else:
        df = pd.read_csv(
            io.StringIO(text),
            sep=separator,
            header=None,
            names=["timestamp", "open", "high", "low", "close", "volume"],
        )

    rename_map = {}
    for col in df.columns:
        clean = str(col).strip().lower()
        if clean in ("o", "open"):
            rename_map[col] = "open"
        elif clean in ("h", "high"):
            rename_map[col] = "high"
        elif clean in ("l", "low"):
            rename_map[col] = "low"
        elif clean in ("c", "close", "last"):
            rename_map[col] = "close"
        elif clean in ("v", "volume", "vol"):
            rename_map[col] = "volume"
    df = df.rename(columns=rename_map)

    required = ["timestamp", "open", "high", "low", "close"]
    missing = [col for col in required if col not in df.columns]
    if missing:
        raise ValueError("Faltan columnas requeridas: " + ", ".join(missing))

    df = df[required + (["volume"] if "volume" in df.columns else [])].copy()
    raw_timestamp = df["timestamp"].copy()
    df["timestamp"] = pd.to_datetime(raw_timestamp, errors="coerce", format="%Y%m%d %H%M%S")
    if df["timestamp"].isna().all():
        df["timestamp"] = pd.to_datetime(raw_timestamp, errors="coerce")
    for col in ["open", "high", "low", "close"]:
        df[col] = pd.to_numeric(df[col], errors="coerce")
    df = df.dropna(subset=required).sort_values("timestamp").reset_index(drop=True)
    if len(df) < 100:
        raise ValueError("El historico tiene muy pocas velas validas.")
    return df


def inside_window(minutes: np.ndarray, start: int, end: int) -> np.ndarray:
    if start == end:
        return np.ones(len(minutes), dtype=bool)
    if start < end:
        return (minutes >= start) & (minutes <= end)
    return (minutes >= start) | (minutes <= end)


def simulate(
    open_px: np.ndarray,
    high: np.ndarray,
    low: np.ndarray,
    hours: np.ndarray,
    tradable: np.ndarray,
    cross_up: np.ndarray,
    cross_down: np.ndarray,
    start_index: int,
    fast: int,
    slow: int,
    stop_ticks: int,
    target_ticks: int,
    req: OptimizationRequest,
) -> dict[str, Any] | None:
    stop_points = stop_ticks * req.tick_size
    target_points = target_ticks * req.tick_size
    position = 0
    entry = 0.0
    trades: list[float] = []
    long_trades = 0
    short_trades = 0
    equity = 0.0
    peak = 0.0
    max_dd = 0.0
    equity_curve = [0.0]
    hourly_ticks = {hour: 0.0 for hour in range(24)}

    for i in range(start_index, len(open_px) - 1):
        if position == 1:
            stop = entry - stop_points
            target = entry + target_points
            exit_ticks = None
            if low[i] <= stop:
                exit_ticks = -stop_ticks
            elif high[i] >= target:
                exit_ticks = target_ticks
            elif cross_down[i - 1]:
                exit_ticks = (open_px[i + 1] - entry) / req.tick_size
            if exit_ticks is not None:
                trade_ticks = exit_ticks * req.quantity
                trades.append(trade_ticks)
                equity += trade_ticks
                equity_curve.append(float(equity))
                hourly_ticks[int(hours[i])] += trade_ticks
                peak = max(peak, equity)
                max_dd = max(max_dd, peak - equity)
                position = 0
        elif position == -1:
            stop = entry + stop_points
            target = entry - target_points
            exit_ticks = None
            if high[i] >= stop:
                exit_ticks = -stop_ticks
            elif low[i] <= target:
                exit_ticks = target_ticks
            elif cross_up[i - 1]:
                exit_ticks = (entry - open_px[i + 1]) / req.tick_size
            if exit_ticks is not None:
                trade_ticks = exit_ticks * req.quantity
                trades.append(trade_ticks)
                equity += trade_ticks
                equity_curve.append(float(equity))
                hourly_ticks[int(hours[i])] += trade_ticks
                peak = max(peak, equity)
                max_dd = max(max_dd, peak - equity)
                position = 0

        if position != 0 or not tradable[i]:
            continue

        if cross_up[i - 1]:
            position = 1
            entry = open_px[i + 1]
            long_trades += 1
        elif cross_down[i - 1]:
            position = -1
            entry = open_px[i + 1]
            short_trades += 1

    if not trades:
        return None

    arr = np.asarray(trades, dtype=float)
    if len(arr) < req.min_trades:
        return None

    gross_profit = float(arr[arr > 0].sum())
    gross_loss = float(-arr[arr < 0].sum())
    wins = int((arr > 0).sum())
    losses = int((arr < 0).sum())
    profit_factor = gross_profit / gross_loss if gross_loss > 0 else (999.0 if gross_profit > 0 else 0.0)
    net_ticks = float(arr.sum())
    avg_ticks = float(arr.mean())
    if avg_ticks < req.min_avg_ticks:
        return None

    gross_cash = net_ticks * req.tick_value
    total_commission = len(arr) * req.commission_per_trade
    net_cash = gross_cash - total_commission
    gross_profit_cash = gross_profit * req.tick_value
    gross_loss_cash = gross_loss * req.tick_value
    max_drawdown_cash = max_dd * req.tick_value
    return_dd_ratio = net_ticks / max_dd if max_dd > 0 else (999.0 if net_ticks > 0 else 0.0)
    return {
        "fast": fast,
        "slow": slow,
        "stop_ticks": stop_ticks,
        "target_ticks": target_ticks,
        "trades": int(len(arr)),
        "long_trades": int(long_trades),
        "short_trades": int(short_trades),
        "wins": wins,
        "losses": losses,
        "win_rate": float(wins / len(arr) * 100.0),
        "net_ticks": net_ticks,
        "gross_profit_ticks": gross_profit,
        "gross_loss_ticks": gross_loss,
        "gross_cash": float(gross_cash),
        "net_cash": float(net_cash),
        "gross_profit_cash": float(gross_profit_cash),
        "gross_loss_cash": float(gross_loss_cash),
        "total_commission": float(total_commission),
        "profit_factor": float(profit_factor),
        "avg_ticks": avg_ticks,
        "max_drawdown_ticks": float(max_dd),
        "max_drawdown_cash": float(max_drawdown_cash),
        "return_dd_ratio": float(return_dd_ratio),
        "equity_curve": equity_curve,
        "hourly_ticks": {str(hour): float(ticks) for hour, ticks in hourly_ticks.items() if abs(ticks) > 0.0000001},
    }


class OptimizationJob:
    def __init__(self, job_id: str, df: pd.DataFrame, req: OptimizationRequest, file_info: dict[str, Any]) -> None:
        self.job_id = job_id
        self.df = df
        self.req = req
        self.file_info = file_info
        self.status = "queued"
        self.message = "En cola."
        self.total_tests = 0
        self.completed_tests = 0
        self.started_at: float | None = None
        self.ended_at: float | None = None
        self.pause_started_at: float | None = None
        self.paused_seconds = 0.0
        self.results: list[dict[str, Any]] = []
        self.error: str | None = None
        self.stop_requested = False
        self.paused = False
        self.lock = threading.Lock()
        self.thread = threading.Thread(target=self.run, daemon=True)

    def start(self) -> None:
        self.thread.start()

    def run(self) -> None:
        try:
            with self.lock:
                self.status = "running"
                self.message = "Optimizacion en curso..."
                self.started_at = time.time()
            self.results, self.total_tests = optimize(self.df, self.req, self)
            with self.lock:
                if self.status != "stopped":
                    self.status = "completed"
                    self.message = "Optimizacion completada."
                self.ended_at = time.time()
        except Exception as exc:
            with self.lock:
                self.status = "failed"
                self.error = str(exc)
                self.message = str(exc)
                self.ended_at = time.time()

    def wait_if_paused_or_stopped(self) -> bool:
        while True:
            with self.lock:
                if self.stop_requested:
                    self.status = "stopped"
                    self.message = "Optimizacion detenida."
                    return False
                is_paused = self.paused
                if is_paused and self.status != "paused":
                    self.status = "paused"
                    self.message = "Optimizacion pausada."
                    self.pause_started_at = time.time()
            if not is_paused:
                with self.lock:
                    if self.status == "paused":
                        self.status = "running"
                        self.message = "Optimizacion en curso..."
                        if self.pause_started_at is not None:
                            self.paused_seconds += time.time() - self.pause_started_at
                            self.pause_started_at = None
                return True
            time.sleep(0.2)

    def mark_completed_test(self) -> None:
        with self.lock:
            self.completed_tests += 1

    def snapshot(self) -> dict[str, Any]:
        with self.lock:
            now = self.ended_at or time.time()
            if self.started_at is None:
                elapsed = 0.0
            else:
                paused_extra = (now - self.pause_started_at) if self.pause_started_at is not None else 0.0
                elapsed = max(0.0, now - self.started_at - self.paused_seconds - paused_extra)
            remaining = None
            if self.status in ("running", "paused") and self.completed_tests > 0 and self.total_tests > 0:
                rate = self.completed_tests / max(elapsed, 0.001)
                remaining = (self.total_tests - self.completed_tests) / rate if rate > 0 else None
            return {
                "job_id": self.job_id,
                "status": self.status,
                "message": self.message,
                "file": self.file_info,
                "total_tests": self.total_tests,
                "completed_tests": self.completed_tests,
                "elapsed_seconds": elapsed,
                "remaining_seconds": remaining,
                "results": self.results[:10],
                "error": self.error,
            }


def sort_results(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    results.sort(
        key=lambda row: (
            row["return_dd_ratio"],
            row["net_ticks"],
            row["profit_factor"],
            row["trades"],
            -row["max_drawdown_ticks"],
        ),
        reverse=True,
    )
    return results


def build_test_combinations(req: OptimizationRequest) -> list[tuple[int, int, int, int]]:
    fast_values = list(range(req.fast_min, req.fast_max + 1, req.fast_step))
    slow_values = list(range(req.slow_min, req.slow_max + 1, req.slow_step))
    stop_values = list(range(req.stop_min, req.stop_max + 1, req.stop_step))
    target_values = list(range(req.target_min, req.target_max + 1, req.target_step))
    combinations: list[tuple[int, int, int, int]] = []

    for fast in fast_values:
        for slow in slow_values:
            if req.limit_fast_below_slow and fast >= slow:
                continue

            for stop_ticks in stop_values:
                min_target = req.target_min
                max_target = req.target_max
                if req.limit_target_pct:
                    min_target = max(min_target, int(math.ceil(stop_ticks * req.target_pct_min / 100.0)))
                    max_target = min(max_target, int(math.floor(stop_ticks * req.target_pct_max / 100.0)))
                    if min_target > max_target:
                        continue

                for target_ticks in target_values:
                    if target_ticks < min_target or target_ticks > max_target:
                        continue
                    combinations.append((fast, slow, stop_ticks, target_ticks))

    return combinations


def optimize(
    df: pd.DataFrame,
    req: OptimizationRequest,
    job: OptimizationJob | None = None,
) -> tuple[list[dict[str, Any]], int]:
    combinations = build_test_combinations(req)
    total_tests = len(combinations)
    if job is not None:
        with job.lock:
            job.total_tests = total_tests

    close = df["close"]
    open_px = df["open"].to_numpy(dtype=float)
    high = df["high"].to_numpy(dtype=float)
    low = df["low"].to_numpy(dtype=float)
    dt = df["timestamp"]
    hours = dt.dt.hour.to_numpy()
    minutes = (hours * 60) + dt.dt.minute.to_numpy()
    tradable = inside_window(minutes, req.start_minutes, req.end_minutes)
    fast_periods = sorted({combo[0] for combo in combinations})
    slow_periods = sorted({combo[1] for combo in combinations})
    fast_emas = {period: close.ewm(span=period, adjust=False).mean().to_numpy(dtype=float) for period in fast_periods}
    slow_emas = {period: close.ewm(span=period, adjust=False).mean().to_numpy(dtype=float) for period in slow_periods}
    results: list[dict[str, Any]] = []
    signal_cache: dict[tuple[int, int], tuple[np.ndarray, np.ndarray, int]] = {}

    for fast, slow, stop_ticks, target_ticks in combinations:
        if job is not None and not job.wait_if_paused_or_stopped():
            return sort_results(results)[:10], total_tests

        key = (fast, slow)
        cached = signal_cache.get(key)
        if cached is None:
            fast_ema = fast_emas[fast]
            slow_ema = slow_emas[slow]
            cross_up = (fast_ema[1:] > slow_ema[1:]) & (fast_ema[:-1] <= slow_ema[:-1])
            cross_down = (fast_ema[1:] < slow_ema[1:]) & (fast_ema[:-1] >= slow_ema[:-1])
            start_index = max(max(fast, slow) + 2, 2)
            cached = (cross_up, cross_down, start_index)
            signal_cache[key] = cached

        cross_up, cross_down, start_index = cached
        result = simulate(
            open_px,
            high,
            low,
            hours,
            tradable,
            cross_up,
            cross_down,
            start_index,
            fast,
            slow,
            stop_ticks,
            target_ticks,
            req,
        )
        if result is not None:
            results.append(result)
        if job is not None:
            job.mark_completed_test()

    return sort_results(results)[:10], total_tests


def get_job(job_id: str) -> OptimizationJob | None:
    with JOBS_LOCK:
        return JOBS.get(job_id)


def cleanup_jobs() -> None:
    cutoff = time.time() - 3600
    with JOBS_LOCK:
        old_ids = [
            job_id
            for job_id, job in JOBS.items()
            if job.ended_at is not None and job.ended_at < cutoff
        ]
        for job_id in old_ids:
            del JOBS[job_id]


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/progress":
            query = parse_qs(parsed.query)
            job_id = query.get("job_id", [""])[0]
            job = get_job(job_id)
            if job is None:
                self.write_json(404, {"error": "Job no encontrado."})
                return
            self.write_json(200, job.snapshot())
            return

        if parsed.path not in ("/", "/index.html"):
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.end_headers()
        self.wfile.write(PAGE.encode("utf-8"))

    def do_POST(self) -> None:
        if self.path == "/control":
            try:
                length = int(self.headers.get("Content-Length", "0"))
                payload = json.loads(self.rfile.read(length).decode("utf-8"))
                job = get_job(str(payload.get("job_id", "")))
                if job is None:
                    self.write_json(404, {"error": "Job no encontrado."})
                    return
                action = str(payload.get("action", "")).lower()
                with job.lock:
                    if action == "pause" and job.status in ("queued", "running"):
                        job.paused = True
                    elif action == "resume" and (job.status == "paused" or job.paused):
                        job.paused = False
                    elif action == "stop":
                        job.stop_requested = True
                        job.paused = False
                    else:
                        self.write_json(400, {"error": "Accion invalida para el estado actual."})
                        return
                self.write_json(200, job.snapshot())
            except Exception as exc:
                self.write_json(400, {"error": str(exc)})
            return

        if self.path not in ("/start", "/optimize"):
            self.send_error(404)
            return
        try:
            form = cgi.FieldStorage(
                fp=self.rfile,
                headers=self.headers,
                environ={
                    "REQUEST_METHOD": "POST",
                    "CONTENT_TYPE": self.headers.get("Content-Type", ""),
                    "CONTENT_LENGTH": self.headers.get("Content-Length", "0"),
                },
            )
            upload = form["file"] if "file" in form else None
            if upload is None or not getattr(upload, "filename", ""):
                raise ValueError("Debes cargar un archivo.")
            file_bytes = upload.file.read()
            req = parse_request(form)
            df = load_history(file_bytes, upload.filename)
            file_info = {
                "name": html.escape(upload.filename),
                "rows": int(len(df)),
                "start": str(df["timestamp"].min()),
                "end": str(df["timestamp"].max()),
            }
            if self.path == "/optimize":
                results, total = optimize(df, req)
                self.write_json(200, {"file": file_info, "total_tests": total, "results": results})
                return

            job_id = uuid.uuid4().hex
            job = OptimizationJob(job_id, df, req, file_info)
            with JOBS_LOCK:
                JOBS[job_id] = job
            cleanup_jobs()
            job.start()
            self.write_json(200, {"job_id": job_id})
        except Exception as exc:
            self.write_json(400, {"error": str(exc)})

    def write_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=True).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args: Any) -> None:
        return


def main() -> None:
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    url = f"http://{HOST}:{PORT}"
    print(f"EMA optimizer running at {url}")
    server.serve_forever()


if __name__ == "__main__":
    main()
