// ========== IPC 桥接 ==========
function send(type, body) {
  window.ipc.postMessage(JSON.stringify(Object.assign({ type: type }, body || {})));
}

// ========== DOM 引用 ==========
const $ = (id) => document.getElementById(id);
const dialogueBox = $("dialogue-box");
const speakerName = $("speaker-name");
const speakerText = $("speaker-text");
const dialogueText = $("dialogue-text");
const advanceHint = $("advance-hint");
const choicesBox = $("choices");
const overlay = $("panel-overlay");
const panelTitle = $("panel-title");
const panelBody = $("panel-body");
const ctxMenu = $("context-menu");
const autoIndicator = $("auto-indicator");

// ========== 状态 ==========
let cps = 40;               // 打字速度（字符/秒）
let typing = false;
let fullText = "";
let shownCount = 0;
let typeAcc = 0;
let typeTimer = null;
let choicesVisible = false;
let currentPanel = null;    // 当前打开的面板名
let panelData = {};         // 各面板最新数据缓存
let saveLoadMode = "save";  // 存读档面板模式
let galleryTab = "cgs";

// ========== 打字机 ==========
function startDialogue(speaker, text, color) {
  clearChoices();
  if (speaker) {
    speakerName.classList.remove("hidden");
    speakerText.textContent = speaker;
    speakerText.style.color = color || "#ffffff";
  } else {
    speakerName.classList.add("hidden");
  }
  dialogueText.innerHTML = "";
  advanceHint.classList.add("hidden");
  fullText = text || "";
  shownCount = 0;
  typeAcc = 0;
  typing = true;
  if (typeTimer) clearInterval(typeTimer);
  typeTimer = setInterval(typeTick, 1000 / 60);
}

function typeTick() {
  typeAcc += cps / 60;
  const target = Math.min(Math.floor(typeAcc), fullText.length);
  if (target > shownCount) {
    for (let i = shownCount; i < target; i++) {
      const span = document.createElement("span");
      span.className = "ch";
      span.textContent = fullText[i];
      dialogueText.appendChild(span);
    }
    shownCount = target;
  }
  if (shownCount >= fullText.length) finishTyping();
}

function finishTyping() {
  if (!typing) return;
  typing = false;
  if (typeTimer) { clearInterval(typeTimer); typeTimer = null; }
  if (shownCount < fullText.length) {
    dialogueText.textContent = fullText;
    shownCount = fullText.length;
  }
  advanceHint.classList.remove("hidden");
  send("typing_complete");
}

// ========== 选择支 ==========
function showChoices(options) {
  choicesBox.innerHTML = "";
  options.forEach((opt) => {
    const btn = document.createElement("button");
    btn.className = "choice-btn";
    btn.textContent = opt.text;
    btn.onclick = (e) => {
      e.stopPropagation();
      clearChoices();
      send("choice", { target: opt.target });
    };
    choicesBox.appendChild(btn);
  });
  choicesBox.classList.remove("hidden");
  choicesVisible = true;
}

function clearChoices() {
  choicesBox.classList.add("hidden");
  choicesBox.innerHTML = "";
  choicesVisible = false;
}

// ========== 面板 ==========
const panelTitles = { history: "历史记录", save: "存档", load: "读档", settings: "设置", gallery: "鉴赏" };

function openPanel(panel) {
  currentPanel = panel;
  panelTitle.textContent = panelTitles[panel] || panel;
  renderPanel(panel);
  overlay.classList.remove("hidden");
  send("panel_opened", { panel: panel });
}

function closePanel() {
  if (!currentPanel) return;
  const closed = currentPanel;
  currentPanel = null;
  overlay.classList.add("hidden");
  send("panel_closed", { panel: closed });
}

function renderPanel(panel) {
  if (panel === "history") renderHistory();
  else if (panel === "save" || panel === "load") renderSlots();
  else if (panel === "settings") renderSettings();
  else if (panel === "gallery") renderGallery();
}

function renderHistory() {
  const entries = (panelData.history && panelData.history.entries) || [];
  panelBody.innerHTML = "";
  if (entries.length === 0) {
    panelBody.innerHTML = '<div class="gallery-empty">暂无历史记录</div>';
    return;
  }
  entries.forEach((e) => {
    const div = document.createElement("div");
    div.className = "history-entry" + (e.speaker ? "" : " narrate");
    if (e.speaker) {
      const sp = document.createElement("span");
      sp.className = "h-speaker";
      sp.textContent = e.speaker + "：";
      div.appendChild(sp);
    }
    const tx = document.createElement("span");
    tx.className = "h-text";
    tx.textContent = e.text;
    div.appendChild(tx);
    panelBody.appendChild(div);
  });
  panelBody.scrollTop = panelBody.scrollHeight;
}

function renderSlots() {
  const data = panelData.save_slots || { slots: [] };
  panelBody.innerHTML = "";
  const grid = document.createElement("div");
  grid.className = "slot-grid";
  data.slots.forEach((s) => {
    const card = document.createElement("div");
    card.className = "slot-card" + (s.empty ? " empty" : "");
    card.innerHTML =
      '<div class="slot-no">槽位 ' + s.slot + "</div>" +
      '<div class="slot-scene"></div>' +
      '<div class="slot-time"></div>';
    card.querySelector(".slot-scene").textContent = s.empty ? "—— 空 ——" : s.scene;
    card.querySelector(".slot-time").textContent = s.empty ? "" : s.time;
    card.onclick = () => {
      if (saveLoadMode === "load") {
        if (s.empty) return;
        send("load_slot", { slot: s.slot });
      } else {
        send("save_slot", { slot: s.slot });
      }
    };
    grid.appendChild(card);
  });
  panelBody.appendChild(grid);
}

function renderSettings() {
  const c = panelData.settings || {};
  panelBody.innerHTML = "";
  const rows = [
    { key: "master", label: "主音量" },
    { key: "bgm", label: "BGM 音量" },
    { key: "se", label: "SE 音量" },
    { key: "voice", label: "语音音量" },
  ];
  rows.forEach((r) => {
    const div = document.createElement("div");
    div.className = "setting-row";
    const val = Math.round((c[r.key] != null ? c[r.key] : 1) * 100);
    div.innerHTML = "<label>" + r.label + '</label><input type="range" min="0" max="100" value="' +
      val + '" data-key="' + r.key + '">';
    const input = div.querySelector("input");
    const updateFill = () => input.style.setProperty("--fill", input.value + "%");
    input.oninput = updateFill;
    updateFill();
    panelBody.appendChild(div);
  });

  // 全屏开关
  const fsRow = document.createElement("div");
  fsRow.className = "setting-row setting-inline";
  fsRow.innerHTML = '<label style="margin:0">全屏</label>' +
    '<label class="switch"><input type="checkbox" id="set-fullscreen"' +
    (c.fullscreen ? " checked" : "") + '><span class="track"></span></label>';
  panelBody.appendChild(fsRow);

  // 帧率 / 自动延迟 / 快进延迟
  const nums = [
    { key: "fps", label: "帧率锁定", min: 30, max: 240, step: 1, val: c.fps != null ? c.fps : 60 },
    { key: "autoDelay", label: "自动播放延迟（秒）", min: 0.1, max: 10, step: 0.1, val: c.autoDelay != null ? c.autoDelay : 2 },
    { key: "skipDelay", label: "快进延迟（秒）", min: 0.01, max: 1, step: 0.01, val: c.skipDelay != null ? c.skipDelay : 0.05 },
  ];
  nums.forEach((n) => {
    const div = document.createElement("div");
    div.className = "setting-row setting-inline";
    div.innerHTML = '<label style="margin:0;flex:1">' + n.label + '</label>' +
      '<input type="number" data-key="' + n.key + '" min="' + n.min + '" max="' + n.max +
      '" step="' + n.step + '" value="' + n.val + '">';
    panelBody.appendChild(div);
  });

  const saveBtn = document.createElement("button");
  saveBtn.id = "settings-save";
  saveBtn.textContent = "保存设置";
  saveBtn.onclick = () => {
    const msg = { fullscreen: $("set-fullscreen").checked };
    panelBody.querySelectorAll("input[type=range]").forEach((i) => { msg[i.dataset.key] = i.value / 100; });
    panelBody.querySelectorAll("input[type=number]").forEach((i) => { msg[i.dataset.key] = parseFloat(i.value); });
    send("apply_settings", msg);
  };
  panelBody.appendChild(saveBtn);
}

function renderGallery() {
  const g = panelData.gallery || { cgs: [], musics: [], voices: [] };
  panelBody.innerHTML = "";
  const tabs = document.createElement("div");
  tabs.className = "gallery-tabs";
  [["cgs", "CG"], ["musics", "音乐"], ["voices", "语音"]].forEach(([key, label]) => {
    const t = document.createElement("button");
    t.className = "gallery-tab" + (galleryTab === key ? " active" : "");
    t.textContent = label;
    t.onclick = () => { galleryTab = key; renderGallery(); };
    tabs.appendChild(t);
  });
  panelBody.appendChild(tabs);

  const grid = document.createElement("div");
  grid.className = "gallery-grid";
  const items = g[galleryTab] || [];
  if (items.length === 0) {
    grid.innerHTML = '<div class="gallery-empty">尚未解锁任何内容</div>';
  } else {
    items.forEach((name) => {
      const div = document.createElement("div");
      div.className = "gallery-item";
      div.textContent = name;
      grid.appendChild(div);
    });
  }
  panelBody.appendChild(grid);
}

// ========== 右键菜单 ==========
function showContextMenu(x, y) {
  $("ctx-auto-check").textContent = autoOn ? "✓ " : "";
  ctxMenu.classList.remove("hidden");
  const rect = ctxMenu.getBoundingClientRect();
  ctxMenu.style.left = Math.min(x, window.innerWidth - rect.width - 8) + "px";
  ctxMenu.style.top = Math.min(y, window.innerHeight - rect.height - 8) + "px";
}
function hideContextMenu() { ctxMenu.classList.add("hidden"); }

ctxMenu.querySelectorAll(".ctx-item").forEach((item) => {
  item.onclick = (e) => {
    e.stopPropagation();
    const action = item.dataset.action;
    hideContextMenu();
    if (action === "close") return;
    if (action === "auto") { send("toggle_auto"); return; }
    send("open_panel", { panel: action });
  };
});

// ========== 输入处理 ==========
let autoOn = false;

function handleAdvance() {
  if (currentPanel) return;
  hideContextMenu();
  if (typing) { finishTyping(); return; }
  if (choicesVisible) return;
  send("advance");
}

document.addEventListener("click", (e) => {
  if (overlay.contains(e.target) || ctxMenu.contains(e.target)) return;
  if (e.target.closest("#topbar")) return;
  handleAdvance();
});

document.addEventListener("wheel", (e) => {
  if (currentPanel) return;
  if (e.deltaY > 0) handleAdvance();
});

document.addEventListener("contextmenu", (e) => {
  e.preventDefault();
  if (currentPanel) return;
  if (ctxMenu.classList.contains("hidden")) showContextMenu(e.clientX, e.clientY);
  else hideContextMenu();
});

document.addEventListener("keydown", (e) => {
  if (e.repeat) return;
  switch (e.key) {
    case " ":
    case "Enter":
      e.preventDefault();
      if (!currentPanel) handleAdvance();
      break;
    case "Escape":
      if (currentPanel) closePanel();
      else hideContextMenu();
      break;
    case "h": case "H":
      if (!currentPanel) send("open_panel", { panel: "history" });
      break;
    case "a": case "A":
      if (!currentPanel) send("toggle_auto");
      break;
    case "Control":
      send("skip_start");
      break;
  }
});

document.addEventListener("keyup", (e) => {
  if (e.key === "Control") send("skip_end");
});

// 顶部按钮
document.querySelectorAll(".top-btn").forEach((btn) => {
  btn.onclick = (e) => {
    e.stopPropagation();
    send("open_panel", { panel: btn.dataset.panel });
  };
});

// 面板关闭按钮 / 点击遮罩空白处
$("panel-close").onclick = closePanel;
overlay.addEventListener("click", (e) => { if (e.target === overlay) closePanel(); });

// ========== 接收引擎消息 ==========
document.addEventListener("message", (e) => {
  let msg;
  try { msg = JSON.parse(e.detail); } catch { return; }

  switch (msg.type) {
    case "init":
      cps = msg.cps || 40;
      autoOn = !!msg.auto;
      autoIndicator.classList.toggle("hidden", !autoOn);
      break;
    case "dialogue":
      startDialogue(msg.speaker, msg.text, msg.color);
      break;
    case "choices":
      showChoices(msg.options || []);
      break;
    case "clear_choices":
      clearChoices();
      break;
    case "skip_typing":
      finishTyping();
      break;
    case "auto_state":
      autoOn = !!msg.on;
      autoIndicator.classList.toggle("hidden", !autoOn);
      break;
    case "history":
    case "save_slots":
    case "settings":
    case "gallery":
      panelData[msg.type] = msg;
      if (msg.type === "save_slots") saveLoadMode = msg.mode || "save";
      // 面板已打开时收到新数据则刷新
      if (currentPanel) {
        const map = { history: "history", save_slots: saveLoadMode, settings: "settings", gallery: "gallery" };
        if (map[msg.type] === currentPanel) renderPanel(currentPanel);
      }
      break;
    case "open_panel":
      openPanel(msg.panel);
      break;
    case "close_all":
      if (currentPanel) { currentPanel = null; overlay.classList.add("hidden"); }
      hideContextMenu();
      break;
  }
});

// ========== 就绪通知 ==========
document.addEventListener("DOMContentLoaded", () => send("ready"));
