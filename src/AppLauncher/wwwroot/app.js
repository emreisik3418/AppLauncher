"use strict";

const DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const DAY_TR = ["Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi"];
const DAY_ORDER = [1, 2, 3, 4, 5, 6, 0]; // Pazartesi başlasın
const STATUS_TR = {
  Running: "Çalışıyor", Succeeded: "Başarılı", Failed: "Hatalı",
  Stopped: "Durduruldu", TimedOut: "Zaman aşımı", Interrupted: "Yarıda kaldı"
};

const $ = (s, root = document) => root.querySelector(s);
const esc = (v) => String(v ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const fmt = (iso) => iso ? new Date(iso).toLocaleString("tr-TR", { dateStyle: "short", timeStyle: "short" }) : "–";
const fmtFull = (iso) => iso ? new Date(iso).toLocaleString("tr-TR") : "–";
const duration = (a, b) => {
  if (!a) return "–";
  const s = Math.max(0, Math.round(((b ? new Date(b) : new Date()) - new Date(a)) / 1000));
  const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), sec = s % 60;
  return (h ? h + "s " : "") + (h || m ? m + "dk " : "") + sec + "sn";
};

async function api(url, opt = {}) {
  const res = await fetch(url, { headers: { "Content-Type": "application/json" }, ...opt });
  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  if (!res.ok) {
    const msg = data?.errors?.join?.("\n") || data?.error || data?.title || res.statusText;
    throw new Error(msg);
  }
  return data;
}

function toast(msg) {
  const t = $("#toast");
  t.textContent = msg;
  t.classList.remove("hidden");
  clearTimeout(toast._t);
  toast._t = setTimeout(() => t.classList.add("hidden"), 3000);
}

// ------------------------------------------------------------------ liste
let appsCache = [];

async function refresh() {
  try {
    const [status, apps] = await Promise.all([api("/api/status"), api("/api/apps")]);
    $("#serverInfo").textContent = `Sunucu saati: ${fmtFull(status.serverTime)} · ${status.timeZone}`;
    const ce = $("#configError");
    if (status.configError) {
      ce.textContent = "apps.json hatalı, son geçerli yapılandırma kullanılıyor:\n" + status.configError;
      ce.classList.remove("hidden");
    } else ce.classList.add("hidden");
    $("#configPath").textContent = status.configFile;
    appsCache = apps;
    render();
  } catch (e) {
    $("#serverInfo").textContent = "Sunucuya ulaşılamıyor: " + e.message;
  }
}

function render() {
  const q = $("#search").value.trim().toLocaleLowerCase("tr");
  const list = appsCache.filter(v => !q ||
    [v.app.id, v.app.name, v.app.description, v.app.path].join(" ").toLocaleLowerCase("tr").includes(q));

  $("#empty").classList.toggle("hidden", appsCache.length > 0);
  $("#apps").innerHTML = list.map(cardHtml).join("");
}

function cardHtml(v) {
  const a = v.app;
  const running = v.activeRuns.length > 0;
  const last = v.lastRun;
  const statusBadge = running
    ? `<span class="badge b-Running">Çalışıyor</span>`
    : last ? `<span class="badge b-${last.status}">${STATUS_TR[last.status] ?? last.status}</span>` : "";
  const enabledBadge = a.schedule && a.schedule.type !== "None" && !a.enabled
    ? `<span class="badge b-off">Zamanlama kapalı</span>` : "";

  return `
  <article class="card ${a.enabled ? "" : "disabled"}" data-id="${esc(a.id)}">
    <div class="card-head">
      <div>
        <h3>${esc(a.name)}</h3>
        <div class="id">${esc(a.id)}</div>
      </div>
      <div>${statusBadge} ${enabledBadge}</div>
    </div>
    ${a.description ? `<p class="desc">${esc(a.description)}</p>` : ""}
    <div class="path">${esc(a.path)} ${esc(a.arguments)}</div>
    <dl class="meta">
      <dt>Zamanlama</dt><dd>${esc(v.scheduleText)}${v.cron ? ` <span class="id">(${esc(v.cron)})</span>` : ""}
        ${v.scheduleError ? `<br><span style="color:var(--err)">${esc(v.scheduleError)}</span>` : ""}</dd>
      <dt>Sonraki</dt><dd>${fmt(v.nextRun)}</dd>
      <dt>Son çalışma</dt><dd>${last ? `${fmt(last.startedAt)} · ${duration(last.startedAt, last.endedAt)}${last.exitCode != null ? " · kod " + last.exitCode : ""}` : "–"}</dd>
    </dl>
    <div class="card-actions">
      ${running
        ? v.activeRuns.map(r => `<button class="btn sm danger" data-act="stop" data-run="${r.id}">■ Durdur</button>
                                 <button class="btn sm" data-act="log" data-run="${r.id}">Canlı log</button>`).join("")
        : ""}
      ${!running || a.allowConcurrentRuns ? `<button class="btn sm primary" data-act="run">▶ Çalıştır</button>` : ""}
      <button class="btn sm" data-act="history">Geçmiş</button>
      ${last && !running ? `<button class="btn sm" data-act="log" data-run="${last.id}">Son log</button>` : ""}
      <button class="btn sm" data-act="toggle">${a.enabled ? "Devre dışı bırak" : "Etkinleştir"}</button>
      <button class="btn sm" data-act="edit">Düzenle</button>
      <button class="btn sm danger" data-act="delete">Sil</button>
    </div>
  </article>`;
}

$("#apps").addEventListener("click", async (e) => {
  const btn = e.target.closest("button[data-act]");
  if (!btn) return;
  const id = btn.closest(".card").dataset.id;
  const view = appsCache.find(v => v.app.id === id);
  const act = btn.dataset.act;
  try {
    if (act === "run") {
      await api(`/api/apps/${encodeURIComponent(id)}/run`, { method: "POST" });
      toast(`${view.app.name} başlatıldı`);
    } else if (act === "stop") {
      if (!confirm("Süreç sonlandırılsın mı?")) return;
      await api(`/api/runs/${btn.dataset.run}/stop`, { method: "POST" });
      toast("Durdurma isteği gönderildi");
    } else if (act === "log") {
      openLog(btn.dataset.run, view.app.name);
    } else if (act === "history") {
      openHistory(id, view.app.name);
    } else if (act === "edit") {
      openForm(view.app);
    } else if (act === "toggle") {
      await api(`/api/apps/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify({ ...view.app, enabled: !view.app.enabled }) });
    } else if (act === "delete") {
      if (!confirm(`"${view.app.name}" silinsin mi?`)) return;
      await api(`/api/apps/${encodeURIComponent(id)}`, { method: "DELETE" });
      toast("Silindi");
    }
    refresh();
  } catch (err) {
    alert(err.message);
  }
});

$("#search").addEventListener("input", render);

// ------------------------------------------------------------------ form
const form = $("#appForm");
let editingId = null;

(function buildDayInputs() {
  $("#weekDays").innerHTML = DAY_ORDER.map(i =>
    `<label><input type="checkbox" name="wd" value="${DAYS[i]}"> ${DAY_TR[i]}</label>`).join("");
  $("#dowSelect").innerHTML = DAY_ORDER.map(i =>
    `<option value="${DAYS[i]}" ${i === 4 ? "selected" : ""}>${DAY_TR[i]}</option>`).join("");
})();

function updateScheduleFields() {
  const type = form.schedType.value;
  form.querySelectorAll("[data-show]").forEach(el =>
    el.classList.toggle("hidden", !el.dataset.show.split(" ").includes(type)));
  schedulePreview();
}

function readSchedule() {
  const type = form.schedType.value;
  if (type === "None") return null;
  return {
    type,
    time: form.time.value || "00:00",
    daysOfWeek: [...form.querySelectorAll("input[name=wd]:checked")].map(c => c.value),
    dayOfMonth: parseInt(form.dayOfMonth.value || "1", 10),
    week: form.week.value,
    dayOfWeek: form.dayOfWeek.value,
    cron: form.cron.value.trim() || null
  };
}

let previewTimer;
function schedulePreview() {
  clearTimeout(previewTimer);
  previewTimer = setTimeout(async () => {
    const box = $("#preview");
    const s = readSchedule();
    if (!s) { box.innerHTML = "Uygulama yalnızca elle çalıştırılır."; return; }
    try {
      const p = await api("/api/schedule/preview", { method: "POST", body: JSON.stringify(s) });
      box.innerHTML = `<b>${esc(p.description)}</b> · <code>${esc(p.cron)}</code><br>
        Sonraki çalışmalar: ${p.next.map(fmt).join(" · ") || "–"}`;
    } catch (e) {
      box.innerHTML = `<span class="err">${esc(e.message)}</span>`;
    }
  }, 250);
}

form.addEventListener("input", (e) => {
  if (e.target.closest("fieldset")) schedulePreview();
});
form.schedType.addEventListener("change", updateScheduleFields);

function openForm(app) {
  editingId = app?.id ?? null;
  form.reset();
  $("#formErrors").classList.add("hidden");
  $("#appFormTitle").textContent = app ? "Uygulamayı düzenle" : "Yeni uygulama";

  const a = app ?? { enabled: true, timeoutMinutes: 0, environmentVariables: {} };
  form.appId.value = a.id ?? "";
  form.appName.value = a.name ?? "";
  form.description.value = a.description ?? "";
  form.path.value = a.path ?? "";
  form.arguments.value = a.arguments ?? "";
  form.workingDirectory.value = a.workingDirectory ?? "";
  form.timeoutMinutes.value = a.timeoutMinutes ?? 0;
  form.enabled.checked = a.enabled !== false;
  form.allowConcurrentRuns.checked = !!a.allowConcurrentRuns;
  form.env.value = Object.entries(a.environmentVariables ?? {}).map(([k, v]) => `${k}=${v}`).join("\n");

  const s = a.schedule;
  form.schedType.value = s?.type ?? "None";
  form.time.value = s?.time ?? "22:00";
  form.dayOfMonth.value = s?.dayOfMonth ?? 28;
  form.week.value = s?.week ?? "Last";
  form.dayOfWeek.value = s?.dayOfWeek ?? "Thursday";
  form.cron.value = s?.cron ?? "";
  form.querySelectorAll("input[name=wd]").forEach(c => c.checked = (s?.daysOfWeek ?? []).includes(c.value));

  updateScheduleFields();
  $("#dlgApp").showModal();
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  const env = {};
  form.env.value.split(/\r?\n/).forEach(line => {
    const i = line.indexOf("=");
    if (i > 0) env[line.slice(0, i).trim()] = line.slice(i + 1).trim();
  });
  const body = {
    id: form.appId.value.trim(),
    name: form.appName.value.trim(),
    description: form.description.value.trim(),
    path: form.path.value.trim(),
    arguments: form.arguments.value,
    workingDirectory: form.workingDirectory.value.trim() || null,
    enabled: form.enabled.checked,
    allowConcurrentRuns: form.allowConcurrentRuns.checked,
    timeoutMinutes: parseInt(form.timeoutMinutes.value || "0", 10),
    environmentVariables: env,
    schedule: readSchedule()
  };
  try {
    if (editingId) await api(`/api/apps/${encodeURIComponent(editingId)}`, { method: "PUT", body: JSON.stringify(body) });
    else await api("/api/apps", { method: "POST", body: JSON.stringify(body) });
    $("#dlgApp").close();
    toast("Kaydedildi");
    refresh();
  } catch (err) {
    const box = $("#formErrors");
    box.textContent = err.message;
    box.classList.remove("hidden");
  }
});

$("#btnAdd").addEventListener("click", () => openForm(null));

// ------------------------------------------------------------------ geçmiş
async function openHistory(appId, name) {
  $("#historyTitle").textContent = appId ? `Geçmiş – ${name}` : "Tüm çalıştırmalar";
  const runs = await api(`/api/runs?take=200${appId ? "&appId=" + encodeURIComponent(appId) : ""}`);
  $("#historyTable").innerHTML = `
    <thead><tr><th>Başlangıç</th>${appId ? "" : "<th>Uygulama</th>"}<th>Tetikleyen</th><th>Durum</th><th>Süre</th><th>Kod</th><th></th></tr></thead>
    <tbody>${runs.map(r => `
      <tr>
        <td>${fmtFull(r.startedAt)}</td>
        ${appId ? "" : `<td>${esc(r.appName)}</td>`}
        <td>${r.trigger === "Manual" ? "Elle" : "Zamanlama"}</td>
        <td><span class="badge b-${r.status}">${STATUS_TR[r.status] ?? r.status}</span>${r.error ? `<br><small>${esc(r.error)}</small>` : ""}</td>
        <td>${duration(r.startedAt, r.endedAt)}</td>
        <td>${r.exitCode ?? "–"}</td>
        <td><button class="btn sm" data-run="${r.id}" data-name="${esc(r.appName)}">Log</button></td>
      </tr>`).join("") || `<tr><td colspan="7">Kayıt yok.</td></tr>`}
    </tbody>`;
  $("#dlgHistory").showModal();
}

$("#historyTable").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-run]");
  if (b) openLog(b.dataset.run, b.dataset.name);
});
$("#btnHistoryAll").addEventListener("click", () => openHistory(null));

// ------------------------------------------------------------------ log
let logTimer;
async function openLog(runId, name) {
  $("#logTitle").textContent = `Log – ${name}`;
  const pre = $("#logContent");
  pre.textContent = "yükleniyor…";
  $("#dlgLog").showModal();

  const load = async () => {
    const res = await fetch(`/api/runs/${runId}/log`);
    pre.textContent = await res.text();
    if ($("#logFollow").checked) pre.scrollTop = pre.scrollHeight;
    const runs = await api(`/api/runs?take=1000`);
    const rec = runs.find(r => r.id === runId);
    if (!rec || rec.status !== "Running") clearInterval(logTimer);
  };
  clearInterval(logTimer);
  await load();
  logTimer = setInterval(load, 2000);
}
$("#dlgLog").addEventListener("close", () => clearInterval(logTimer));

// ------------------------------------------------------------------ ham config
$("#btnConfig").addEventListener("click", async () => {
  $("#configErrors").classList.add("hidden");
  const res = await fetch("/api/config");
  $("#configText").value = await res.text();
  $("#dlgConfig").showModal();
});

$("#btnConfigSave").addEventListener("click", async () => {
  try {
    await fetch("/api/config", { method: "PUT", body: $("#configText").value, headers: { "Content-Type": "application/json" } })
      .then(async r => { if (!r.ok) { const d = await r.json().catch(() => ({})); throw new Error(d.errors?.join("\n") || r.statusText); } });
    $("#dlgConfig").close();
    toast("apps.json kaydedildi");
    refresh();
  } catch (e) {
    const box = $("#configErrors");
    box.textContent = e.message;
    box.classList.remove("hidden");
  }
});

// ------------------------------------------------------------------ genel
document.querySelectorAll("dialog [data-close]").forEach(b =>
  b.addEventListener("click", () => b.closest("dialog").close()));

refresh();
setInterval(() => {
  // Bir pencere açıkken kartlar yeniden çizilsin ama odak kaybolmasın
  if (!document.querySelector("dialog[open]")) refresh();
}, 3000);
