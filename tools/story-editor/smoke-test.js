// 冒烟测试：用 DOM 桩加载 editor.js，验证载入、校验、序列化逻辑
const fs = require('fs');
const path = require('path');
const vm = require('vm');

function makeEl(tag) {
  const el = {
    tagName: tag, children: [], style: {}, dataset: {}, value: '', textContent: '',
    classList: {
      _s: new Set(),
      add(c) { this._s.add(c); }, remove(c) { this._s.delete(c); },
      toggle(c, f) { f ? this._s.add(c) : this._s.delete(c); },
      contains(c) { return this._s.has(c); },
    },
    addEventListener() {}, appendChild(c) { this.children.push(c); },
    append(...cs) { this.children.push(...cs); },
    setAttribute() {}, click() {},
  };
  return el;
}

const elCache = {};
const documentStub = {
  getElementById(id) { return elCache[id] || (elCache[id] = makeEl('div#' + id)); },
  createElement: makeEl,
  createTextNode(t) { return { text: t }; },
  addEventListener() {},
  querySelectorAll() { return []; },
  querySelector() { return null; },
};
const sandbox = {
  document: documentStub,
  window: { addEventListener() {} },
  alert(m) { throw new Error('alert: ' + m); },
  confirm: () => true, prompt: () => null,
  console, URL, Blob: class {},
  MAIN_JSON: fs.readFileSync(path.join(__dirname, '..', '..', 'story', 'main.json'), 'utf8'),
};
vm.createContext(sandbox);

const src = fs.readFileSync(path.join(__dirname, 'editor.js'), 'utf8');
const test = `
;(function runTests() {
  const assert = (cond, msg) => { if (!cond) throw new Error('断言失败: ' + msg); };

  // 1. 加载项目自带的 main.json
  const main = JSON.parse(MAIN_JSON);
  loadDoc(main, 'main.json');
  assert(sceneOrder.length === 4, '应解析出 4 个场景, 实际 ' + sceneOrder.length);
  assert(currentScene === 'start', '当前场景应为 start');
  assert(validate(false) === true, '自带 main.json 应通过校验, 问题: ' + JSON.stringify(lastIssues));

  // 2. 序列化应保持场景顺序与结构
  const out = JSON.parse(serializeDoc());
  assert(JSON.stringify(Object.keys(out.scenes)) === JSON.stringify(['start','sweet_path','yandere_path','ending_intro']), '场景顺序不一致');
  assert(out.characters.shiori.displayName === '千织', '角色数据丢失');

  // 3. 构造问题脚本应被校验捕获
  loadDoc({
    characters: { a: { displayName: 'A', color: '#fff', defaultPosition: 'center' } },
    scenes: {
      s1: { commands: [
        { cmd: 'say', character: 'ghost', text: 'hi' },
        { cmd: 'jump', target: 'nowhere' },
        { cmd: 'choice', options: [{ text: '', target: 's1' }, { text: 'ok' }] },
        { cmd: 'bogus_cmd' },
        { cmd: 'bgm' },
      ]},
      s2: { commands: [] },
    },
  }, 'test.json');
  validate(false);
  const msgs = lastIssues.map(i => i.msg).join('\\n');
  assert(msgs.includes('ghost'), '应捕获不存在的角色');
  assert(msgs.includes('nowhere'), '应捕获不存在的跳转场景');
  assert(msgs.includes('缺少文本'), '应捕获选项缺文本');
  assert(msgs.includes('缺少目标场景'), '应捕获选项缺目标');
  assert(msgs.includes('未知指令类型'), '应捕获未知指令');
  assert(msgs.includes('必填参数'), '应捕获缺失必填参数');
  assert(lastIssues.length === 6, '应恰好 6 个问题, 实际 ' + lastIssues.length + ': ' + msgs);

  // 4. 指令操作
  currentScene = 's2';
  insertCommand(0, 'say');
  assert(cmdsOfCurrent().length === 1 && cmdsOfCurrent()[0].cmd === 'say', '插入指令失败');
  duplicateCommand(0);
  moveCommand(1, -1);
  assert(cmdsOfCurrent().length === 2, '复制/移动失败');
  deleteCommand(0);
  assert(cmdsOfCurrent().length === 1, '删除指令失败');

  // 5. 场景重命名应同步引用
  loadDoc({
    characters: {},
    scenes: {
      a: { commands: [{ cmd: 'jump', target: 'b' }, { cmd: 'choice', options: [{ text: 'x', target: 'b' }] }] },
      b: { commands: [] },
    },
  }, 'test2.json');
  currentScene = 'b';
  // renameScene 使用 prompt，这里直接模拟其内部逻辑不可行；改测 setCmdArg 的 undefined 删除语义
  const c = { cmd: 'show', character: 'x', emotion: 'smile' };
  setCmdArg(c, 'emotion', undefined);
  assert(!('emotion' in c), 'setCmdArg(undefined) 应删除键');

  console.log('SMOKE_TEST_ALL_PASSED');
})();
`;
vm.runInContext(src + test, sandbox, { filename: 'editor.js' });
