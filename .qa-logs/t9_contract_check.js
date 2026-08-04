/**
 * T9 前端契约核查脚本（只读，不修改任何业务文件）。
 *
 * 做三件事：
 *  1. 从后端 C# DTO 中提取公共属性名，camelCase 化，作为「权威字段集」。
 *  2. 从前端 TS interface 中提取字段名，作为「消费字段集」。
 *  3. 比对：前端缺字段 / 前端多字段（拼写错） / 端点 URL 是否匹配后端路由。
 */
const fs = require('fs');
const path = require('path');

const ROOT = 'H:/IoTPlatform';

function read(p) {
  return fs.readFileSync(path.join(ROOT, p), 'utf8');
}

/** 从 C# 源中抽取指定类的公共属性名（camelCase 后返回）。 */
function csharpProps(source, className) {
  const clsIdx = source.indexOf(`class ${className}`);
  if (clsIdx < 0) return null;
  // 取该类到下一个 "\npublic class" / "\npublic enum" 之间的片段
  const rest = source.slice(clsIdx);
  const nextIdx = rest.slice(10).search(/\npublic (class|enum|record)/);
  const body = nextIdx < 0 ? rest : rest.slice(0, nextIdx + 10);
  const props = [];
  const re = /public\s+[^\s]+[^\n]*?\s+(\w+)\s*\{\s*get;/g;
  let m;
  while ((m = re.exec(body)) !== null) {
    const name = m[1];
    props.push(name[0].toLowerCase() + name.slice(1));
  }
  return props;
}

/** 从 TS 源中抽取指定 interface 的字段名。 */
function tsFields(source, ifaceName) {
  const idx = source.indexOf(`interface ${ifaceName} {`);
  if (idx < 0) return null;
  const body = source.slice(idx, source.indexOf('\n}', idx));
  const fields = [];
  // 跳过注释行，匹配 `name?: type;` / `name: type;`
  body.split('\n').slice(1).forEach(line => {
    const t = line.trim();
    if (t.startsWith('//') || t.startsWith('/*') || t.startsWith('*')) return;
    const m = /^(\w+)\??\s*:/.exec(t);
    if (m) fields.push(m[1]);
  });
  return fields;
}

const beReq = read('DTOs/Requests/AnShengRequests.cs');
const beRes = read('DTOs/Responses/AnShengResponses.cs');
const feTypes = read('Web/src/app/services/api/types/ansheng.types.ts');
const feApi = read('Web/src/app/services/api/anshengApi.ts');
const fePage = read('Web/src/app/pages/SwitchControlPage.tsx');

const PAIRS = [
  ['AnShengActionRequest', beReq, 'AnShengSwitchActionRequest'],
  ['AnShengActionsRequest', beReq, 'AnShengSwitchActionsRequest'],
  ['AnShengStartDelayTaskRequest', beReq, 'AnShengStartDelayTaskRequest'],
  ['AnShengStopDelayTaskRequest', beReq, 'AnShengStopDelayTaskRequest'],
  ['AnShengSwitchResultDto', beRes, 'AnShengSwitchResultDto'],
  ['AnShengDelayTaskDto', beRes, 'AnShengDelayTaskDto'],
  ['AnShengDelayTaskResultDto', beRes, 'AnShengDelayTaskResultDto'],
  ['AnShengDeviceProfileDto', beRes, 'AnShengDeviceProfileDto'],
];

let failures = 0;
console.log('════ 1. DTO 字段比对（后端 camelCase ⇄ 前端 interface）════');
for (const [csName, csSrc, tsName] of PAIRS) {
  const be = csharpProps(csSrc, csName);
  const fe = tsFields(feTypes, tsName);
  if (!be) { console.log(`  ✗ 后端类未找到: ${csName}`); failures++; continue; }
  if (!fe) { console.log(`  ✗ 前端接口未找到: ${tsName}`); failures++; continue; }
  const missing = be.filter(f => !fe.includes(f));
  const extra = fe.filter(f => !be.includes(f));
  const ok = missing.length === 0 && extra.length === 0;
  if (!ok) failures++;
  console.log(`  ${ok ? '✓' : '✗'} ${csName} ⇄ ${tsName}`);
  console.log(`      后端(${be.length}): ${be.join(', ')}`);
  console.log(`      前端(${fe.length}): ${fe.join(', ')}`);
  if (missing.length) console.log(`      ⚠ 前端缺少: ${missing.join(', ')}`);
  if (extra.length) console.log(`      ⚠ 前端多出: ${extra.join(', ')}`);
}

console.log('\n════ 2. 端点 URL 比对（后端路由 ⇄ 前端 api 方法）════');
const EXPECTED = [
  ['getProfile', '/ansheng/${deviceId}/profile', 'GET  api/v1/ansheng/{deviceId}/profile'],
  ['switchAction', '/ansheng/${deviceId}/action', 'POST api/v1/ansheng/{deviceId}/action'],
  ['switchActions', '/ansheng/${deviceId}/actions', 'POST api/v1/ansheng/{deviceId}/actions'],
  ['getDelayTasks', '/ansheng/${deviceId}/delay-tasks', 'GET  api/v1/ansheng/{deviceId}/delay-tasks'],
  ['startDelayTask', '/ansheng/${deviceId}/delay-tasks/start', 'POST api/v1/ansheng/{deviceId}/delay-tasks/start'],
  ['stopDelayTask', '/ansheng/${deviceId}/delay-tasks/stop', 'POST api/v1/ansheng/{deviceId}/delay-tasks/stop'],
];
for (const [method, url, backend] of EXPECTED) {
  const declared = feApi.includes(`${method}: async`);
  const hasUrl = feApi.includes('`' + url + '`');
  const ok = declared && hasUrl;
  if (!ok) failures++;
  console.log(`  ${ok ? '✓' : '✗'} ${method.padEnd(15)} → ${url}   [后端 ${backend}]`);
}

console.log('\n════ 3. 枚举 / 信封 / 伪命令 静态断言 ════');
const assertions = [
  ['rejectReason 为字符串联合类型（含 RejectedByKind）',
    /export type AnShengCommandRejectReason =[\s\S]*?'RejectedByKind'/.test(feTypes)],
  ['页面按字符串键映射拒绝原因（REJECT_REASON_TEXT.RejectedByKind）',
    /REJECT_REASON_TEXT[\s\S]{0,200}RejectedByKind:/.test(fePage)],
  ['页面用 response.data.code 判定信封',
    /response\.data\.code/.test(fePage)],
  ['页面用 result?.accepted 判定受理',
    /result\?\.accepted|Boolean\(result\?\.accepted\)/.test(fePage)],
  ['页面未使用不存在的 response.data.success',
    !/response\.data\.success/.test(fePage)],
  ['无伪命令调用（setSwitch/getSwitchStatus/setSwitchConfig/getSwitchConfig 仅出现在注释）',
    (() => {
      const files = { fePage, feApi, feTypes };
      for (const [name, src] of Object.entries(files)) {
        for (const line of src.split('\n')) {
          const t = line.trim();
          if (t.startsWith('//') || t.startsWith('*') || t.startsWith('/*')) continue;
          if (/setSwitch|getSwitchStatus|setSwitchConfig|getSwitchConfig/.test(t)) {
            console.log(`      命中非注释行 [${name}]: ${t}`);
            return false;
          }
        }
      }
      return true;
    })()],
  ['slotNums 类型为 number[]（非字符串）',
    /slotNums:\s*number\[\]/.test(feTypes)],
  ['批量下发前做了升序去重的整数数组构造',
    /Array\.from\(new Set\(selectedSlots\)\)\.sort/.test(fePage)],
  ['sAction / eAction 小写开头',
    /\bsAction:/.test(feTypes) && /\beAction:/.test(feTypes) && !/\bSAction:/.test(feTypes)],
  ['插槽下标约定 slots[slotNum - 1]',
    /slots\[slotNum - 1\]/.test(fePage)],
  ['初始通断态取自 getProfile().data.slots',
    /anshengApi\.getProfile/.test(fePage) && /dto\.slots/.test(fePage)],
  ['写后延迟补刷（REFRESH_DELAY_MS = 1500）',
    /REFRESH_DELAY_MS\s*=\s*1500/.test(fePage)],
  ['兜底路数 DEFAULT_SLOT_COUNT = 4',
    /DEFAULT_SLOT_COUNT\s*=\s*4/.test(fePage)],
  ['页面门控 VIEW_DEVICES',
    /hasPermission\(PERMISSIONS\.VIEW_DEVICES\)/.test(fePage)],
  ['动作门控 SEND_DEVICE_COMMANDS',
    /hasPermission\(PERMISSIONS\.SEND_DEVICE_COMMANDS\)/.test(fePage)],
  ['缺权限时按钮 disabled（!canSend）',
    /disabled=\{!canSend/.test(fePage)],
  ['缺权限时给出只读提示',
    /只读模式/.test(fePage)],
];
for (const [desc, ok] of assertions) {
  if (!ok) failures++;
  console.log(`  ${ok ? '✓' : '✗'} ${desc}`);
}

// ── 第 2 轮回归：锁定 F1 / F2 两处修复，防止回退 ──────────────
console.log('\n════ 4. 回归断言（F1 / F2 修复锁定）════');
const feSidebar = read('Web/src/app/components/Sidebar.tsx');

/** 抽取 Sidebar 中 switch-control 菜单项对象片段。 */
function switchControlMenuBlock(src) {
  const idx = src.indexOf("id: 'switch-control'");
  if (idx < 0) return null;
  // 往前找到 '{'，往后找到配对的 '}'（菜单项是扁平对象，无嵌套花括号除 JSX 外）
  const start = src.lastIndexOf('{', idx);
  const end = src.indexOf('},', idx);
  return src.slice(start, end + 1);
}
const menuBlock = switchControlMenuBlock(feSidebar);

const regressions = [
  ['F1: switch-control 菜单项声明存在',
    menuBlock !== null],
  ['F1: 菜单可见性改为 VIEW_DEVICES',
    menuBlock !== null && /requiredPermission:\s*PERMISSIONS\.VIEW_DEVICES/.test(menuBlock)],
  ['F1: 菜单不再用 SEND_DEVICE_COMMANDS 过滤（只读用户可达）',
    menuBlock !== null && !/requiredPermission:\s*PERMISSIONS\.SEND_DEVICE_COMMANDS/.test(menuBlock)],
  ['F1: 与同级「安圣设备」菜单项权限一致',
    (() => {
      const idx = feSidebar.indexOf("id: 'ansheng-management'");
      if (idx < 0 || menuBlock === null) return false;
      const sibling = feSidebar.slice(feSidebar.lastIndexOf('{', idx), feSidebar.indexOf('},', idx) + 1);
      const perm = s => (/requiredPermission:\s*(PERMISSIONS\.\w+)/.exec(s) || [])[1];
      return perm(sibling) === perm(menuBlock);
    })()],
  ['F1: 页面自身仍保留 canSend 动作门控（只读 UI 未被删）',
    /canSend\s*=\s*hasPermission\(PERMISSIONS\.SEND_DEVICE_COMMANDS\)/.test(fePage)
      && /只读模式/.test(fePage)],
  ['F2: slotCount 用 max(slotNum) 而非 delayTasks.length',
    /delayTasks\.reduce\(\([^)]*\)\s*=>\s*Math\.max\([^)]*task\.slotNum\)/.test(fePage)],
  ['F2: slotCount 计算中不再出现 delayTasks.length',
    (() => {
      const m = /const slotCount = useMemo<number>\(\(\) => \{[\s\S]*?\}, \[/.exec(fePage);
      return m !== null && !/delayTasks\.length/.test(m[0]);
    })()],
  ['F2: slotCountKnown 只认 profile.slotAmount 与 slots 两个权威源',
    (() => {
      const m = /const slotCountKnown = useMemo<boolean>\(\(\) => \{[\s\S]*?\}, \[/.exec(fePage);
      return m !== null && !/delayTasks/.test(m[0])
        && /profile\?\.slotAmount/.test(m[0]) && /slots\.length/.test(m[0]);
    })()],
  ['F2: 手动路数逃生通道仍在（!slotCountKnown 时显示下拉框）',
    /!slotCountKnown/.test(fePage) && /SLOT_COUNT_OPTIONS\.map/.test(fePage)],
  ['F2: slotNumbers 仍由 slotCount 线性展开（1..slotCount）',
    /Array\.from\(\{ length: slotCount \}, \(_, i\) => i \+ 1\)/.test(fePage)],
];
for (const [desc, ok] of regressions) {
  if (!ok) failures++;
  console.log(`  ${ok ? '✓' : '✗'} ${desc}`);
}

// ── F2 行为级仿真：把 slotCount / slotCountKnown 的语义复刻一遍跑用例 ──
console.log('\n════ 5. F2 行为仿真（复刻修复后语义跑边界用例）════');
const DEFAULT_SLOT_COUNT = 4;
const calcKnown = (slotAmount, slots) => (slotAmount ?? 0) > 0 || slots.length > 0;
const calcCount = (slotAmount, slots, tasks, manual) => {
  const maxTaskSlot = tasks.reduce((m, t) => Math.max(m, t.slotNum), 0);
  const resolved = Math.max(slotAmount ?? 0, slots.length, maxTaskSlot);
  return resolved > 0 ? resolved : manual;
};
const CASES = [
  ['缺陷复现场景：未探测 + 无快照 + 仅插槽#6 有稀疏镜像',
    null, [], [{ slotNum: 6 }], DEFAULT_SLOT_COUNT, { count: 6, known: false }],
  ['档案给出 8 路，快照与镜像为空',
    8, [], [], DEFAULT_SLOT_COUNT, { count: 8, known: true }],
  ['无档案无镜像，纯兜底',
    null, [], [], DEFAULT_SLOT_COUNT, { count: 4, known: false }],
  ['快照 4 路 + 镜像最大插槽 6（取更大者，不漏渲染）',
    null, [0, 1, 0, 1], [{ slotNum: 6 }], DEFAULT_SLOT_COUNT, { count: 6, known: true }],
  ['档案 4 路但镜像出现插槽 6（异常数据也不漏渲染）',
    4, [], [{ slotNum: 2 }, { slotNum: 6 }], DEFAULT_SLOT_COUNT, { count: 6, known: true }],
  ['稀疏镜像不得吞掉手动逃生通道（known 必须为 false）',
    null, [], [{ slotNum: 3 }], DEFAULT_SLOT_COUNT, { count: 3, known: false }],
];
for (const [desc, slotAmount, slots, tasks, manual, expect] of CASES) {
  const count = calcCount(slotAmount, slots, tasks, manual);
  const known = calcKnown(slotAmount, slots);
  const ok = count === expect.count && known === expect.known;
  if (!ok) failures++;
  console.log(`  ${ok ? '✓' : '✗'} ${desc}`);
  console.log(`      期望 slotCount=${expect.count} known=${expect.known} | 实际 slotCount=${count} known=${known}`);
}

console.log(`\n════ 汇总 ════\n  失败项: ${failures}`);
process.exit(failures === 0 ? 0 : 1);
