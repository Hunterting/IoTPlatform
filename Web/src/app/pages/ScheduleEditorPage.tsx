// T10：安圣定时任务面板。
//
// 【消费的后端端点（T10 已交付并 QA 验收通过）】
//   GET  /ansheng/{deviceId}/time-tasks            读全部插槽定时任务镜像
//   POST /ansheng/{deviceId}/time-tasks            整表覆盖（需 confirm，未列出的插槽被清空）
//   GET  /ansheng/{deviceId}/time-tasks/{slotNum}  读单插槽定时任务镜像
//   POST /ansheng/{deviceId}/time-tasks/{slotNum}  设置单插槽定时任务（需 confirm）
//   （辅助）GET /ansheng/{deviceId}/profile        能力档案：插槽数量
//
// 【响应信封（铁律②）】ApiResponse<T> = { code, message, data, timestamp }。
//   业务失败 HTTP 状态恒为 200，靠 code=400 表达，机器可读原因在 data.rejectReason
//   （字符串枚举，如 "RejectedByKind" / "RejectedByConfirm"）。**绝不**读 data.success。
//
// 【唯一的 HTTP 非 200】乐观并发冲突：控制器显式 StatusCode(409, 信封体)，axios 会抛异常，
//   必须在 catch 里判定，见 extractHttpStatus / CONFLICT_STATUS 注释。
//
// 【枚举按字符串分支（铁律④）】taskKind 是 "Normal" / "Loop"，不是 0 / 1。

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  CalendarClock, Repeat, Clock, RefreshCw, Loader2,
  AlertCircle, CheckCircle, Cpu, Radio, Hash,
  Plus, Trash2, Save, Layers, ShieldAlert, Activity,
  Search, Info, Power, PowerOff, Shuffle, Upload,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';
import { anshengApi } from '@/app/services/api/anshengApi';
import { deviceApi } from '@/app/services/api/deviceApi';
import type {
  AnShengCommandRejectReason,
  AnShengSwitchAction,
  AnShengSlotTimeTaskSetDto,
  AnShengTimeTaskDto,
  AnShengTimeTaskItemRequest,
  AnShengLoopTimeTaskItemRequest,
  AnShengSlotTimeTaskSetRequest,
  AnShengTimeTaskResultDto,
  AnShengDeviceProfileDto,
} from '@/app/services/api/types/ansheng.types';
import type { DeviceDto } from '@/app/services/api/types/device.types';

// ── 常量 ─────────────────────────────────────────────────────────

/** ApiResponse 的成功码。业务失败为 400（HTTP 仍是 200）。 */
const OK_CODE = 200;

/**
 * 乐观并发冲突的 HTTP 状态码。
 *
 * 【T10 唯一的非 200】AnShengScheduleController 在 result.ConcurrencyConflict 时
 * 显式 StatusCode(409, ApiResponse.Fail(409, ..., result))，axios 因此抛异常。
 */
const CONFLICT_STATUS = 409;

/** 档案未给出插槽数量时的兜底路数（与 T9 SwitchControlPage 同口径）。 */
const DEFAULT_SLOT_COUNT = 4;

/** 可手动选择的插槽路数（仅在档案未给出 slotAmount 时暴露）。 */
const SLOT_COUNT_OPTIONS: number[] = [1, 2, 4, 6, 8, 12, 16];

/**
 * 写后刷新延迟（毫秒）。
 *
 * 【为什么要等】设备权威 + 异步刷新：setSlotTimeTasks / setTimeTasks 在命令出网即返回乐观镜像，
 * 真实镜像由后端写后回读（getTimeTasks 应答经 Router 钩子）在另起作用域里覆盖并 bump syncedAt。
 * 立即重查必然读到旧镜像，故延后一次补刷，把设备真值兜回来。
 */
const REFRESH_DELAY_MS = 1500;

/** 命令日志最大保留条数。 */
const MAX_LOG_ENTRIES = 50;

/** 单插槽允许编辑的最大任务条数（普通 / 循环各自独立计数）。 */
const MAX_TASKS_PER_GROUP = 16;

/** 拒绝原因 → 中文提示。键与后端 AnShengCommandRejectReason 枚举成员名一致。 */
const REJECT_REASON_TEXT: Record<AnShengCommandRejectReason, string> = {
  RejectedByKind: '设备品类不支持定时任务（仅开关类 Switch4G 放行，喇叭类被结构性拒绝）',
  RejectedByValidation: '参数校验不通过（含插槽编号越界、时分越界）',
  RejectedByFirmware: '设备固件版本过低，请先升级固件',
  RejectedByOffline: '设备离线或适配器未连接',
  RejectedByUnknownMethod: '协议目录中不存在该方法，或该方法仅为设备上报事件',
  RejectedByConfirm: '高危命令缺少二次确认（整表覆盖 / 单插槽设置均需 confirm=true）',
};

/** 开关动作 → 中文。 */
const ACTION_TEXT: Record<string, string> = {
  on: '开',
  off: '关',
  toggle: '翻转',
};

/** 星期序号（1-7）→ 中文短名。协议约定 1=周一 … 7=周日。 */
const WEEK_DAY_LABELS: ReadonlyArray<{ value: number; label: string }> = [
  { value: 1, label: '一' },
  { value: 2, label: '二' },
  { value: 3, label: '三' },
  { value: 4, label: '四' },
  { value: 5, label: '五' },
  { value: 6, label: '六' },
  { value: 7, label: '日' },
];

// ── 本地视图模型 ─────────────────────────────────────────────────

/** 内联结果提示。 */
interface Notice {
  kind: 'success' | 'error' | 'warning';
  title: string;
  detail?: string;
}

/** 命令日志条目。 */
interface LogEntry {
  time: Date;
  method: string;
  success: boolean;
  message: string;
}

/**
 * 普通定时任务草稿行。
 *
 * key 只用于 React 列表渲染，**不出网**；taskId 才是设备分配的任务 id，
 * 新建行为 null，出网时映射到请求项的 id 字段。
 */
interface NormalDraft {
  key: string;
  taskId: string | null;
  enable: boolean;
  weekDays: number[];
  hour: number;
  minute: number;
  action: AnShengSwitchAction;
  uploadEnable: boolean;
}

/** 循环定时任务草稿行。key 同样只用于渲染，不出网。 */
interface LoopDraft {
  key: string;
  taskId: string | null;
  enable: boolean;
  weekDays: number[];
  sHour: number;
  sMinute: number;
  eHour: number;
  eMinute: number;
  onMins: number;
  offMins: number;
}

/** 单插槽草稿（普通 + 循环两组）。 */
interface SlotDraft {
  timeTasks: NormalDraft[];
  loopTimeTasks: LoopDraft[];
}

// ── 纯函数工具 ───────────────────────────────────────────────────

/** 单调递增的本地行标识计数器，保证 React key 稳定唯一。 */
let draftKeySeed = 0;

/**
 * 生成一个只在前端使用的草稿行标识。
 * @param prefix 前缀，便于调试时区分普通 / 循环。
 * @returns 唯一 key。
 */
function nextDraftKey(prefix: string): string {
  draftKeySeed += 1;
  return `${prefix}-${draftKeySeed}`;
}

/**
 * 把拒绝原因翻译成中文。
 *
 * 后端以字符串枚举出网；对未来新增的未知值原样回显，绝不吞掉信息。
 * @param reason 拒绝原因，可能为空。
 * @returns 中文描述；无拒绝原因时返回 null。
 */
function describeRejectReason(reason?: AnShengCommandRejectReason | null): string | null {
  if (!reason) {
    return null;
  }
  return REJECT_REASON_TEXT[reason] ?? `未知拒绝原因：${String(reason)}`;
}

/**
 * 从一次失败的下发结果里拼出面向人的原因文案。
 * @param rejectReason 机器可读拒绝原因。
 * @param errorMessage 后端给的人类可读描述。
 * @param fallbackMessage 信封层 message。
 * @returns 组合后的提示文案。
 */
function buildFailureDetail(
  rejectReason: AnShengCommandRejectReason | null | undefined,
  errorMessage: string | null | undefined,
  fallbackMessage: string
): string {
  const reasonText = describeRejectReason(rejectReason);
  if (reasonText && rejectReason) {
    return `${rejectReason}：${reasonText}`;
  }
  return errorMessage || fallbackMessage || '下发失败';
}

/**
 * 从异常里抽取可展示的错误文案。
 *
 * 【两种形状都要认】httpClient 的响应拦截器把非 401/403/404 的失败**归一化**成
 * 纯对象 { code, message, details, timestamp }（AxiosError 的 response 被丢弃）；
 * 但网络层直抛或未来拦截器调整时仍可能拿到原始 AxiosError，故两种形状都兜。
 * @param err 未知异常对象。
 * @param fallback 兜底文案。
 * @returns 错误文案。
 */
function extractErrorMessage(err: unknown, fallback: string): string {
  const anyErr = err as { response?: { data?: { message?: string } }; message?: string };
  return anyErr?.response?.data?.message || anyErr?.message || fallback;
}

/**
 * 从异常里抽取 HTTP 状态码。
 *
 * 【为什么不能只读 error.response.status】httpClient 的响应拦截器对非 401/403/404 的
 * 失败 reject 的是 { code: status, message, details, timestamp } 这个**纯对象**，
 * 原始 AxiosError.response 已被丢掉。因此 409 判定必须优先认归一化对象的 code 字段，
 * 同时保留对原始 AxiosError 的兼容。
 * @param err 未知异常对象。
 * @returns HTTP 状态码；无法判定时返回 null。
 */
function extractHttpStatus(err: unknown): number | null {
  const anyErr = err as {
    response?: { status?: number };
    code?: unknown;
    status?: unknown;
  };
  if (typeof anyErr?.response?.status === 'number') {
    return anyErr.response.status;
  }
  if (typeof anyErr?.code === 'number') {
    return anyErr.code;
  }
  if (typeof anyErr?.status === 'number') {
    return anyErr.status;
  }
  return null;
}

/**
 * 判断一次异常是否为乐观并发冲突。
 *
 * 双保险：先看 HTTP 状态码 409；若拦截器保留了信封体，再确认 data.concurrencyConflict。
 * @param err 未知异常对象。
 * @returns 是否为并发冲突。
 */
function isConcurrencyConflict(err: unknown): boolean {
  if (extractHttpStatus(err) === CONFLICT_STATUS) {
    return true;
  }
  const anyErr = err as { response?: { data?: { data?: { concurrencyConflict?: boolean } } } };
  return anyErr?.response?.data?.data?.concurrencyConflict === true;
}

/**
 * 格式化 UTC 时间串为本地短时间。
 * @param value ISO 时间串。
 * @returns 本地时间字符串；空值返回「—」。
 */
function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }
  return date.toLocaleString('zh-CN', {
    month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

/**
 * 格式化日志时间。
 * @param date 时间对象。
 * @returns HH:mm:ss。
 */
function formatLogTime(date: Date): string {
  return date.toLocaleTimeString('zh-CN', {
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

/**
 * 把时分补零成 HH:mm。
 * @param hour 小时。
 * @param minute 分钟。
 * @returns HH:mm 文本。
 */
function formatHourMinute(hour: number, minute: number): string {
  const hh = String(Math.max(0, Math.min(23, hour))).padStart(2, '0');
  const mm = String(Math.max(0, Math.min(59, minute))).padStart(2, '0');
  return `${hh}:${mm}`;
}

/**
 * 把星期数组渲染成可读文案。
 * @param weekDays 星期序号数组（1-7）。
 * @returns 中文描述。
 */
function formatWeekDays(weekDays: number[] | null | undefined): string {
  if (!weekDays || weekDays.length === 0) {
    return '仅一次';
  }
  if (weekDays.length === 7) {
    return '每天';
  }
  const sorted = Array.from(new Set(weekDays)).sort((a, b) => a - b);
  return sorted
    .map(d => WEEK_DAY_LABELS.find(w => w.value === d)?.label ?? String(d))
    .map(label => `周${label}`)
    .join(' ');
}

/**
 * 把输入框里的字符串安全解析成受限整数。
 * @param raw 原始输入。
 * @param min 下界（含）。
 * @param max 上界（含）。
 * @param fallback 解析失败时的兜底值。
 * @returns 落在 [min, max] 内的整数。
 */
function clampInt(raw: string, min: number, max: number, fallback: number): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }
  return Math.max(min, Math.min(max, parsed));
}

/**
 * 判断一个设备是否像安圣设备（SerialNumber 为纯数字 IMEI）。
 * 与 T9 SwitchControlPage.isAnShengDevice 保持同一判据。
 * @param device 设备。
 * @returns 是否为安圣设备。
 */
function isAnShengDevice(device: DeviceDto): boolean {
  return Boolean(device.serialNumber && /^\d{10,20}$/.test(device.serialNumber));
}

/**
 * 镜像行 → 普通定时草稿行。
 * @param dto 后端镜像行。
 * @returns 草稿行。
 */
function toNormalDraft(dto: AnShengTimeTaskDto): NormalDraft {
  const action = dto.action === 'off' || dto.action === 'toggle' ? dto.action : 'on';
  return {
    key: nextDraftKey('normal'),
    taskId: dto.taskId ?? null,
    enable: dto.enable,
    weekDays: Array.isArray(dto.weekDays) ? [...dto.weekDays] : [],
    hour: dto.hour,
    minute: dto.minute,
    action,
    uploadEnable: dto.uploadEnable,
  };
}

/**
 * 镜像行 → 循环定时草稿行。
 * @param dto 后端镜像行。
 * @returns 草稿行。
 */
function toLoopDraft(dto: AnShengTimeTaskDto): LoopDraft {
  return {
    key: nextDraftKey('loop'),
    taskId: dto.taskId ?? null,
    enable: dto.enable,
    weekDays: Array.isArray(dto.weekDays) ? [...dto.weekDays] : [],
    sHour: dto.sHour,
    sMinute: dto.sMinute,
    eHour: dto.eHour,
    eMinute: dto.eMinute,
    onMins: dto.onMins,
    offMins: dto.offMins,
  };
}

/**
 * 普通定时草稿行 → 出网请求项。
 * @param draft 草稿行。
 * @returns 请求项（字段名与后端 AnShengTimeTaskItemRequest 一一对应）。
 */
function toNormalRequest(draft: NormalDraft): AnShengTimeTaskItemRequest {
  return {
    id: draft.taskId,
    enable: draft.enable,
    weekDays: Array.from(new Set(draft.weekDays)).sort((a, b) => a - b),
    hour: draft.hour,
    minute: draft.minute,
    action: draft.action,
    uploadEnable: draft.uploadEnable,
  };
}

/**
 * 循环定时草稿行 → 出网请求项。
 * @param draft 草稿行。
 * @returns 请求项（字段名与后端 AnShengLoopTimeTaskItemRequest 一一对应）。
 */
function toLoopRequest(draft: LoopDraft): AnShengLoopTimeTaskItemRequest {
  return {
    id: draft.taskId,
    enable: draft.enable,
    weekDays: Array.from(new Set(draft.weekDays)).sort((a, b) => a - b),
    sHour: draft.sHour,
    sMinute: draft.sMinute,
    eHour: draft.eHour,
    eMinute: draft.eMinute,
    onMins: draft.onMins,
    offMins: draft.offMins,
  };
}

/** 新建一条普通定时草稿行（默认 08:00 开、每天、上报关）。 */
function createNormalDraft(): NormalDraft {
  return {
    key: nextDraftKey('normal'),
    taskId: null,
    enable: true,
    weekDays: [1, 2, 3, 4, 5, 6, 7],
    hour: 8,
    minute: 0,
    action: 'on',
    uploadEnable: false,
  };
}

/** 新建一条循环定时草稿行（默认 08:00-18:00、开 5 分关 5 分、每天）。 */
function createLoopDraft(): LoopDraft {
  return {
    key: nextDraftKey('loop'),
    taskId: null,
    enable: true,
    weekDays: [1, 2, 3, 4, 5, 6, 7],
    sHour: 8,
    sMinute: 0,
    eHour: 18,
    eMinute: 0,
    onMins: 5,
    offMins: 5,
  };
}

/**
 * 从镜像集合里取出乐观并发令牌。
 *
 * 后端约定「来自 GET /time-tasks 返回的**任意行**的 rowVersion」，
 * 这里稳定地取第一条可用行，保证同一份镜像每次算出同一个令牌。
 * @param sets 镜像集合列表。
 * @returns 令牌；无任何行时返回 null（后端视为不做并发校验）。
 */
function resolveRowVersion(sets: AnShengSlotTimeTaskSetDto[]): number | null {
  for (const set of sets) {
    const row = (set.timeTasks ?? [])[0] ?? (set.loopTimeTasks ?? [])[0];
    if (row && typeof row.rowVersion === 'number') {
      return row.rowVersion;
    }
  }
  return null;
}

/**
 * 从单个插槽镜像里取出乐观并发令牌。
 * @param set 单插槽镜像；可能为空（该插槽尚无任务）。
 * @returns 令牌；无行时返回 null。
 */
function resolveSlotRowVersion(set: AnShengSlotTimeTaskSetDto | undefined): number | null {
  if (!set) {
    return null;
  }
  return resolveRowVersion([set]);
}

// ── 主组件 ───────────────────────────────────────────────────────

/**
 * 安圣定时任务面板。
 *
 * 页面可见需 VIEW_DEVICES；所有下发动作需 SEND_DEVICE_COMMANDS，
 * 缺权限时按钮 disabled 而非隐藏，让用户知道「功能存在但你没权限」（与 T9 同口径）。
 */
export function ScheduleEditorPage() {
  const { hasPermission } = useAuth();
  const canView = hasPermission(PERMISSIONS.VIEW_DEVICES);
  const canSend = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS);

  // ── 设备列表 ───────────────────────────────────────────────
  const [devices, setDevices] = useState<DeviceDto[]>([]);
  const [devicesLoading, setDevicesLoading] = useState<boolean>(false);
  const [devicesError, setDevicesError] = useState<string | null>(null);
  const [deviceSearch, setDeviceSearch] = useState<string>('');
  const [selectedDeviceId, setSelectedDeviceId] = useState<number | null>(null);

  // ── 能力档案（只用来确定插槽路数）───────────────────────────
  const [profile, setProfile] = useState<AnShengDeviceProfileDto | null>(null);
  const [profileLoading, setProfileLoading] = useState<boolean>(false);
  const [manualSlotCount, setManualSlotCount] = useState<number>(DEFAULT_SLOT_COUNT);

  // ── 定时任务镜像 + 编辑草稿 ─────────────────────────────────
  const [taskSets, setTaskSets] = useState<AnShengSlotTimeTaskSetDto[]>([]);
  const [tasksLoading, setTasksLoading] = useState<boolean>(false);
  const [drafts, setDrafts] = useState<Record<number, SlotDraft>>({});
  const [activeSlot, setActiveSlot] = useState<number>(1);
  const [savingSlot, setSavingSlot] = useState<boolean>(false);
  const [savingAll, setSavingAll] = useState<boolean>(false);
  const [overwriteDialogOpen, setOverwriteDialogOpen] = useState<boolean>(false);

  // ── 结果反馈 ───────────────────────────────────────────────
  const [notice, setNotice] = useState<Notice | null>(null);
  const [commandLog, setCommandLog] = useState<LogEntry[]>([]);

  /** 挂起的补刷定时器；设备切换 / 卸载时统一清理，避免 setState on unmounted。 */
  const refreshTimersRef = useRef<number[]>([]);

  /** 清空所有挂起的补刷定时器。 */
  const clearRefreshTimers = useCallback((): void => {
    refreshTimersRef.current.forEach(id => window.clearTimeout(id));
    refreshTimersRef.current = [];
  }, []);

  /**
   * 追加一条命令日志。
   * @param method 协议方法名。
   * @param success 是否成功。
   * @param message 描述。
   */
  const appendLog = useCallback((method: string, success: boolean, message: string): void => {
    setCommandLog(prev => [
      { time: new Date(), method, success, message },
      ...prev,
    ].slice(0, MAX_LOG_ENTRIES));
  }, []);

  // ── 加载安圣设备列表 ───────────────────────────────────────
  const loadDevices = useCallback(async (): Promise<void> => {
    setDevicesLoading(true);
    setDevicesError(null);
    try {
      const response = await deviceApi.getDevices(1, 200);
      if (response.data.code === OK_CODE && response.data.data) {
        const all = response.data.data.items || [];
        setDevices(all.filter(isAnShengDevice));
      } else {
        setDevices([]);
        setDevicesError(response.data.message || '加载安圣设备列表失败');
      }
    } catch (err: unknown) {
      setDevices([]);
      setDevicesError(extractErrorMessage(err, '加载安圣设备列表失败'));
    } finally {
      setDevicesLoading(false);
    }
  }, []);

  // ── 加载能力档案（仅取插槽数量）─────────────────────────────
  const loadProfile = useCallback(async (deviceId: number, silent = false): Promise<void> => {
    if (!silent) {
      setProfileLoading(true);
    }
    try {
      const response = await anshengApi.getProfile(deviceId);
      if (response.data.code === OK_CODE && response.data.data) {
        setProfile(response.data.data);
      } else if (!silent) {
        // 档案缺失不是致命错误：设备可能尚未探测过，插槽路数按兜底值渲染即可。
        setProfile(null);
      }
    } catch (err: unknown) {
      if (!silent) {
        setProfile(null);
        appendLog('getProfile', false, extractErrorMessage(err, '读取设备档案失败'));
      }
    } finally {
      if (!silent) {
        setProfileLoading(false);
      }
    }
  }, [appendLog]);

  /**
   * 把后端镜像铺成可编辑草稿。
   *
   * 【设备权威】草稿始终以最近一次镜像为准：每次成功读取（含写后补刷）都会重建草稿，
   * 让界面反映设备真值而不是用户的中间态。这与 T9「设备权威 + 异步刷新」同一原则。
   * @param sets 镜像集合列表。
   * @returns slotNum → 草稿。
   */
  const buildDrafts = useCallback((sets: AnShengSlotTimeTaskSetDto[]): Record<number, SlotDraft> => {
    const next: Record<number, SlotDraft> = {};
    sets.forEach(set => {
      next[set.slotNum] = {
        timeTasks: (set.timeTasks ?? []).map(toNormalDraft),
        loopTimeTasks: (set.loopTimeTasks ?? []).map(toLoopDraft),
      };
    });
    return next;
  }, []);

  // ── 加载定时任务镜像 ───────────────────────────────────────
  const loadTimeTasks = useCallback(async (deviceId: number, silent = false): Promise<void> => {
    if (!silent) {
      setTasksLoading(true);
    }
    try {
      const response = await anshengApi.getTimeTasks(deviceId);
      if (response.data.code === OK_CODE && Array.isArray(response.data.data)) {
        const sets = response.data.data;
        setTaskSets(sets);
        setDrafts(buildDrafts(sets));
      } else if (!silent) {
        setTaskSets([]);
        setDrafts({});
        appendLog('getTimeTasks', false, response.data.message || '读取定时任务镜像失败');
      }
    } catch (err: unknown) {
      if (!silent) {
        setTaskSets([]);
        setDrafts({});
        appendLog('getTimeTasks', false, extractErrorMessage(err, '读取定时任务镜像失败'));
      }
    } finally {
      if (!silent) {
        setTasksLoading(false);
      }
    }
  }, [appendLog, buildDrafts]);

  /**
   * 延后一次静默补刷（设备权威 + 异步刷新，见 REFRESH_DELAY_MS 注释）。
   * @param deviceId 设备主键。
   */
  const scheduleRefresh = useCallback((deviceId: number): void => {
    const timerId = window.setTimeout(() => {
      void loadTimeTasks(deviceId, true);
    }, REFRESH_DELAY_MS);
    refreshTimersRef.current.push(timerId);
  }, [loadTimeTasks]);

  // ── 初始加载 ───────────────────────────────────────────────
  useEffect(() => {
    void loadDevices();
  }, [loadDevices]);

  // ── 切换设备：重置面板并拉取该设备数据 ─────────────────────
  useEffect(() => {
    clearRefreshTimers();
    setNotice(null);
    setProfile(null);
    setTaskSets([]);
    setDrafts({});
    setActiveSlot(1);
    setOverwriteDialogOpen(false);

    if (selectedDeviceId === null) {
      return;
    }
    void loadProfile(selectedDeviceId);
    void loadTimeTasks(selectedDeviceId);
  }, [selectedDeviceId, loadProfile, loadTimeTasks, clearRefreshTimers]);

  // ── 卸载清理 ───────────────────────────────────────────────
  useEffect(() => clearRefreshTimers, [clearRefreshTimers]);

  // ── 派生数据 ───────────────────────────────────────────────

  /** 关键词过滤后的设备列表。 */
  const filteredDevices = useMemo<DeviceDto[]>(() => {
    const keyword = deviceSearch.trim().toLowerCase();
    if (!keyword) {
      return devices;
    }
    return devices.filter(d =>
      (d.name || '').toLowerCase().includes(keyword) ||
      (d.serialNumber || '').toLowerCase().includes(keyword) ||
      (d.model || '').toLowerCase().includes(keyword)
    );
  }, [devices, deviceSearch]);

  /** 当前选中的设备。 */
  const selectedDevice = useMemo<DeviceDto | undefined>(
    () => devices.find(d => Number(d.id) === selectedDeviceId),
    [devices, selectedDeviceId]
  );

  /**
   * 插槽路数是否由权威源给出。
   *
   * 【与 T9 同一坑】time-tasks 镜像是**稀疏集合**（只含配置过定时任务的插槽），
   * 其 length 不等于插槽路数，绝不能据此推断，否则会隐藏手动改路数的逃生通道。
   * 故「已知路数」只认档案 slotAmount。
   */
  const slotCountKnown = useMemo<boolean>(
    () => (profile?.slotAmount ?? 0) > 0,
    [profile]
  );

  /**
   * 最终渲染的插槽路数。
   *
   * 镜像为稀疏集合，用「最大 slotNum」而非 length 作为上界，避免漏渲染插槽。
   */
  const slotCount = useMemo<number>(() => {
    const maxMirrorSlot = taskSets.reduce((max, set) => Math.max(max, set.slotNum), 0);
    const resolved = Math.max(profile?.slotAmount ?? 0, maxMirrorSlot);
    return resolved > 0 ? resolved : manualSlotCount;
  }, [profile, taskSets, manualSlotCount]);

  /** 插槽编号列表（1..slotCount）。 */
  const slotNumbers = useMemo<number[]>(
    () => Array.from({ length: slotCount }, (_, i) => i + 1),
    [slotCount]
  );

  /** slotNum → 镜像集合。 */
  const setBySlot = useMemo<Map<number, AnShengSlotTimeTaskSetDto>>(() => {
    const map = new Map<number, AnShengSlotTimeTaskSetDto>();
    taskSets.forEach(set => map.set(set.slotNum, set));
    return map;
  }, [taskSets]);

  /** 镜像里全部任务行（普通 + 循环）的扁平视图，用于陈旧判定与只读表渲染。 */
  const allMirrorRows = useMemo<AnShengTimeTaskDto[]>(() => {
    const rows: AnShengTimeTaskDto[] = [];
    taskSets.forEach(set => {
      (set.timeTasks ?? []).forEach(row => rows.push(row));
      (set.loopTimeTasks ?? []).forEach(row => rows.push(row));
    });
    return rows;
  }, [taskSets]);

  /** 是否存在陈旧镜像（>24h 未与设备同步）。 */
  const hasStaleRow = useMemo<boolean>(
    () => allMirrorRows.some(row => row.isStale),
    [allMirrorRows]
  );

  /** 当前插槽的草稿（未初始化时给空集合，保证受控输入不闪 undefined）。 */
  const activeDraft = useMemo<SlotDraft>(
    () => drafts[activeSlot] ?? { timeTasks: [], loopTimeTasks: [] },
    [drafts, activeSlot]
  );

  /** 当前插槽的并发令牌（来自镜像行；该插槽尚无任务时为 null）。 */
  const activeRowVersion = useMemo<number | null>(
    () => resolveSlotRowVersion(setBySlot.get(activeSlot)),
    [setBySlot, activeSlot]
  );

  /** 整表覆盖用的并发令牌（取镜像里第一条可用行）。 */
  const tableRowVersion = useMemo<number | null>(
    () => resolveRowVersion(taskSets),
    [taskSets]
  );

  // ── 草稿编辑 ───────────────────────────────────────────────

  /**
   * 以不可变方式更新某插槽的草稿。
   * @param slotNum 插槽编号。
   * @param updater 草稿变换函数。
   */
  const updateDraft = useCallback((
    slotNum: number,
    updater: (draft: SlotDraft) => SlotDraft
  ): void => {
    setDrafts(prev => {
      const current = prev[slotNum] ?? { timeTasks: [], loopTimeTasks: [] };
      return { ...prev, [slotNum]: updater(current) };
    });
  }, []);

  /**
   * 修改某条普通定时草稿行的部分字段。
   * @param key 草稿行 key。
   * @param patch 待覆盖字段。
   */
  const patchNormal = useCallback((key: string, patch: Partial<NormalDraft>): void => {
    updateDraft(activeSlot, draft => ({
      ...draft,
      timeTasks: draft.timeTasks.map(row => (row.key === key ? { ...row, ...patch } : row)),
    }));
  }, [activeSlot, updateDraft]);

  /**
   * 修改某条循环定时草稿行的部分字段。
   * @param key 草稿行 key。
   * @param patch 待覆盖字段。
   */
  const patchLoop = useCallback((key: string, patch: Partial<LoopDraft>): void => {
    updateDraft(activeSlot, draft => ({
      ...draft,
      loopTimeTasks: draft.loopTimeTasks.map(row => (row.key === key ? { ...row, ...patch } : row)),
    }));
  }, [activeSlot, updateDraft]);

  /**
   * 切换某条普通定时草稿行的星期选中态。
   * @param key 草稿行 key。
   * @param day 星期序号（1-7）。
   */
  const toggleNormalWeekDay = useCallback((key: string, day: number): void => {
    updateDraft(activeSlot, draft => ({
      ...draft,
      timeTasks: draft.timeTasks.map(row => {
        if (row.key !== key) {
          return row;
        }
        const has = row.weekDays.includes(day);
        return {
          ...row,
          weekDays: has ? row.weekDays.filter(d => d !== day) : [...row.weekDays, day],
        };
      }),
    }));
  }, [activeSlot, updateDraft]);

  /**
   * 切换某条循环定时草稿行的星期选中态。
   * @param key 草稿行 key。
   * @param day 星期序号（1-7）。
   */
  const toggleLoopWeekDay = useCallback((key: string, day: number): void => {
    updateDraft(activeSlot, draft => ({
      ...draft,
      loopTimeTasks: draft.loopTimeTasks.map(row => {
        if (row.key !== key) {
          return row;
        }
        const has = row.weekDays.includes(day);
        return {
          ...row,
          weekDays: has ? row.weekDays.filter(d => d !== day) : [...row.weekDays, day],
        };
      }),
    }));
  }, [activeSlot, updateDraft]);

  /** 新增一条普通定时草稿行。 */
  const addNormal = useCallback((): void => {
    updateDraft(activeSlot, draft => (
      draft.timeTasks.length >= MAX_TASKS_PER_GROUP
        ? draft
        : { ...draft, timeTasks: [...draft.timeTasks, createNormalDraft()] }
    ));
  }, [activeSlot, updateDraft]);

  /** 新增一条循环定时草稿行。 */
  const addLoop = useCallback((): void => {
    updateDraft(activeSlot, draft => (
      draft.loopTimeTasks.length >= MAX_TASKS_PER_GROUP
        ? draft
        : { ...draft, loopTimeTasks: [...draft.loopTimeTasks, createLoopDraft()] }
    ));
  }, [activeSlot, updateDraft]);

  /**
   * 删除一条普通定时草稿行。
   * @param key 草稿行 key。
   */
  const removeNormal = useCallback((key: string): void => {
    updateDraft(activeSlot, draft => ({
      ...draft,
      timeTasks: draft.timeTasks.filter(row => row.key !== key),
    }));
  }, [activeSlot, updateDraft]);

  /**
   * 删除一条循环定时草稿行。
   * @param key 草稿行 key。
   */
  const removeLoop = useCallback((key: string): void => {
    updateDraft(activeSlot, draft => ({
      ...draft,
      loopTimeTasks: draft.loopTimeTasks.filter(row => row.key !== key),
    }));
  }, [activeSlot, updateDraft]);

  // ── 下发 ───────────────────────────────────────────────────

  /**
   * 消化一次定时任务下发结果：写日志、给出提示。
   *
   * 【铁律②】只认信封 code 与 data.accepted，绝不读 data.success（后端根本没有这个字段）。
   * @param method 协议方法名（setTimeTasks / setSlotTimeTasks）。
   * @param code 信封状态码。
   * @param envelopeMessage 信封 message。
   * @param result 下发结果 DTO。
   * @param successTitle 成功提示标题。
   * @returns 是否成功受理。
   */
  const consumeTimeTaskResult = useCallback((
    method: string,
    code: number,
    envelopeMessage: string,
    result: AnShengTimeTaskResultDto | null | undefined,
    successTitle: string
  ): boolean => {
    // 无论成败，后端都会带回乐观镜像快照（形状稳定），能用就先用，写后补刷再覆盖真值。
    if (result && Array.isArray(result.slots)) {
      setTaskSets(result.slots);
    }

    const accepted = code === OK_CODE && Boolean(result?.accepted);
    if (accepted) {
      setNotice({
        kind: 'success',
        title: successTitle,
        detail: result?.commandId ? `命令 ID：${result.commandId}` : envelopeMessage,
      });
      appendLog(method, true, successTitle);
      return true;
    }

    const detail = buildFailureDetail(result?.rejectReason, result?.errorMessage, envelopeMessage);
    setNotice({ kind: 'error', title: '定时任务下发被拒绝', detail });
    appendLog(method, false, detail);
    return false;
  }, [appendLog]);

  /**
   * 统一处理一次下发异常，把乐观并发冲突单列出来。
   *
   * 【409 是 T10 唯一的非 200】冲突时提示用户刷新重试，并立即重新拉取镜像，
   * 让页面上的 rowVersion 与草稿回到最新状态。
   * @param err 异常对象。
   * @param method 协议方法名。
   * @param fallback 兜底文案。
   */
  const handleSendError = useCallback((err: unknown, method: string, fallback: string): void => {
    if (isConcurrencyConflict(err)) {
      const detail = extractErrorMessage(err, '定时任务已被其他操作修改，请刷新后重试');
      setNotice({
        kind: 'warning',
        title: '乐观并发冲突（HTTP 409）',
        detail: `${detail}｜已自动重新拉取镜像，请复核后重新提交`,
      });
      appendLog(method, false, `并发冲突：${detail}`);
      if (selectedDeviceId !== null) {
        void loadTimeTasks(selectedDeviceId);
      }
      return;
    }
    const message = extractErrorMessage(err, fallback);
    setNotice({ kind: 'error', title: '请求失败', detail: message });
    appendLog(method, false, message);
  }, [appendLog, selectedDeviceId, loadTimeTasks]);

  /**
   * 保存当前插槽的定时任务（setSlotTimeTasks）。
   *
   * slotNum 走路由段；confirm 恒为 true（页面上的编辑动作本身即用户意图的显式确认，
   * 且后端在 confirm=false 时零出网，传 false 只会白白得到一次 RejectedByConfirm）。
   */
  const handleSaveSlot = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setSavingSlot(true);
    setNotice(null);
    try {
      const response = await anshengApi.setSlotTimeTasks(selectedDeviceId, activeSlot, {
        confirm: true,
        timeTasks: activeDraft.timeTasks.map(toNormalRequest),
        loopTimeTasks: activeDraft.loopTimeTasks.map(toLoopRequest),
        rowVersion: activeRowVersion,
      });
      consumeTimeTaskResult(
        'setSlotTimeTasks',
        response.data.code,
        response.data.message,
        response.data.data,
        `插槽 ${activeSlot} 定时任务已下发（普通 ${activeDraft.timeTasks.length} 条 / 循环 ${activeDraft.loopTimeTasks.length} 条）`
      );
      scheduleRefresh(selectedDeviceId);
    } catch (err: unknown) {
      handleSendError(err, 'setSlotTimeTasks', '单插槽定时任务下发失败');
    } finally {
      setSavingSlot(false);
    }
  }, [
    selectedDeviceId, canSend, activeSlot, activeDraft, activeRowVersion,
    consumeTimeTaskResult, scheduleRefresh, handleSendError,
  ]);

  /**
   * 整表覆盖全部插槽的定时任务（setTimeTasks）。
   *
   * 【高危】未列出的插槽会被清空，故这里把**所有**插槽（1..slotCount）都显式列出，
   * 没有草稿的插槽以空集合提交，语义与「该插槽无定时任务」一致，不会误伤别的插槽。
   */
  const handleOverwriteAll = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setOverwriteDialogOpen(false);
    setSavingAll(true);
    setNotice(null);
    const slots: AnShengSlotTimeTaskSetRequest[] = slotNumbers.map(slotNum => {
      const draft = drafts[slotNum] ?? { timeTasks: [], loopTimeTasks: [] };
      return {
        slotNum,
        timeTasks: draft.timeTasks.map(toNormalRequest),
        loopTimeTasks: draft.loopTimeTasks.map(toLoopRequest),
      };
    });
    try {
      const response = await anshengApi.setTimeTasks(selectedDeviceId, {
        confirm: true,
        slots,
        rowVersion: tableRowVersion,
      });
      consumeTimeTaskResult(
        'setTimeTasks',
        response.data.code,
        response.data.message,
        response.data.data,
        `整表覆盖已下发（共 ${slots.length} 个插槽）`
      );
      scheduleRefresh(selectedDeviceId);
    } catch (err: unknown) {
      handleSendError(err, 'setTimeTasks', '整表覆盖下发失败');
    } finally {
      setSavingAll(false);
    }
  }, [
    selectedDeviceId, canSend, slotNumbers, drafts, tableRowVersion,
    consumeTimeTaskResult, scheduleRefresh, handleSendError,
  ]);

  /** 手动同步：重新拉取档案与定时任务镜像。 */
  const handleManualSync = useCallback((): void => {
    if (selectedDeviceId === null) {
      return;
    }
    void loadProfile(selectedDeviceId);
    void loadTimeTasks(selectedDeviceId);
  }, [selectedDeviceId, loadProfile, loadTimeTasks]);

  // ── 无查看权限：直接门控整页 ───────────────────────────────
  if (!canView) {
    return (
      <div className="p-6">
        <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
          <ShieldAlert className="w-12 h-12 text-amber-500/70 mx-auto mb-3" />
          <p className="text-slate-300 text-sm font-medium">无权访问「安圣定时任务」</p>
          <p className="text-slate-500 text-xs mt-1">需要「查看设备」（view_devices）权限，请联系管理员开通</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      {/* ────────────── 页面标题 ────────────── */}
      <div className="flex items-center justify-between flex-wrap gap-3">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-lg bg-indigo-500/20 border border-indigo-500/30">
            <CalendarClock className="w-6 h-6 text-indigo-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-white">安圣定时任务</h1>
            <p className="text-sm text-slate-400">普通定时 · 循环定时 · 整表覆盖</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {!canSend && (
            <span className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs
                             bg-amber-500/10 border border-amber-500/30 text-amber-400">
              <ShieldAlert className="w-3.5 h-3.5" />
              只读模式（缺少 send_device_commands 权限）
            </span>
          )}
          <button
            onClick={handleManualSync}
            disabled={selectedDeviceId === null || profileLoading || tasksLoading}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-indigo-600/20 border border-indigo-500/30
                       text-indigo-400 hover:bg-indigo-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
          >
            <RefreshCw className={`w-4 h-4 ${(profileLoading || tasksLoading) ? 'animate-spin' : ''}`} />
            手动同步
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        {/* ────────────── 左栏：设备选择 ────────────── */}
        <div className="space-y-4">
          <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-3">
            <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
              <Cpu className="w-4 h-4 text-indigo-400" />
              选择安圣设备
            </h3>

            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <input
                type="text"
                placeholder="搜索名称 / IMEI / 型号..."
                value={deviceSearch}
                onChange={e => setDeviceSearch(e.target.value)}
                className="w-full pl-10 pr-4 py-2 rounded-lg bg-slate-900/60 border border-slate-700/50 text-sm text-slate-200
                           placeholder:text-slate-500 focus:outline-none focus:border-indigo-500/50 transition-all"
              />
            </div>

            {devicesError && (
              <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-red-500/10 border border-red-500/30 text-red-400 text-xs">
                <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                <span>{devicesError}</span>
              </div>
            )}

            {devicesLoading ? (
              <div className="flex items-center gap-2 text-slate-400 text-sm py-4 justify-center">
                <Loader2 className="w-4 h-4 animate-spin" />
                加载设备...
              </div>
            ) : filteredDevices.length === 0 ? (
              <div className="text-center py-6 text-slate-500 text-sm">
                <Radio className="w-8 h-8 mx-auto mb-2 text-slate-600" />
                {devices.length === 0 ? '暂未发现已认领的安圣设备' : '没有匹配的设备'}
              </div>
            ) : (
              <div className="space-y-1 max-h-[320px] overflow-y-auto">
                {filteredDevices.map(device => {
                  const deviceId = Number(device.id);
                  const active = deviceId === selectedDeviceId;
                  return (
                    <button
                      key={device.id}
                      onClick={() => setSelectedDeviceId(deviceId)}
                      className={`w-full text-left px-3 py-2.5 rounded-lg text-sm transition-all ${
                        active
                          ? 'bg-indigo-600/20 border border-indigo-500/30 text-indigo-300'
                          : 'border border-transparent text-slate-300 hover:bg-slate-700/50'
                      }`}
                    >
                      <div className="font-medium">{device.name}</div>
                      <div className="text-xs text-slate-500 mt-0.5">
                        <span className="font-mono">IMEI: {device.serialNumber || device.id}</span>
                        {device.model && <span className="ml-2">型号: {device.model}</span>}
                      </div>
                    </button>
                  );
                })}
              </div>
            )}

            <button
              onClick={() => { void loadDevices(); }}
              className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-md text-xs
                         text-slate-400 border border-slate-700/50 hover:text-slate-200 hover:bg-slate-700/50 transition-all"
            >
              <RefreshCw className={`w-3 h-3 ${devicesLoading ? 'animate-spin' : ''}`} />
              刷新设备列表
            </button>
          </div>

          {/* 设备档案摘要 + 并发令牌 */}
          {selectedDeviceId !== null && (
            <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-2">
              <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2 mb-1">
                <Info className="w-4 h-4 text-cyan-400" />
                设备档案
              </h3>
              {profileLoading ? (
                <div className="flex items-center gap-2 text-slate-400 text-xs py-2">
                  <Loader2 className="w-3.5 h-3.5 animate-spin" /> 读取档案...
                </div>
              ) : (
                <dl className="space-y-1.5 text-xs">
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">品类</dt>
                    <dd className="text-slate-300">{profile?.kindName || profile?.kind || '未知'}</dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">插槽数</dt>
                    <dd className="text-slate-300">{profile?.slotAmount ?? '未知'}</dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">固件</dt>
                    <dd className="text-slate-300 font-mono">{profile?.version || '—'}</dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">并发令牌</dt>
                    <dd className="text-slate-300 font-mono">{tableRowVersion ?? '—'}</dd>
                  </div>
                </dl>
              )}
              <p className="text-[11px] text-slate-600 leading-relaxed pt-1">
                定时任务仅开关类（Switch4G）设备放行；喇叭类下发会被结构性拒绝并返回 RejectedByKind。
              </p>
            </div>
          )}
        </div>

        {/* ────────────── 右栏：插槽编辑 + 镜像 + 日志 ────────────── */}
        <div className="xl:col-span-2 space-y-4">
          {selectedDeviceId === null ? (
            <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
              <CalendarClock className="w-12 h-12 text-slate-600 mx-auto mb-3" />
              <p className="text-slate-500 text-sm">请先选择一个安圣设备</p>
              <p className="text-slate-600 text-xs mt-1">选择设备后即可编辑各插槽的普通 / 循环定时任务</p>
            </div>
          ) : (
            <>
              {/* 结果提示 */}
              <AnimatePresence>
                {notice && (
                  <motion.div
                    initial={{ opacity: 0, y: -8 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -8 }}
                    className={`flex items-start gap-2 px-4 py-3 rounded-lg text-sm ${
                      notice.kind === 'success'
                        ? 'bg-green-500/10 border border-green-500/30 text-green-400'
                        : notice.kind === 'warning'
                          ? 'bg-amber-500/10 border border-amber-500/30 text-amber-400'
                          : 'bg-red-500/10 border border-red-500/30 text-red-400'
                    }`}
                  >
                    {notice.kind === 'success'
                      ? <CheckCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
                      : <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />}
                    <div className="min-w-0">
                      <div className="font-medium">{notice.title}</div>
                      {notice.detail && (
                        <div className="text-xs opacity-80 mt-0.5 break-all">{notice.detail}</div>
                      )}
                    </div>
                    <button
                      onClick={() => setNotice(null)}
                      className="ml-auto text-xs opacity-60 hover:opacity-100 transition-opacity"
                    >
                      关闭
                    </button>
                  </motion.div>
                )}
              </AnimatePresence>

              {/* ── 插槽切换条 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-3">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                    <Layers className="w-4 h-4 text-indigo-400" />
                    插槽
                    {selectedDevice && (
                      <span className="text-xs text-slate-500 font-normal">· {selectedDevice.name}</span>
                    )}
                  </h3>
                  {!slotCountKnown && (
                    <label className="flex items-center gap-1.5 text-xs text-slate-500">
                      路数
                      <select
                        value={manualSlotCount}
                        onChange={e => setManualSlotCount(Number(e.target.value))}
                        className="px-2 py-1 rounded bg-slate-900/60 border border-slate-700 text-slate-300
                                   focus:outline-none focus:border-indigo-500/50"
                      >
                        {SLOT_COUNT_OPTIONS.map(n => (
                          <option key={n} value={n}>{n}</option>
                        ))}
                      </select>
                    </label>
                  )}
                </div>

                {!slotCountKnown && (
                  <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-400 text-xs">
                    <Info className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                    <span>
                      设备档案未给出插槽数量，当前按 {manualSlotCount} 路展示。
                      可在「安圣设备」页下发 getDevInfo 完善档案后再回来。
                    </span>
                  </div>
                )}

                <div className="flex items-center gap-2 flex-wrap">
                  {slotNumbers.map(slotNum => {
                    const draft = drafts[slotNum];
                    const total = (draft?.timeTasks.length ?? 0) + (draft?.loopTimeTasks.length ?? 0);
                    const active = slotNum === activeSlot;
                    return (
                      <button
                        key={slotNum}
                        onClick={() => setActiveSlot(slotNum)}
                        className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
                          active
                            ? 'bg-indigo-600/25 text-indigo-300 border border-indigo-500/40'
                            : 'bg-slate-900/40 text-slate-400 border border-slate-700/50 hover:bg-slate-700/40'
                        }`}
                      >
                        <Hash className="w-3 h-3" />
                        插槽 {slotNum}
                        {total > 0 && (
                          <span className="ml-0.5 px-1.5 rounded-full bg-slate-700/60 text-[10px] text-slate-300">
                            {total}
                          </span>
                        )}
                      </button>
                    );
                  })}
                </div>

                {hasStaleRow && (
                  <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-400 text-xs">
                    <Clock className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                    <span>部分镜像超过 24 小时未与设备同步（isStale），建议点「手动同步」核对后再改。</span>
                  </div>
                )}

                <div className="flex items-center justify-between flex-wrap gap-2 pt-2 border-t border-slate-700/50">
                  <span className="text-xs text-slate-500">
                    插槽 {activeSlot} 当前令牌：
                    <span className="ml-1 font-mono text-slate-400">{activeRowVersion ?? '无（该插槽尚无镜像）'}</span>
                  </span>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => { void handleSaveSlot(); }}
                      disabled={!canSend || savingSlot || savingAll}
                      className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-medium
                                 bg-indigo-600/25 text-indigo-300 border border-indigo-500/30
                                 hover:bg-indigo-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                    >
                      {savingSlot ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
                      保存本插槽
                    </button>
                    <button
                      onClick={() => setOverwriteDialogOpen(true)}
                      disabled={!canSend || savingSlot || savingAll}
                      className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-medium
                                 bg-red-600/20 text-red-400 border border-red-500/30
                                 hover:bg-red-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                    >
                      {savingAll ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Upload className="w-3.5 h-3.5" />}
                      整表覆盖
                    </button>
                  </div>
                </div>
              </div>

              {/* ── 普通定时任务编辑 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                    <Clock className="w-4 h-4 text-emerald-400" />
                    普通定时任务
                    <span className="text-xs text-slate-500 font-normal">（到点执行一次动作 · taskKind=Normal）</span>
                  </h3>
                  <button
                    onClick={addNormal}
                    disabled={!canSend || activeDraft.timeTasks.length >= MAX_TASKS_PER_GROUP}
                    className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs
                               text-emerald-400 border border-emerald-500/30 bg-emerald-600/15
                               hover:bg-emerald-600/25 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    新增
                  </button>
                </div>

                {activeDraft.timeTasks.length === 0 ? (
                  <p className="text-xs text-slate-600 text-center py-6">
                    插槽 {activeSlot} 暂无普通定时任务，点「新增」添加一条
                  </p>
                ) : (
                  <div className="space-y-2.5">
                    {activeDraft.timeTasks.map((row, index) => (
                      <div
                        key={row.key}
                        className="rounded-lg bg-slate-900/40 border border-slate-700/50 p-3 space-y-2.5"
                      >
                        <div className="flex items-center justify-between flex-wrap gap-2">
                          <div className="flex items-center gap-2 text-xs">
                            <span className="text-slate-400 font-medium">#{index + 1}</span>
                            {row.taskId && (
                              <span className="font-mono text-[11px] text-slate-600">id: {row.taskId}</span>
                            )}
                            <label className="flex items-center gap-1.5 text-slate-400 cursor-pointer">
                              <input
                                type="checkbox"
                                checked={row.enable}
                                disabled={!canSend}
                                onChange={e => patchNormal(row.key, { enable: e.target.checked })}
                                className="w-3.5 h-3.5 rounded border-slate-600 bg-slate-800
                                           text-emerald-500 focus:ring-0 focus:ring-offset-0 cursor-pointer"
                              />
                              启用
                            </label>
                            <label className="flex items-center gap-1.5 text-slate-400 cursor-pointer">
                              <input
                                type="checkbox"
                                checked={row.uploadEnable}
                                disabled={!canSend}
                                onChange={e => patchNormal(row.key, { uploadEnable: e.target.checked })}
                                className="w-3.5 h-3.5 rounded border-slate-600 bg-slate-800
                                           text-cyan-500 focus:ring-0 focus:ring-offset-0 cursor-pointer"
                              />
                              <span className="inline-flex items-center gap-1">
                                <Upload className="w-3 h-3" /> 触发上报
                              </span>
                            </label>
                          </div>
                          <button
                            onClick={() => removeNormal(row.key)}
                            disabled={!canSend}
                            className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-[11px]
                                       text-red-400 border border-red-500/30 bg-red-600/10
                                       hover:bg-red-600/20 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                          >
                            <Trash2 className="w-3 h-3" />
                            删除
                          </button>
                        </div>

                        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2.5">
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">时（0-23）</label>
                            <input
                              type="number"
                              min={0}
                              max={23}
                              value={row.hour}
                              disabled={!canSend}
                              onChange={e => patchNormal(row.key, { hour: clampInt(e.target.value, 0, 23, row.hour) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-emerald-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">分（0-59）</label>
                            <input
                              type="number"
                              min={0}
                              max={59}
                              value={row.minute}
                              disabled={!canSend}
                              onChange={e => patchNormal(row.key, { minute: clampInt(e.target.value, 0, 59, row.minute) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-emerald-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">动作</label>
                            <select
                              value={row.action}
                              disabled={!canSend}
                              onChange={e => patchNormal(row.key, { action: e.target.value as AnShengSwitchAction })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-emerald-500/50 disabled:opacity-50 transition-all"
                            >
                              <option value="on">on（开）</option>
                              <option value="off">off（关）</option>
                              <option value="toggle">toggle（翻转）</option>
                            </select>
                          </div>
                          <div className="flex items-end">
                            <span className="inline-flex items-center gap-1 px-2 py-1.5 rounded-md text-[11px]
                                             bg-slate-800/60 border border-slate-700/50 text-slate-400 w-full justify-center">
                              {row.action === 'on'
                                ? <Power className="w-3 h-3 text-green-400" />
                                : row.action === 'off'
                                  ? <PowerOff className="w-3 h-3 text-red-400" />
                                  : <Shuffle className="w-3 h-3 text-cyan-400" />}
                              {formatHourMinute(row.hour, row.minute)} {ACTION_TEXT[row.action] ?? row.action}
                            </span>
                          </div>
                        </div>

                        <div className="flex items-center gap-1.5 flex-wrap">
                          <span className="text-[11px] text-slate-500 mr-1">星期</span>
                          {WEEK_DAY_LABELS.map(day => {
                            const checked = row.weekDays.includes(day.value);
                            return (
                              <button
                                key={day.value}
                                onClick={() => toggleNormalWeekDay(row.key, day.value)}
                                disabled={!canSend}
                                className={`w-6 h-6 rounded text-[11px] transition-all disabled:opacity-40 disabled:cursor-not-allowed ${
                                  checked
                                    ? 'bg-emerald-600/30 text-emerald-300 border border-emerald-500/40'
                                    : 'bg-slate-800 text-slate-500 border border-slate-700'
                                }`}
                              >
                                {day.label}
                              </button>
                            );
                          })}
                          <span className="text-[11px] text-slate-600 ml-1">{formatWeekDays(row.weekDays)}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {/* ── 循环定时任务编辑 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                    <Repeat className="w-4 h-4 text-cyan-400" />
                    循环定时任务
                    <span className="text-xs text-slate-500 font-normal">（时间窗内按开 N 分 / 关 N 分往复 · taskKind=Loop）</span>
                  </h3>
                  <button
                    onClick={addLoop}
                    disabled={!canSend || activeDraft.loopTimeTasks.length >= MAX_TASKS_PER_GROUP}
                    className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs
                               text-cyan-400 border border-cyan-500/30 bg-cyan-600/15
                               hover:bg-cyan-600/25 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    新增
                  </button>
                </div>

                {activeDraft.loopTimeTasks.length === 0 ? (
                  <p className="text-xs text-slate-600 text-center py-6">
                    插槽 {activeSlot} 暂无循环定时任务，点「新增」添加一条
                  </p>
                ) : (
                  <div className="space-y-2.5">
                    {activeDraft.loopTimeTasks.map((row, index) => (
                      <div
                        key={row.key}
                        className="rounded-lg bg-slate-900/40 border border-slate-700/50 p-3 space-y-2.5"
                      >
                        <div className="flex items-center justify-between flex-wrap gap-2">
                          <div className="flex items-center gap-2 text-xs">
                            <span className="text-slate-400 font-medium">#{index + 1}</span>
                            {row.taskId && (
                              <span className="font-mono text-[11px] text-slate-600">id: {row.taskId}</span>
                            )}
                            <label className="flex items-center gap-1.5 text-slate-400 cursor-pointer">
                              <input
                                type="checkbox"
                                checked={row.enable}
                                disabled={!canSend}
                                onChange={e => patchLoop(row.key, { enable: e.target.checked })}
                                className="w-3.5 h-3.5 rounded border-slate-600 bg-slate-800
                                           text-cyan-500 focus:ring-0 focus:ring-offset-0 cursor-pointer"
                              />
                              启用
                            </label>
                            <span className="text-[11px] text-slate-600">
                              {formatHourMinute(row.sHour, row.sMinute)} ~ {formatHourMinute(row.eHour, row.eMinute)}
                              ｜开 {row.onMins} 分 / 关 {row.offMins} 分
                            </span>
                          </div>
                          <button
                            onClick={() => removeLoop(row.key)}
                            disabled={!canSend}
                            className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-[11px]
                                       text-red-400 border border-red-500/30 bg-red-600/10
                                       hover:bg-red-600/20 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                          >
                            <Trash2 className="w-3 h-3" />
                            删除
                          </button>
                        </div>

                        <div className="grid grid-cols-3 sm:grid-cols-6 gap-2.5">
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">起-时</label>
                            <input
                              type="number" min={0} max={23} value={row.sHour} disabled={!canSend}
                              onChange={e => patchLoop(row.key, { sHour: clampInt(e.target.value, 0, 23, row.sHour) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-cyan-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">起-分</label>
                            <input
                              type="number" min={0} max={59} value={row.sMinute} disabled={!canSend}
                              onChange={e => patchLoop(row.key, { sMinute: clampInt(e.target.value, 0, 59, row.sMinute) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-cyan-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">止-时</label>
                            <input
                              type="number" min={0} max={23} value={row.eHour} disabled={!canSend}
                              onChange={e => patchLoop(row.key, { eHour: clampInt(e.target.value, 0, 23, row.eHour) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-cyan-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">止-分</label>
                            <input
                              type="number" min={0} max={59} value={row.eMinute} disabled={!canSend}
                              onChange={e => patchLoop(row.key, { eMinute: clampInt(e.target.value, 0, 59, row.eMinute) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-cyan-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">开 N 分</label>
                            <input
                              type="number" min={0} max={1440} value={row.onMins} disabled={!canSend}
                              onChange={e => patchLoop(row.key, { onMins: clampInt(e.target.value, 0, 1440, row.onMins) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-cyan-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                          <div>
                            <label className="block text-[11px] text-slate-500 mb-1">关 N 分</label>
                            <input
                              type="number" min={0} max={1440} value={row.offMins} disabled={!canSend}
                              onChange={e => patchLoop(row.key, { offMins: clampInt(e.target.value, 0, 1440, row.offMins) })}
                              className="w-full px-2.5 py-1.5 rounded-md bg-slate-800 border border-slate-700 text-xs text-slate-200
                                         focus:outline-none focus:border-cyan-500/50 disabled:opacity-50 transition-all"
                            />
                          </div>
                        </div>

                        <div className="flex items-center gap-1.5 flex-wrap">
                          <span className="text-[11px] text-slate-500 mr-1">星期</span>
                          {WEEK_DAY_LABELS.map(day => {
                            const checked = row.weekDays.includes(day.value);
                            return (
                              <button
                                key={day.value}
                                onClick={() => toggleLoopWeekDay(row.key, day.value)}
                                disabled={!canSend}
                                className={`w-6 h-6 rounded text-[11px] transition-all disabled:opacity-40 disabled:cursor-not-allowed ${
                                  checked
                                    ? 'bg-cyan-600/30 text-cyan-300 border border-cyan-500/40'
                                    : 'bg-slate-800 text-slate-500 border border-slate-700'
                                }`}
                              >
                                {day.label}
                              </button>
                            );
                          })}
                          <span className="text-[11px] text-slate-600 ml-1">{formatWeekDays(row.weekDays)}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {/* ── 命令日志 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-2">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                    <Activity className="w-4 h-4 text-slate-400" />
                    命令日志
                    <span className="text-xs text-slate-500 font-normal">（最近 {MAX_LOG_ENTRIES} 条）</span>
                  </h3>
                  {commandLog.length > 0 && (
                    <button
                      onClick={() => setCommandLog([])}
                      className="text-xs text-slate-500 hover:text-slate-300 transition-colors"
                    >
                      清空
                    </button>
                  )}
                </div>
                {commandLog.length === 0 ? (
                  <p className="text-xs text-slate-600 text-center py-4">暂无命令下发记录</p>
                ) : (
                  <div className="space-y-1 max-h-[260px] overflow-y-auto">
                    {commandLog.map((entry, idx) => (
                      <div
                        key={idx}
                        className="flex items-start gap-2 px-2.5 py-1.5 rounded-md text-xs
                                   bg-slate-900/40 border border-slate-700/40"
                      >
                        {entry.success
                          ? <CheckCircle className="w-3.5 h-3.5 text-green-400 flex-shrink-0 mt-0.5" />
                          : <AlertCircle className="w-3.5 h-3.5 text-red-400 flex-shrink-0 mt-0.5" />}
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="font-mono text-[11px] text-slate-400">{entry.method}</span>
                            <span className="text-slate-600">{formatLogTime(entry.time)}</span>
                          </div>
                          <div className="text-slate-300 break-all mt-0.5">{entry.message}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {/* ── 整表覆盖二次确认弹窗（仅在已选设备且触发时显示）── */}
              {overwriteDialogOpen && (
                <div
                  className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
                  onClick={() => setOverwriteDialogOpen(false)}
                >
                  <div
                    className="w-full max-w-md rounded-xl bg-slate-800 border border-slate-700 p-5 space-y-4"
                    onClick={e => e.stopPropagation()}
                  >
                    <div className="flex items-center gap-2 text-amber-400">
                      <ShieldAlert className="w-5 h-5" />
                      <h4 className="text-base font-semibold">确认整表覆盖</h4>
                    </div>
                    <p className="text-sm text-slate-300 leading-relaxed">
                      此操作将覆盖设备全部 {slotCount} 个插槽的定时任务。
                      <span className="text-amber-400 font-medium">未列出的插槽其定时任务将被清空</span>，
                      且不可撤销。请确认当前编辑无误后再提交。
                    </p>
                    {hasStaleRow && (
                      <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-400 text-xs">
                        <Clock className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                        <span>部分镜像已超过 24 小时未同步，覆盖后将以页面当前编辑为准。</span>
                      </div>
                    )}
                    <div className="flex items-center justify-end gap-2 pt-1">
                      <button
                        onClick={() => setOverwriteDialogOpen(false)}
                        className="px-4 py-2 rounded-lg text-sm text-slate-300 border border-slate-600
                                   hover:bg-slate-700/50 transition-all"
                      >
                        取消
                      </button>
                      <button
                        onClick={() => { void handleOverwriteAll(); }}
                        disabled={savingAll}
                        className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium
                                   bg-red-600/30 text-red-300 border border-red-500/40
                                   hover:bg-red-600/40 disabled:opacity-50 disabled:cursor-not-allowed transition-all"
                      >
                        {savingAll ? <Loader2 className="w-4 h-4 animate-spin" /> : <Upload className="w-4 h-4" />}
                        确认覆盖
                      </button>
                    </div>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
