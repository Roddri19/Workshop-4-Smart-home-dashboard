"use strict";

// How often the page asks the API for fresh data.
const POLL_MS = 5000;
// Actions that need a second click to confirm.
const DANGEROUS = new Set(["SHUTDOWN", "RESTART"]);
// Actions not shown as buttons (the card already shows this information).
const HIDDEN = new Set(["INFO"]);

const $ = (id) => document.getElementById(id);
const grid = $("devices");
const template = $("device-template");

// Per-device UI state that must survive the periodic re-render.
const cards = new Map();   // id -> card element
const outputs = new Map(); // id -> { text, isError }

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const problem = await response.json();
      message = problem.detail || firstValidationError(problem) || problem.title || message;
    } catch { /* body was not JSON */ }
    throw new Error(message);
  }
  return response.status === 204 ? null : response.json();
}

function firstValidationError(problem) {
  const errors = problem && problem.errors;
  if (!errors) return null;
  const first = Object.values(errors)[0];
  return Array.isArray(first) ? first[0] : null;
}

// ---------- Formatting ----------

function formatMetric(key, value) {
  const number = Number(value);
  if (key === "UptimeSeconds" && Number.isFinite(number)) return formatDuration(number);
  if (key === "Temperature" && Number.isFinite(number)) return `${number.toFixed(1)} °C`;
  return value;
}

function metricLabel(key) {
  if (key === "UptimeSeconds") return "Uptime";
  return key.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatDuration(totalSeconds) {
  const s = Math.max(0, Math.floor(totalSeconds));
  const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600), m = Math.floor((s % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s % 60}s`;
  return `${s}s`;
}

function timeAgo(iso) {
  if (!iso) return null;
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 5) return "just now";
  if (seconds < 60) return `${seconds} s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  return new Date(iso).toLocaleString();
}

// ---------- Rendering ----------

function render(data) {
  $("kpi-online").textContent = data.online;
  $("kpi-total").textContent = data.total;
  $("kpi-offline").textContent = data.offline;
  $("kpi-availability").textContent = data.total ? `${Math.round((data.online / data.total) * 100)}%` : "–";
  $("last-update").textContent = `Updated ${new Date(data.generatedUtc).toLocaleTimeString()}`;
  $("interval-note").textContent = `Each device is checked every ${data.checkIntervalSeconds} s`;
  $("empty").hidden = data.devices.length > 0;

  const seen = new Set();
  data.devices.forEach((device, index) => {
    seen.add(device.id);
    let card = cards.get(device.id);
    if (!card) {
      card = createCard(device.id);
      cards.set(device.id, card);
    }
    updateCard(card, device);
    // Keep the order sent by the API (online first) without rebuilding the cards.
    if (grid.children[index] !== card) grid.insertBefore(card, grid.children[index] || null);
  });

  for (const [id, card] of cards) {
    if (!seen.has(id)) {
      card.remove();
      cards.delete(id);
      outputs.delete(id);
    }
  }
}

function createCard(id) {
  const card = template.content.firstElementChild.cloneNode(true);
  card.dataset.id = id;
  card.querySelector('[data-role="refresh"]').addEventListener("click", (e) => refreshDevice(id, e.currentTarget));
  card.querySelector('[data-role="remove"]').addEventListener("click", (e) => removeDevice(id, e.currentTarget));
  return card;
}

function updateCard(card, device) {
  card.classList.toggle("is-offline", !device.isOnline);
  card.querySelector(".device-name").textContent = device.name;
  card.querySelector(".device-address").textContent = device.address;

  const pill = card.querySelector(".pill");
  pill.textContent = device.isOnline ? "Online" : "Offline";
  pill.className = `pill ${device.isOnline ? "pill-online" : "pill-offline"}`;

  const metrics = card.querySelector(".metrics");
  metrics.replaceChildren(...Object.entries(device.metrics).map(([key, value]) => {
    const wrapper = document.createElement("div");
    const dt = document.createElement("dt");
    const dd = document.createElement("dd");
    dt.textContent = metricLabel(key);
    dd.textContent = formatMetric(key, value);
    wrapper.append(dt, dd);
    return wrapper;
  }));
  metrics.hidden = Object.keys(device.metrics).length === 0;

  const lastSeen = timeAgo(device.lastSeenUtc);
  card.querySelector(".seen").textContent = device.isOnline
    ? `Last seen ${lastSeen}`
    : lastSeen ? `Not answering · last seen ${lastSeen} (values above are the last known)` : "Not answering · never seen online";

  // Rebuild the action buttons only when the list changed, so clicks and confirmations are not lost.
  const visible = device.actions.filter((a) => !HIDDEN.has(a.name.toUpperCase()));
  const signature = visible.map((a) => a.name).join("|") + `#${device.isOnline}`;
  const actions = card.querySelector(".actions");
  if (actions.dataset.signature !== signature) {
    actions.dataset.signature = signature;
    actions.replaceChildren(...visible.map((action) => actionButton(device.id, action.name, device.isOnline)));
  }
  actions.hidden = visible.length === 0;

  const output = card.querySelector(".output");
  const state = outputs.get(device.id);
  output.hidden = !state;
  if (state) {
    output.textContent = state.text;
    output.classList.toggle("is-error", state.isError);
  }
}

function actionButton(deviceId, name, isOnline) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "btn";
  button.textContent = prettyAction(name);
  button.disabled = !isOnline;
  if (!isOnline) button.title = "The device is offline";

  const dangerous = DANGEROUS.has(name.toUpperCase());
  if (dangerous) button.classList.add("btn-danger");

  let confirmTimer = null;
  button.addEventListener("click", () => {
    if (dangerous && !button.dataset.confirming) {
      // First click: ask for confirmation inside the button itself (no browser pop-ups).
      button.dataset.confirming = "1";
      button.textContent = `Confirm ${prettyAction(name).toLowerCase()}?`;
      button.classList.add("btn-confirm");
      confirmTimer = setTimeout(() => resetConfirm(button, name), 4000);
      return;
    }
    clearTimeout(confirmTimer);
    resetConfirm(button, name);
    runAction(deviceId, name, button);
  });
  return button;
}

function resetConfirm(button, name) {
  delete button.dataset.confirming;
  button.textContent = prettyAction(name);
  button.classList.remove("btn-confirm");
}

function prettyAction(name) {
  return name === name.toUpperCase() ? name.charAt(0) + name.slice(1).toLowerCase() : name;
}

// ---------- Actions ----------

async function runAction(deviceId, name, button) {
  button.disabled = true;
  const label = button.textContent;
  button.textContent = "Sending…";
  try {
    const result = await api(`/api/devices/${deviceId}/actions/${encodeURIComponent(name)}`, { method: "POST" });
    const text = (result.output || "").trim();
    outputs.set(deviceId, { text: `${result.action}: ${text || "done (no output)"}`, isError: false });
  } catch (error) {
    outputs.set(deviceId, { text: `${name} failed: ${error.message}`, isError: true });
  } finally {
    button.disabled = false;
    button.textContent = label;
    await load();
  }
}

async function refreshDevice(id, button) {
  button.disabled = true;
  try {
    await api(`/api/devices/${id}/refresh`, { method: "POST" });
  } catch (error) {
    showError(error.message);
  } finally {
    button.disabled = false;
    await load();
  }
}

async function removeDevice(id, button) {
  if (!button.dataset.confirming) {
    button.dataset.confirming = "1";
    button.textContent = "Click again to remove";
    setTimeout(() => { delete button.dataset.confirming; button.textContent = "Remove"; }, 4000);
    return;
  }
  button.disabled = true;
  try {
    await api(`/api/devices/${id}`, { method: "DELETE" });
  } catch (error) {
    showError(error.message);
    button.disabled = false;
  }
  await load();
}

async function refreshAll(button) {
  button.disabled = true;
  try {
    await Promise.all([...cards.keys()].map((id) => api(`/api/devices/${id}/refresh`, { method: "POST" })));
  } catch (error) {
    showError(error.message);
  } finally {
    button.disabled = false;
    await load();
  }
}

$("refresh-all").addEventListener("click", (e) => refreshAll(e.currentTarget));

$("add-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const message = $("add-msg");
  const submit = form.querySelector("button[type=submit]");
  const body = {
    address: form.elements.address.value.trim(),
    name: form.elements.namedItem("name").value.trim() || null,
  };
  if (!body.address) {
    message.textContent = "Enter the device address.";
    message.className = "form-msg is-error";
    form.elements.address.focus();
    return;
  }

  submit.disabled = true;
  message.textContent = "Contacting the device…";
  message.className = "form-msg";
  try {
    const device = await api("/api/devices", { method: "POST", body: JSON.stringify(body) });
    message.textContent = device.isOnline
      ? `${device.name} added and online.`
      : `${device.name} added, but it is not answering yet. It will turn green when it comes online.`;
    message.className = "form-msg is-ok";
    form.reset();
    await load();
  } catch (error) {
    message.textContent = error.message;
    message.className = "form-msg is-error";
  } finally {
    submit.disabled = false;
  }
});

// ---------- Polling ----------

function showError(text) {
  const box = $("error");
  box.textContent = text;
  box.hidden = !text;
}

let loading = false;
async function load() {
  if (loading) return;
  loading = true;
  try {
    render(await api("/api/dashboard"));
    showError("");
  } catch (error) {
    showError(`Cannot reach the Home Management API: ${error.message}`);
  } finally {
    loading = false;
  }
}

load();
setInterval(load, POLL_MS);
