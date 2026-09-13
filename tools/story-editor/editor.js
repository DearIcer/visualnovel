/* ============================================================
 * 交互式文本 · 剧情编辑器
 * 纯前端实现，无构建步骤。直接编辑 story/*.json。
 * 推荐使用 Chrome / Edge（File System Access API 直接读写本地文件），
 * 其他浏览器自动退化为「上传打开 / 下载保存」。
 * ============================================================ */

'use strict';

/* ---------- 指令定义：与 scripts/Engine/CommandRunner.cs 支持的指令保持一致 ---------- */
// kind: text=单行文本, textarea=多行, number=数字, select=下拉, char=角色(带提示), scene=场景(带提示), checkbox=布尔, color=颜色
const CMD_DEFS = {
  say:       { label: '对话',     fields: [
    { key: 'character', name: '角色', kind: 'char', required: true },
    { key: 'text', name: '台词', kind: 'textarea', required: true },
  ]},
  narrate:   { label: '旁白',     fields: [
    { key: 'text', name: '文本', kind: 'textarea', required: true },
  ]},
  bg:        { label: '背景',     fields: [
    { key: 'asset', name: '背景文件名', kind: 'text', placeholder: 'assets/backgrounds 下文件名，不含 .png' },
    { key: 'color', name: '纯色', kind: 'color' },
    { key: 'fade', name: '淡入(秒)', kind: 'number' },
  ]},
  bgm:       { label: '背景音乐', fields: [
    { key: 'action', name: '动作', kind: 'select', options: ['play', 'crossfade', 'stop'], required: true },
    { key: 'track', name: '曲目', kind: 'text' },
    { key: 'fade', name: '淡变(秒)', kind: 'number' },
    { key: 'loop', name: '循环', kind: 'checkbox' },
  ]},
  se:        { label: '音效',     fields: [
    { key: 'sound', name: '音效文件名', kind: 'text', required: true, placeholder: 'assets/se 下文件名，不含 .wav' },
    { key: 'volume', name: '音量(0-1)', kind: 'number' },
    { key: 'pitch', name: '音调', kind: 'number' },
  ]},
  voice:     { label: '语音',     fields: [
    { key: 'path', name: '语音路径', kind: 'text', required: true },
  ]},
  show:      { label: '显示立绘', fields: [
    { key: 'character', name: '角色', kind: 'char', required: true },
    { key: 'position', name: '位置', kind: 'select', options: ['', 'left', 'center', 'right'] },
    { key: 'emotion', name: '表情', kind: 'text' },
    { key: 'animation', name: '动画', kind: 'text' },
    { key: 'duration', name: '时长(秒)', kind: 'number' },
  ]},
  hide:      { label: '隐藏立绘', fields: [
    { key: 'character', name: '角色', kind: 'char', required: true },
  ]},
  move:      { label: '移动立绘', fields: [
    { key: 'character', name: '角色', kind: 'char', required: true },
    { key: 'position', name: '位置', kind: 'select', options: ['left', 'center', 'right'], required: true },
    { key: 'animation', name: '动画', kind: 'text' },
    { key: 'duration', name: '时长(秒)', kind: 'number' },
  ]},
  choice:    { label: '选择支',   special: 'choice' },
  jump:      { label: '跳转场景', fields: [
    { key: 'target', name: '目标场景', kind: 'scene', required: true },
  ]},
  set:       { label: '设置变量', fields: [
    { key: 'name', name: '变量名', kind: 'text', required: true },
    { key: 'value', name: '值', kind: 'any', required: true, placeholder: '数字 / true,false / 字符串' },
  ]},
  if:        { label: '条件判断', fields: [
    { key: 'name', name: '变量名', kind: 'text', required: true },
    { key: 'op', name: '比较符', kind: 'select', options: ['==', '!=', '>', '>=', '<', '<='], required: true },
    { key: 'value', name: '值', kind: 'any', required: true },
  ]},
  cg:        { label: '解锁 CG',  fields: [ { key: 'id', name: 'CG ID', kind: 'text', required: true } ]},
  music:     { label: '解锁音乐', fields: [ { key: 'id', name: '音乐 ID', kind: 'text', required: true } ]},
  vo_unlock: { label: '解锁语音', fields: [ { key: 'id', name: '语音 ID', kind: 'text', required: true } ]},
  cgshow:    { label: '展示 CG',  fields: [
    { key: 'id', name: 'CG ID', kind: 'text', required: true, placeholder: 'assets/cg 下文件名，不含 .png' },
    { key: 'fade', name: '淡入(秒)', kind: 'number' },
  ]},
  cghide:    { label: '收起 CG',  fields: [] },
};

const CMD_ORDER = ['say', 'narrate', 'show', 'hide', 'move', 'bg', 'bgm', 'se', 'voice',
                   'choice', 'jump', 'set', 'if', 'cg', 'music', 'vo_unlock', 'cgshow', 'cghide'];

/* ---------- 全局状态 ---------- */
let doc = null;            // { characters: {...}, scenes: {...} }
let sceneOrder = [];       // 场景顺序（JSON 对象序之外的显式维护）
let currentScene = null;   // 当前编辑的场景 id
let fileHandle = null;     // File System Access 句柄
let dirty = false;

/* ---------- DOM 快捷引用 ---------- */
const $ = (id) => document.getElementById(id);
const el = (tag, cls, text) => {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text !== undefined) n.textContent = text;
  return n;
};

/* ============================================================
 * 文档加载 / 保存
 * ============================================================ */

function newDoc() {
  return {
    characters: {},
    scenes: { start: { commands: [{ cmd: 'narrate', text: '' }] } },
  };
}

function loadDoc(data, name) {
  if (!data || typeof data !== 'object' || !data.scenes) {
    alert('文件格式不正确：缺少 scenes 字段。');
    return;
  }
  doc = {
    characters: data.characters || {},
    scenes: data.scenes,
  };
  sceneOrder = Object.keys(doc.scenes);
  currentScene = sceneOrder[0] || null;
  dirty = false;
  fileHandle = null;
  $('file-name').textContent = name;
  updateDirtyDot();
  $('welcome').classList.add('hidden');
  $('scene-editor').classList.remove('hidden');
  hideIssues();
  renderAll();
}

async function openFile() {
  if (window.showOpenFilePicker) {
    try {
      const [handle] = await window.showOpenFilePicker({
        types: [{ description: '剧情脚本', accept: { 'application/json': ['.json'] } }],
      });
      const file = await handle.getFile();
      const data = JSON.parse(await file.text());
      loadDoc(data, file.name);
      fileHandle = handle;
    } catch (e) {
      if (e.name === 'AbortError') return;
      if (e instanceof SyntaxError) alert('JSON 解析失败：' + e.message);
      else alert('打开文件失败：' + e.message);
    }
  } else {
    $('file-input').click();
  }
}

$('file-input').addEventListener('change', (e) => {
  const file = e.target.files[0];
  e.target.value = '';
  if (!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    try {
      loadDoc(JSON.parse(reader.result), file.name);
    } catch (err) {
      alert('JSON 解析失败：' + err.message);
    }
  };
  reader.readAsText(file);
});

function serializeDoc() {
  const out = { characters: doc.characters, scenes: {} };
  for (const sid of sceneOrder) out.scenes[sid] = doc.scenes[sid];
  return JSON.stringify(out, null, 2) + '\n';
}

async function saveFile() {
  if (!doc) return;
  if (!validate(false)) {
    if (!confirm(`校验发现 ${lastIssues.length} 个问题，仍要保存吗？`)) return;
  }
  const text = serializeDoc();
  if (fileHandle) {
    try {
      const w = await fileHandle.createWritable();
      await w.write(text);
      await w.close();
      dirty = false;
      updateDirtyDot();
    } catch (e) {
      alert('保存失败：' + e.message);
    }
  } else {
    saveAsFile();
  }
}

async function saveAsFile() {
  if (!doc) return;
  const text = serializeDoc();
  if (window.showSaveFilePicker) {
    try {
      const handle = await window.showSaveFilePicker({
        suggestedName: 'main.json',
        types: [{ description: '剧情脚本', accept: { 'application/json': ['.json'] } }],
      });
      const w = await handle.createWritable();
      await w.write(text);
      await w.close();
      fileHandle = handle;
      $('file-name').textContent = handle.name;
      dirty = false;
      updateDirtyDot();
    } catch (e) {
      if (e.name !== 'AbortError') alert('保存失败：' + e.message);
    }
  } else {
    downloadText(text, 'main.json');
    dirty = false;
    updateDirtyDot();
  }
}

function downloadText(text, name) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
  a.download = name;
  a.click();
  URL.revokeObjectURL(a.href);
}

function markDirty() {
  dirty = true;
  updateDirtyDot();
}

function updateDirtyDot() {
  $('dirty-dot').classList.toggle('hidden', !dirty);
}

/* ============================================================
 * 渲染
 * ============================================================ */

function renderAll() {
  renderDatalists();
  renderSidebar();
  renderScene();
}

function renderDatalists() {
  const dlChars = $('dl-characters');
  dlChars.textContent = '';
  for (const cid of Object.keys(doc.characters)) {
    const o = document.createElement('option');
    o.value = cid;
    o.label = doc.characters[cid].displayName || cid;
    dlChars.appendChild(o);
  }
  const dlScenes = $('dl-scenes');
  dlScenes.textContent = '';
  for (const sid of sceneOrder) {
    const o = document.createElement('option');
    o.value = sid;
    dlScenes.appendChild(o);
  }
}

function renderSidebar() {
  const list = $('scene-list');
  list.textContent = '';
  for (const sid of sceneOrder) {
    const li = el('li');
    li.dataset.sid = sid;
    li.draggable = true;
    if (sid === currentScene) li.classList.add('active');
    const label = el('span', 'sid', sid);
    const cnt = el('span', 'cnt', String((doc.scenes[sid].commands || []).length));
    li.append(label, cnt);
    li.addEventListener('click', () => { currentScene = sid; renderSidebar(); renderScene(); });

    // 拖拽排序
    li.addEventListener('dragstart', (e) => e.dataTransfer.setData('text/plain', sid));
    li.addEventListener('dragover', (e) => { e.preventDefault(); li.classList.add('drag-over'); });
    li.addEventListener('dragleave', () => li.classList.remove('drag-over'));
    li.addEventListener('drop', (e) => {
      e.preventDefault();
      li.classList.remove('drag-over');
      const from = e.dataTransfer.getData('text/plain');
      if (!from || from === sid) return;
      const fi = sceneOrder.indexOf(from);
      sceneOrder.splice(fi, 1);
      sceneOrder.splice(sceneOrder.indexOf(sid), 0, from);
      markDirty();
      renderSidebar();
    });

    list.appendChild(li);
  }
}

function renderScene() {
  const listEl = $('command-list');
  listEl.textContent = '';
  if (!currentScene || !doc.scenes[currentScene]) {
    $('scene-id-label').textContent = '（无场景）';
    return;
  }
  $('scene-id-label').textContent = currentScene;
  const cmds = doc.scenes[currentScene].commands || [];
  cmds.forEach((cmd, i) => listEl.appendChild(renderCmdCard(cmd, i)));
}

function renderCmdCard(cmd, index) {
  const card = el('div', 'cmd-card');
  card.dataset.index = index;
  card.dataset.cmd = cmd.cmd || '';

  // 头部：序号 / 类型 / 工具按钮
  const head = el('div', 'cmd-head');
  head.appendChild(el('span', 'cmd-index', String(index + 1)));

  const typeSel = document.createElement('select');
  typeSel.className = 'cmd-type';
  const types = CMD_ORDER.includes(cmd.cmd) ? CMD_ORDER : [cmd.cmd, ...CMD_ORDER];
  for (const t of types) {
    const o = document.createElement('option');
    o.value = t;
    o.textContent = CMD_DEFS[t] ? `${CMD_DEFS[t].label} (${t})` : `${t}（未知）`;
    if (t === cmd.cmd) o.selected = true;
    typeSel.appendChild(o);
  }
  typeSel.addEventListener('change', () => {
    replaceCommandType(index, typeSel.value);
  });
  head.appendChild(typeSel);
  head.appendChild(el('span', 'spacer'));

  const tools = el('div', 'cmd-tools');
  const mkBtn = (txt, title, fn) => {
    const b = el('button', 'icon-btn', txt);
    b.title = title;
    b.addEventListener('click', fn);
    tools.appendChild(b);
  };
  mkBtn('＋', '在下方插入指令', () => insertCommand(index + 1));
  mkBtn('⧉', '复制此指令', () => duplicateCommand(index));
  mkBtn('↑', '上移', () => moveCommand(index, -1));
  mkBtn('↓', '下移', () => moveCommand(index, 1));
  mkBtn('✕', '删除此指令', () => deleteCommand(index));
  head.appendChild(tools);
  card.appendChild(head);

  // 主体
  const body = el('div', 'cmd-body');
  const def = CMD_DEFS[cmd.cmd];
  if (!def) {
    body.appendChild(renderRawJsonEditor(cmd, index));
  } else if (def.special === 'choice') {
    body.appendChild(renderChoiceEditor(cmd, index));
  } else {
    for (const f of def.fields) body.appendChild(renderField(cmd, f));
  }
  card.appendChild(body);
  return card;
}

/* 普通字段行 */
function renderField(cmd, f) {
  const row = el('div', 'field-row');
  row.appendChild(el('span', 'fname', f.name + (f.required ? ' *' : '')));
  const wrap = el('div', 'finput');
  let input;
  const val = cmd[f.key];

  switch (f.kind) {
    case 'textarea': {
      input = document.createElement('textarea');
      input.value = val ?? '';
      input.addEventListener('input', () => { setCmdArg(cmd, f.key, input.value); });
      break;
    }
    case 'number': {
      input = document.createElement('input');
      input.type = 'number';
      input.step = 'any';
      input.value = val ?? '';
      input.addEventListener('input', () => {
        setCmdArg(cmd, f.key, input.value === '' ? undefined : parseFloat(input.value));
      });
      break;
    }
    case 'select': {
      input = document.createElement('select');
      for (const opt of f.options) {
        const o = document.createElement('option');
        o.value = opt;
        o.textContent = opt === '' ? '（默认）' : opt;
        if ((val ?? '') === opt || (val === undefined && opt === '')) o.selected = true;
      }
      if (val !== undefined && !f.options.includes(val)) {
        const o = document.createElement('option');
        o.value = val; o.textContent = val; o.selected = true;
        input.appendChild(o);
      }
      input.addEventListener('change', () => {
        setCmdArg(cmd, f.key, input.value === '' && f.options.includes('') ? undefined : input.value);
      });
      break;
    }
    case 'checkbox': {
      input = document.createElement('input');
      input.type = 'checkbox';
      input.checked = val === true;
      input.addEventListener('change', () => setCmdArg(cmd, f.key, input.checked ? true : undefined));
      break;
    }
    case 'color': {
      const box = el('div', 'inline-inputs');
      input = document.createElement('input');
      input.type = 'color';
      input.value = /^#[0-9a-fA-F]{6}$/.test(val) ? val : '#000000';
      const txt = document.createElement('input');
      txt.type = 'text';
      txt.value = val ?? '';
      txt.placeholder = '留空则不使用纯色';
      input.addEventListener('input', () => { txt.value = input.value; setCmdArg(cmd, f.key, input.value); });
      txt.addEventListener('input', () => {
        setCmdArg(cmd, f.key, txt.value === '' ? undefined : txt.value);
        if (/^#[0-9a-fA-F]{6}$/.test(txt.value)) input.value = txt.value;
      });
      box.append(input, txt);
      wrap.appendChild(box);
      row.appendChild(wrap);
      return row;
    }
    case 'any': { // set/if 的值：尝试解析为数字/布尔，否则按字符串
      input = document.createElement('input');
      input.type = 'text';
      input.value = val === undefined ? '' : (typeof val === 'string' ? val : JSON.stringify(val));
      input.placeholder = f.placeholder || '';
      input.addEventListener('input', () => {
        const t = input.value.trim();
        if (t === '') { setCmdArg(cmd, f.key, undefined); return; }
        try { setCmdArg(cmd, f.key, JSON.parse(t)); }
        catch { setCmdArg(cmd, f.key, input.value); }
      });
      break;
    }
    default: { // text / char / scene
      input = document.createElement('input');
      input.type = 'text';
      input.value = val ?? '';
      input.placeholder = f.placeholder || '';
      if (f.kind === 'char') input.setAttribute('list', 'dl-characters');
      if (f.kind === 'scene') input.setAttribute('list', 'dl-scenes');
      input.addEventListener('input', () => {
        setCmdArg(cmd, f.key, input.value === '' ? undefined : input.value);
      });
    }
  }
  wrap.appendChild(input);
  row.appendChild(wrap);
  return row;
}

/* 选择支编辑器 */
function renderChoiceEditor(cmd, index) {
  const box = el('div');
  if (!Array.isArray(cmd.options)) cmd.options = [];

  cmd.options.forEach((opt, oi) => {
    const row = el('div', 'choice-opt');
    const txt = document.createElement('input');
    txt.type = 'text';
    txt.className = 'opt-text';
    txt.placeholder = '选项文本';
    txt.value = opt.text ?? '';
    txt.addEventListener('input', () => { opt.text = txt.value; markDirty(); });

    const tgt = document.createElement('input');
    tgt.type = 'text';
    tgt.className = 'opt-target';
    tgt.placeholder = '目标场景';
    tgt.setAttribute('list', 'dl-scenes');
    tgt.value = opt.target ?? '';
    tgt.addEventListener('input', () => {
      if (tgt.value === '') delete opt.target; else opt.target = tgt.value;
      markDirty();
    });

    const del = el('button', 'icon-btn', '✕');
    del.title = '删除此选项';
    del.addEventListener('click', () => {
      cmd.options.splice(oi, 1);
      markDirty();
      renderScene();
    });

    row.append(txt, tgt, del);
    box.appendChild(row);
  });

  const add = el('button', 'tb-btn choice-add', '＋ 添加选项');
  add.addEventListener('click', () => {
    cmd.options.push({ text: '', target: sceneOrder[0] || '' });
    markDirty();
    renderScene();
  });
  box.appendChild(add);
  return box;
}

/* 未知指令：原始 JSON 编辑，保证往返不丢数据 */
function renderRawJsonEditor(cmd, index) {
  const box = el('div');
  const hint = el('p', null, '未知指令类型，以原始 JSON 编辑：');
  hint.style.cssText = 'color:var(--danger);font-size:12px;margin-bottom:6px;';
  const ta = document.createElement('textarea');
  ta.className = 'json-raw';
  ta.value = JSON.stringify(cmd, null, 2);
  ta.addEventListener('input', () => {
    try {
      const parsed = JSON.parse(ta.value);
      const cmds = doc.scenes[currentScene].commands;
      cmds[index] = parsed;
      ta.classList.remove('invalid');
      markDirty();
    } catch {
      ta.classList.add('invalid');
    }
  });
  box.append(hint, ta);
  return box;
}

/* 写参：undefined 表示删除该键（保持 JSON 干净） */
function setCmdArg(cmd, key, value) {
  if (value === undefined) delete cmd[key];
  else cmd[key] = value;
  markDirty();
}

/* ============================================================
 * 指令操作
 * ============================================================ */

function cmdsOfCurrent() {
  return doc.scenes[currentScene].commands;
}

function defaultCommand(type) {
  const firstChar = Object.keys(doc.characters)[0];
  switch (type) {
    case 'say': return { cmd: 'say', character: firstChar || '', text: '' };
    case 'narrate': return { cmd: 'narrate', text: '' };
    case 'bg': return { cmd: 'bg', asset: '', fade: 1.0 };
    case 'bgm': return { cmd: 'bgm', action: 'play', track: '', fade: 1.0 };
    case 'se': return { cmd: 'se', sound: '' };
    case 'voice': return { cmd: 'voice', path: '' };
    case 'show': return { cmd: 'show', character: firstChar || '', emotion: '' };
    case 'hide': return { cmd: 'hide', character: firstChar || '' };
    case 'move': return { cmd: 'move', character: firstChar || '', position: 'center' };
    case 'choice': return { cmd: 'choice', options: [{ text: '', target: sceneOrder[0] || '' }] };
    case 'jump': return { cmd: 'jump', target: sceneOrder[0] || '' };
    case 'set': return { cmd: 'set', name: '', value: 0 };
    case 'if': return { cmd: 'if', name: '', op: '==', value: 0 };
    case 'cg': return { cmd: 'cg', id: '' };
    case 'music': return { cmd: 'music', id: '' };
    case 'vo_unlock': return { cmd: 'vo_unlock', id: '' };
    default: return { cmd: type };
  }
}

function insertCommand(at, type) {
  const cmds = cmdsOfCurrent();
  cmds.splice(at, 0, defaultCommand(type || $('add-type').value || 'narrate'));
  markDirty();
  renderScene();
}

function duplicateCommand(index) {
  const cmds = cmdsOfCurrent();
  cmds.splice(index + 1, 0, JSON.parse(JSON.stringify(cmds[index])));
  markDirty();
  renderScene();
}

function moveCommand(index, delta) {
  const cmds = cmdsOfCurrent();
  const to = index + delta;
  if (to < 0 || to >= cmds.length) return;
  [cmds[index], cmds[to]] = [cmds[to], cmds[index]];
  markDirty();
  renderScene();
}

function deleteCommand(index) {
  cmdsOfCurrent().splice(index, 1);
  markDirty();
  renderScene();
}

function replaceCommandType(index, newType) {
  const cmds = cmdsOfCurrent();
  const old = cmds[index];
  const next = defaultCommand(newType);
  // 尽量保留通用字段，减少重新填写
  for (const k of ['character', 'text', 'target']) {
    if (old[k] !== undefined && CMD_DEFS[newType] &&
        (CMD_DEFS[newType].fields || []).some((f) => f.key === k)) {
      next[k] = old[k];
    }
  }
  cmds[index] = next;
  markDirty();
  renderScene();
}

/* ============================================================
 * 场景操作
 * ============================================================ */

function addScene() {
  const id = prompt('新场景 id（英文/数字/下划线）：');
  if (!id) return;
  if (!/^[A-Za-z0-9_\-]+$/.test(id)) { alert('场景 id 只能包含英文、数字、下划线、连字符。'); return; }
  if (doc.scenes[id]) { alert('场景已存在：' + id); return; }
  doc.scenes[id] = { commands: [] };
  sceneOrder.push(id);
  currentScene = id;
  markDirty();
  renderAll();
}

function renameScene() {
  if (!currentScene) return;
  const id = prompt('重命名场景为：', currentScene);
  if (!id || id === currentScene) return;
  if (!/^[A-Za-z0-9_\-]+$/.test(id)) { alert('场景 id 只能包含英文、数字、下划线、连字符。'); return; }
  if (doc.scenes[id]) { alert('场景已存在：' + id); return; }
  doc.scenes[id] = doc.scenes[currentScene];
  delete doc.scenes[currentScene];
  sceneOrder[sceneOrder.indexOf(currentScene)] = id;
  // 同步所有 jump / choice 引用
  for (const sid of sceneOrder) {
    for (const c of doc.scenes[sid].commands || []) {
      if (c.target === currentScene) c.target = id;
      if (Array.isArray(c.options)) {
        for (const o of c.options) if (o.target === currentScene) o.target = id;
      }
    }
  }
  currentScene = id;
  markDirty();
  renderAll();
}

function deleteScene() {
  if (!currentScene) return;
  if (sceneOrder.length <= 1) { alert('至少保留一个场景。'); return; }
  // 检查引用
  const refs = [];
  for (const sid of sceneOrder) {
    (doc.scenes[sid].commands || []).forEach((c, i) => {
      if (c.target === currentScene) refs.push(`${sid}[${i + 1}]`);
      if (Array.isArray(c.options)) c.options.forEach((o) => { if (o.target === currentScene) refs.push(`${sid}[${i + 1}]`); });
    });
  }
  const msg = refs.length
    ? `场景「${currentScene}」仍被以下指令引用：\n${refs.join('、')}\n\n删除后这些跳转将失效，确认删除？`
    : `确认删除场景「${currentScene}」？`;
  if (!confirm(msg)) return;
  delete doc.scenes[currentScene];
  sceneOrder.splice(sceneOrder.indexOf(currentScene), 1);
  currentScene = sceneOrder[0];
  markDirty();
  renderAll();
}

/* ============================================================
 * 角色管理
 * ============================================================ */

function renderCharModal() {
  const box = $('char-list');
  box.textContent = '';
  for (const [cid, c] of Object.entries(doc.characters)) {
    const row = el('div', 'char-row');

    const idEl = el('span', 'cid', cid);

    const name = document.createElement('input');
    name.type = 'text';
    name.className = 'cname';
    name.value = c.displayName || '';
    name.placeholder = '显示名';
    name.addEventListener('input', () => { c.displayName = name.value; markDirty(); });

    const color = document.createElement('input');
    color.type = 'color';
    color.value = /^#[0-9a-fA-F]{6}$/.test(c.color) ? c.color : '#ffffff';
    color.addEventListener('input', () => { c.color = color.value; markDirty(); });

    const pos = document.createElement('select');
    for (const p of ['left', 'center', 'right']) {
      const o = document.createElement('option');
      o.value = p; o.textContent = p;
      if ((c.defaultPosition || 'center') === p) o.selected = true;
      pos.appendChild(o);
    }
    pos.addEventListener('change', () => { c.defaultPosition = pos.value; markDirty(); });

    const del = el('button', 'icon-btn', '✕');
    del.title = '删除角色';
    del.addEventListener('click', () => {
      if (!confirm(`确认删除角色「${cid}」？引用该角色的指令不会被自动清理。`)) return;
      delete doc.characters[cid];
      markDirty();
      renderDatalists();
      renderCharModal();
    });

    row.append(idEl, name, color, pos, del);
    box.appendChild(row);
  }
  if (Object.keys(doc.characters).length === 0) {
    const p = el('p', null, '暂无角色，点击下方按钮新增。');
    p.style.color = 'var(--text-dim)';
    box.appendChild(p);
  }
}

function addCharacter() {
  const id = prompt('角色 id（英文/数字/下划线，对应 assets/characters/<id>/ 目录）：');
  if (!id) return;
  if (!/^[A-Za-z0-9_\-]+$/.test(id)) { alert('角色 id 只能包含英文、数字、下划线、连字符。'); return; }
  if (doc.characters[id]) { alert('角色已存在：' + id); return; }
  doc.characters[id] = { displayName: id, color: '#ffffff', defaultPosition: 'center' };
  markDirty();
  renderDatalists();
  renderCharModal();
}

/* ============================================================
 * 校验
 * ============================================================ */

let lastIssues = [];

function validate(showPanel = true) {
  const issues = [];
  const charIds = new Set(Object.keys(doc.characters));
  const sceneIds = new Set(sceneOrder);

  for (const sid of sceneOrder) {
    const cmds = doc.scenes[sid].commands || [];
    cmds.forEach((c, i) => {
      const loc = `${sid}[${i + 1}]`;
      const def = CMD_DEFS[c.cmd];
      if (!def) {
        issues.push({ scene: sid, index: i, loc, msg: `未知指令类型「${c.cmd}」` });
        return;
      }
      for (const f of def.fields || []) {
        if (f.required && (c[f.key] === undefined || c[f.key] === '')) {
          issues.push({ scene: sid, index: i, loc, msg: `${def.label} 缺少必填参数「${f.name}(${f.key})」` });
        }
        if (f.kind === 'char' && c[f.key] && !charIds.has(c[f.key])) {
          issues.push({ scene: sid, index: i, loc, msg: `引用了不存在的角色「${c[f.key]}」` });
        }
        if (f.kind === 'scene' && c[f.key] && !sceneIds.has(c[f.key])) {
          issues.push({ scene: sid, index: i, loc, msg: `跳转到不存在的场景「${c[f.key]}」` });
        }
      }
      if (c.cmd === 'choice') {
        if (!Array.isArray(c.options) || c.options.length === 0) {
          issues.push({ scene: sid, index: i, loc, msg: '选择支没有任何选项' });
        } else {
          c.options.forEach((o, oi) => {
            if (!o.text) issues.push({ scene: sid, index: i, loc, msg: `选项 ${oi + 1} 缺少文本` });
            if (!o.target) issues.push({ scene: sid, index: i, loc, msg: `选项 ${oi + 1} 缺少目标场景` });
            else if (!sceneIds.has(o.target)) issues.push({ scene: sid, index: i, loc, msg: `选项 ${oi + 1} 指向不存在的场景「${o.target}」` });
          });
        }
      }
    });
  }

  lastIssues = issues;
  if (showPanel) showIssues(issues);
  return issues.length === 0;
}

function showIssues(issues) {
  const panel = $('issues-panel');
  const list = $('issues-list');
  list.textContent = '';
  if (issues.length === 0) {
    $('issues-title').textContent = '校验通过';
    list.appendChild(el('li', 'ok', '✓ 未发现问题'));
  } else {
    $('issues-title').textContent = `校验发现 ${issues.length} 个问题`;
    for (const it of issues) {
      const li = el('li');
      li.appendChild(el('span', 'loc', it.loc));
      li.appendChild(document.createTextNode(it.msg));
      li.addEventListener('click', () => jumpToIssue(it));
      list.appendChild(li);
    }
  }
  panel.classList.remove('hidden');
  markIssueCards();
}

function hideIssues() {
  $('issues-panel').classList.add('hidden');
  document.querySelectorAll('.cmd-card.issue').forEach((c) => c.classList.remove('issue'));
}

function markIssueCards() {
  document.querySelectorAll('.cmd-card.issue').forEach((c) => c.classList.remove('issue'));
  if (lastIssues.length === 0) return;
  const inScene = new Set(lastIssues.filter((i) => i.scene === currentScene).map((i) => i.index));
  document.querySelectorAll('#command-list .cmd-card').forEach((card) => {
    if (inScene.has(Number(card.dataset.index))) card.classList.add('issue');
  });
}

function jumpToIssue(it) {
  if (it.scene !== currentScene) {
    currentScene = it.scene;
    renderSidebar();
    renderScene();
  }
  markIssueCards();
  const card = document.querySelector(`#command-list .cmd-card[data-index="${it.index}"]`);
  if (card) {
    card.scrollIntoView({ behavior: 'smooth', block: 'center' });
    card.classList.add('issue');
  }
}

/* ============================================================
 * 事件绑定与初始化
 * ============================================================ */

$('btn-open').addEventListener('click', openFile);
$('btn-new').addEventListener('click', () => {
  if (dirty && !confirm('当前有未保存的修改，新建将丢弃它们，继续？')) return;
  loadDoc(newDoc(), '未命名.json');
});
$('btn-save').addEventListener('click', saveFile);
$('btn-saveas').addEventListener('click', saveAsFile);
$('btn-validate').addEventListener('click', () => { if (doc) validate(true); });
$('btn-close-issues').addEventListener('click', hideIssues);
$('btn-characters').addEventListener('click', () => {
  if (!doc) return;
  renderCharModal();
  $('char-modal').classList.remove('hidden');
});
$('btn-close-chars').addEventListener('click', () => {
  $('char-modal').classList.add('hidden');
  renderDatalists();
  renderScene(); // 角色可能变动，刷新卡片里的引用提示
});
$('char-modal').addEventListener('click', (e) => {
  if (e.target === $('char-modal')) $('btn-close-chars').click();
});
$('btn-add-char').addEventListener('click', addCharacter);
$('btn-add-scene').addEventListener('click', () => { if (doc) addScene(); });
$('btn-rename-scene').addEventListener('click', renameScene);
$('btn-del-scene').addEventListener('click', deleteScene);
$('btn-add-cmd').addEventListener('click', () => {
  if (!currentScene) return;
  insertCommand(cmdsOfCurrent().length, $('add-type').value);
});

// 添加指令类型下拉
(() => {
  const sel = $('add-type');
  for (const t of CMD_ORDER) {
    const o = document.createElement('option');
    o.value = t;
    o.textContent = CMD_DEFS[t].label;
    sel.appendChild(o);
  }
})();

// Ctrl+S 保存
document.addEventListener('keydown', (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
    e.preventDefault();
    saveFile();
  }
});

// 离开页面前提醒未保存
window.addEventListener('beforeunload', (e) => {
  if (dirty) { e.preventDefault(); e.returnValue = ''; }
});

// 场景切换后重新标红当前场景的问题卡片（renderScene 的包装）
const origRenderScene = renderScene;
renderScene = function () {
  origRenderScene();
  if (lastIssues.length) markIssueCards();
};
