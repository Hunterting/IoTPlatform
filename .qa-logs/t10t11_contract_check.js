/**
 * T10 / T11 安圣前端契约核查脚本（只读，不修改任何业务文件）。
 *
 * 设计：从后端 C# 控制器 / DTO（权威契约）与前端 TS 类型 / API / 页面交叉比对，
 * 做可机读的静态断言。退出码 0 = 全绿，非 0 = 有红项。
 *
 * 覆盖六类强制断言（任务书要求）：
 *   ① 端点 URL 一致性（4 T10 + 8 T11）
 *   ② camelCase 字段映射
 *   ③ 枚举按字符串出网（铁律④）
 *   ④ 信封消费（铁律②：认 code 不认 success）
 *   ⑤ 异常语义（T10 有 409+concurrencyConflict，T11 全 200 无 409）
 *   ⑥ 权限一致性（前端 hasPermission 与控制器 [PermissionAuthorize] 对齐）
 *
 * 另含：路由/菜单回归锁、伪命令不复现检查。
 */
const fs = require('fs');
const path = require('path');

const ROOT = 'H:/IoTPlatform';

function read(p) {
  try {
    return fs.readFileSync(path.join(ROOT, p), 'utf8');
  } catch (e) {
    return null;
  }
}

// ── 权威源 ──────────────────────────────────────────────────────
const beSchedCtrl = read('Controllers/AnShengScheduleController.cs');
const beEnergyCtrl = read('Controllers/AnShengEnergyController.cs');
const beReq = read('DTOs/Requests/AnShengRequests.cs');
const beRes = read('DTOs/Responses/AnShengResponses.cs');
const feTypes = read('Web/src/app/services/api/types/ansheng.types.ts');
const feApi = read('Web/src/app/services/api/anshengApi.ts');
const feSchedPage = read('Web/src/app/pages/ScheduleEditorPage.tsx');
const feEnergyPage = read('Web/src/app/pages/EnergyStatisticsPage.tsx');
const feSidebar = read('Web/src/app/components/Sidebar.tsx');
const feApp = read('Web/src/app/App.tsx');

let failures = 0;

function assert(desc, ok) {
  if (!ok) failures++;
  console.log(`  ${ok ? '✓' : '✗'} ${desc}`);
}

function missingFile(name, src) {
  if (src === null) {
    assert(`源文件可读: ${name}`, false);
    return true;
  }
  return false;
}

[['ScheduleController', beSchedCtrl], ['EnergyController', beEnergyCtrl],
 ['AnShengRequests', beReq], ['AnShengResponses', beRes],
 ['ansheng.types', feTypes], ['anshengApi', feApi],
 ['ScheduleEditorPage', feSchedPage], ['EnergyStatisticsPage', feEnergyPage],
 ['Sidebar', feSidebar], ['App', feApp]].forEach(([n, s]) => missingFile(n, s));

/** 在非注释行中查找正则（跳过 // /* * 行）。 */
function nonCommentHas(src, re) {
  if (!src) return false;
  for (const line of src.split('\n')) {
    const t = line.trim();
    if (t.startsWith('//') || t.startsWith('*') || t.startsWith('/*')) continue;
    if (re.test(t)) return true;
  }
  return false;
}

/** 在 src 中 [from, from+window] 窗口内查找子串。 */
function within(src, anchor, needle, windowSize = 240) {
  const i = src.indexOf(anchor);
  if (i < 0) return false;
  const slice = src.slice(i, i + windowSize);
  return slice.includes(needle);
}

// ════════════════════════════════════════════════════════════════
console.log('════ ① 端点 URL 一致性（后端路由 ⇄ 前端 api 方法）════');

// method, URL 模板（前端字面量），所属控制器，后端路由属性
const ENDPOINTS = [
  // ── T10 定时任务 ──
  ['getTimeTasks', '/ansheng/${deviceId}/time-tasks', beSchedCtrl, '[HttpGet("{deviceId:long}/time-tasks")]', 'GET  api/v1/ansheng/{deviceId}/time-tasks'],
  ['setTimeTasks', '/ansheng/${deviceId}/time-tasks', beSchedCtrl, '[HttpPost("{deviceId:long}/time-tasks")]', 'POST api/v1/ansheng/{deviceId}/time-tasks'],
  ['getSlotTimeTasks', '/ansheng/${deviceId}/time-tasks/${slotNum}', beSchedCtrl, '[HttpGet("{deviceId:long}/time-tasks/{slotNum:int}")]', 'GET  api/v1/ansheng/{deviceId}/time-tasks/{slotNum}'],
  ['setSlotTimeTasks', '/ansheng/${deviceId}/time-tasks/${slotNum}', beSchedCtrl, '[HttpPost("{deviceId:long}/time-tasks/{slotNum:int}")]', 'POST api/v1/ansheng/{deviceId}/time-tasks/{slotNum}'],
  // ── T11 电量计 ──
  ['requestEnergyRealtime', '/ansheng/${deviceId}/energy/realtime', beEnergyCtrl, '[HttpPost("{deviceId:long}/energy/realtime")]', 'POST api/v1/ansheng/{deviceId}/energy/realtime'],
  ['refreshEnergyStatistics', '/ansheng/${deviceId}/energy/statistics/refresh', beEnergyCtrl, '[HttpPost("{deviceId:long}/energy/statistics/refresh")]', 'POST api/v1/ansheng/{deviceId}/energy/statistics/refresh'],
  ['clearEnergyStatistics', '/ansheng/${deviceId}/energy/statistics/clear', beEnergyCtrl, '[HttpPost("{deviceId:long}/energy/statistics/clear")]', 'POST api/v1/ansheng/{deviceId}/energy/statistics/clear'],
  ['getEnergyStatistics', '/ansheng/${deviceId}/energy/statistics', beEnergyCtrl, '[HttpGet("{deviceId:long}/energy/statistics")]', 'GET  api/v1/ansheng/{deviceId}/energy/statistics'],
  ['getCalParams', '/ansheng/${deviceId}/energy/cal-params', beEnergyCtrl, '[HttpGet("{deviceId:long}/energy/cal-params")]', 'GET  api/v1/ansheng/{deviceId}/energy/cal-params'],
  ['setCalParams', '/ansheng/${deviceId}/energy/cal-params', beEnergyCtrl, '[HttpPost("{deviceId:long}/energy/cal-params")]', 'POST api/v1/ansheng/{deviceId}/energy/cal-params'],
  ['resetCalParams', '/ansheng/${deviceId}/energy/cal-params/reset', beEnergyCtrl, '[HttpPost("{deviceId:long}/energy/cal-params/reset")]', 'POST api/v1/ansheng/{deviceId}/energy/cal-params/reset'],
  ['autoCalParams', '/ansheng/${deviceId}/energy/cal-params/auto', beEnergyCtrl, '[HttpPost("{deviceId:long}/energy/cal-params/auto")]', 'POST api/v1/ansheng/{deviceId}/energy/cal-params/auto'],
];

for (const [method, url, ctrl, routeAttr, backend] of ENDPOINTS) {
  const feDeclared = feApi ? feApi.includes(`${method}: async`) : false;
  const tmpl = '`' + url + '`';
  const feHasUrl = feApi ? feApi.includes(tmpl) : false;
  const beHasRoute = ctrl ? ctrl.includes(routeAttr) : false;
  const ok = feDeclared && feHasUrl && beHasRoute;
  assert(`${method.padEnd(22)} → ${url}   [${backend}]` + (ok ? '' : `  (feDeclared=${feDeclared} feUrl=${feHasUrl} beRoute=${beHasRoute})`), ok);
}

// ════════════════════════════════════════════════════════════════
console.log('\n════ ② camelCase 字段映射（前端类型层）════');
const CAMEL_FIELDS = [
  'taskKind', 'rowVersion', 'slotNum', 'granularity', 'periodKey',
  'kwh', 'rl', 'calParams', 'concurrencyConflict', 'rejectReason',
  'syncedAt', 'isStale',
];
for (const f of CAMEL_FIELDS) {
  const pascal = f.charAt(0).toUpperCase() + f.slice(1);
  const hasCamel = (feTypes || '').includes(`${f}:`) || (feTypes || '').includes(`${f}?:`);
  const noPascalProp = !(feTypes || '').includes(`${pascal}:`);
  const ok = hasCamel && noPascalProp;
  assert(`camelCase 字段 ${f}（无误用 ${pascal}: 属性）`, ok);
  if (!hasCamel) console.log(`      未在 ansheng.types.ts 找到声明 ${f}`);
  if (!noPascalProp) console.log(`      仍残留 PascalCase 属性 ${pascal}:`);
}

// ════════════════════════════════════════════════════════════════
console.log('\n════ ③ 枚举按字符串出网（铁律④）════');
assert('AnShengTimeTaskKind = \'Normal\' | \'Loop\'',
  /export type AnShengTimeTaskKind =[\s\S]*?'Normal'[\s\S]*?'Loop'/.test(feTypes || ''));
assert('AnShengEmGranularity = \'Total\'|\'HourSum\'|\'Hour\'|\'Day\'|\'Month\'',
  /export type AnShengEmGranularity =[\s\S]*?'Total'[\s\S]*?'HourSum'[\s\S]*?'Hour'[\s\S]*?'Day'[\s\S]*?'Month'/.test(feTypes || ''));
assert('rejectReason 为字符串联合（含 RejectedByKind / RejectedByConfirm）',
  /export type AnShengCommandRejectReason =[\s\S]*?'RejectedByKind'/.test(feTypes || '') &&
  /'RejectedByConfirm'/.test(feTypes || ''));
assert('无整型枚举分支（不出现 0|1 映射的 TaskKind/EmGranularity 常量）',
  !/AnShengTimeTaskKind\s*=\s*0|AnShengEmGranularity\s*=\s*0/.test(feTypes || ''));

// ════════════════════════════════════════════════════════════════
console.log('\n════ ④ 信封消费（铁律②：认 code 不认 success）════');
assert('ScheduleEditorPage 用 response.data.code 判定',
  /response\.data\.code/.test(feSchedPage || ''));
assert('ScheduleEditorPage 未读 response.data.success',
  !/response\.data\.success/.test(feSchedPage || ''));
assert('EnergyStatisticsPage 用 response.data.code 判定',
  /response\.data\.code/.test(feEnergyPage || ''));
assert('EnergyStatisticsPage 未读 response.data.success',
  !/response\.data\.success/.test(feEnergyPage || ''));
assert('两页均把「受理」判定建立在 data.accepted / code=200',
  /result\?\.accepted|Boolean\(result\?\.accepted\)/.test(feSchedPage || '') &&
  /result\?\.accepted|Boolean\(result\?\.accepted\)/.test(feEnergyPage || ''));

// ════════════════════════════════════════════════════════════════
console.log('\n════ ⑤ 异常语义（T10 有 409+concurrencyConflict / T11 全 200 无 409）════');
assert('T10 页定义 CONFLICT_STATUS = 409',
  (feSchedPage || '').includes('CONFLICT_STATUS') && (feSchedPage || '').includes('409'));
assert('T10 页侦测 data.concurrencyConflict（isConcurrencyConflict）',
  /concurrencyConflict/.test(feSchedPage || '') && (feSchedPage || '').includes('isConcurrencyConflict'));
assert('T10 页抽取 HTTP 状态（extractHttpStatus 双保险）',
  (feSchedPage || '').includes('extractHttpStatus'));
assert('T11 页无 409 并发冲突分支（CONFLICT_STATUS / concurrencyConflict / extractHttpStatus 均不应出现）',
  !(feEnergyPage || '').includes('CONFLICT_STATUS') &&
  !(feEnergyPage || '').includes('concurrencyConflict') &&
  !(feEnergyPage || '').includes('isConcurrencyConflict') &&
  !(feEnergyPage || '').includes('extractHttpStatus'));
assert('后端 T10 控制器显式 StatusCode(409)',
  (beSchedCtrl || '').includes('StatusCode(409'));
assert('后端 T11 控制器无 StatusCode(409)（全 200）',
  !(beEnergyCtrl || '').includes('StatusCode(409)'));

// ════════════════════════════════════════════════════════════════
console.log('\n════ ⑥ 权限一致性（前端 hasPermission ⇄ 控制器 [PermissionAuthorize]）════');
// T10 页面门控口径
assert('T10 页：整页门控 VIEW_DEVICES（canView）',
  (feSchedPage || '').includes('canView = hasPermission(PERMISSIONS.VIEW_DEVICES)'));
assert('T10 页：写动作门控 SEND_DEVICE_COMMANDS（canSend）',
  (feSchedPage || '').includes('canSend = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS)'));
assert('T10 页：无权限时整页返回只读占位（!canView）',
  (feSchedPage || '').includes('!canView'));
assert('T10 页：写按钮 disabled 而非隐藏（!canSend）',
  (feSchedPage || '').includes('!canSend'));
assert('T10 页：缺权限给出只读提示',
  (feSchedPage || '').includes('只读模式'));

// T11 页面门控口径
assert('T11 页：整页门控 VIEW_DEVICES（canView）',
  (feEnergyPage || '').includes('canView = hasPermission(PERMISSIONS.VIEW_DEVICES)'));
assert('T11 页：写动作门控 SEND_DEVICE_COMMANDS（canSend）',
  (feEnergyPage || '').includes('canSend = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS)'));
assert('T11 页：无权限时整页返回只读占位（!canView）',
  (feEnergyPage || '').includes('!canView'));
assert('T11 页：写按钮 disabled 而非隐藏（!canSend）',
  (feEnergyPage || '').includes('!canSend'));
assert('T11 页：缺权限给出只读提示',
  (feEnergyPage || '').includes('只读模式'));

// 关键修正点：getCalParams（读校准参数）是设备命令，控制器声明 SEND_DEVICE_COMMANDS。
// 任务书草稿曾误称其走 VIEW_DEVICES；以控制器为准，前端应 gate 在 canSend。
assert('后端：GetCalParams 路由紧跟 [PermissionAuthorize(SEND_DEVICE_COMMANDS)]',
  within(beEnergyCtrl || '', '[HttpGet("{deviceId:long}/energy/cal-params")]', 'SEND_DEVICE_COMMANDS'));
assert('前端：handleReadCalParams 在 !canSend 守卫之后才调用 getCalParams',
  (() => {
    const src = feEnergyPage || '';
    const h = src.indexOf('const handleReadCalParams');
    const g = src.indexOf('!canSend', h);
    const c = src.indexOf('anshengApi.getCalParams', h);
    return h >= 0 && g > h && g < c;
  })());

// 文档一致性锁（验收后修正）：头部注释曾误称 cal-params GET 走 VIEW_DEVICES；
// 以控制器为准，cal-params GET 归 SEND_DEVICE_COMMANDS。此断言仅读注释文本，
// 防止该修正被静默回退为错误表述（纯文档改动，不影响编译/契约）。
{
  const HEADER = (feEnergyPage || '').split('\n').slice(0, 26).join('\n');
  const correctClaim = /cal-params 的 GET[\s\S]{0,40}SEND_DEVICE_COMMANDS/.test(HEADER);
  const wrongClaim = /cal-params[\s\S]{0,60}VIEW_DEVICES/.test(HEADER) &&
    !/SEND_DEVICE_COMMANDS[\s\S]{0,60}cal-params/.test(HEADER);
  assert('注释不声称 cal-params GET 走 VIEW_DEVICES（已修正为 SEND_DEVICE_COMMANDS）',
    correctClaim && !wrongClaim);
}

// ════════════════════════════════════════════════════════════════
console.log('\n════ ⑦ 命令日志（两页均落地下发动作）════');
assert('T10 页含命令日志 appendLog + 日志区',
  (feSchedPage || '').includes('appendLog') && (feSchedPage || '').includes('命令日志'));
assert('T11 页含命令日志 appendLog + 日志区',
  (feEnergyPage || '').includes('appendLog') && (feEnergyPage || '').includes('命令日志'));

// ════════════════════════════════════════════════════════════════
console.log('\n════ ⑧ 路由 / 菜单回归锁════');
assert('Sidebar 含 schedule-editor 菜单项',
  (feSidebar || '').includes("id: 'schedule-editor'"));
assert('Sidebar 含 energy-statistics 菜单项',
  (feSidebar || '').includes("id: 'energy-statistics'"));
assert('App 路由 schedule-editor → ScheduleEditorPage',
  (feApp || '').includes("case 'schedule-editor':") && (feApp || '').includes('ScheduleEditorPage'));
assert('App 路由 energy-statistics → EnergyStatisticsPage',
  (feApp || '').includes("case 'energy-statistics':") && (feApp || '').includes('EnergyStatisticsPage'));

// ════════════════════════════════════════════════════════════════
console.log('\n════ ⑨ 伪命令不复现（setSwitch/getSwitchStatus/setSwitchConfig/getSwitchConfig 仅可出现在注释）════');
const PSEUDO = /setSwitch\b|getSwitchStatus|setSwitchConfig|getSwitchConfig/;
const pseudoTargets = {
  'anshengApi.ts': feApi, 'ansheng.types.ts': feTypes,
  'ScheduleEditorPage.tsx': feSchedPage, 'EnergyStatisticsPage.tsx': feEnergyPage,
};
let pseudoHit = false;
for (const [name, src] of Object.entries(pseudoTargets)) {
  if (nonCommentHas(src || '', PSEUDO)) {
    pseudoHit = true;
    console.log(`      命中非注释行 [${name}]`);
  }
}
assert('无伪命令活跃调用（仅允许注释中出现）', !pseudoHit);

// ════════════════════════════════════════════════════════════════
console.log(`\n════ 汇总 ════\n  失败项: ${failures}`);
process.exit(failures === 0 ? 0 : 1);
