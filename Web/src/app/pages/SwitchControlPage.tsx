// T9：安圣开关控制面板。
//
// 【消费的后端端点（T8 已交付并 QA 验收通过）】
//   POST /ansheng/{deviceId}/action              单插槽通断
//   POST /ansheng/{deviceId}/actions             多插槽批量通断（slotNums 为整数数组）
//   GET  /ansheng/{deviceId}/delay-tasks         延时任务镜像
//   POST /ansheng/{deviceId}/delay-tasks/start   开始/配置延时任务
//   POST /ansheng/{deviceId}/delay-tasks/stop    停止延时任务
//   （辅助）GET /ansheng/{deviceId}/profile      能力档案：插槽数量 + 通断快照
//
// 【响应信封】ApiResponse<T> = { code, message, data, timestamp }。
//   业务失败 HTTP 状态恒为 200，靠 code=400 表达；机器可读原因在 data.rejectReason，
//   因后端全局注册了 JsonStringEnumConverter，它是**字符串**（如 "RejectedByKind"）而非整数。
//
// 【绝不复活伪命令】setSwitch / getSwitchStatus / setSwitchConfig / getSwitchConfig
//   四个方法在官方协议 asopen.md 中并不存在，后端端点已物理删除，本页一律不调用。

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Power, PowerOff, Zap, Timer, RefreshCw, Loader2,
  AlertCircle, CheckCircle, XCircle, Cpu, Radio, Hash,
  Play, Square, ListChecks, Clock, ShieldAlert, Activity,
  Search, Info,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';
import { anshengApi } from '@/app/services/api/anshengApi';
import { deviceApi } from '@/app/services/api/deviceApi';
import type {
  AnShengCommandRejectReason,
  AnShengSwitchAction,
  AnShengDelayStartAction,
  AnShengSwitchResultDto,
  AnShengDelayTaskDto,
  AnShengDeviceProfileDto,
} from '@/app/services/api/types/ansheng.types';
import type { DeviceDto } from '@/app/services/api/types/device.types';

// ── 常量 ─────────────────────────────────────────────────────────

/** ApiResponse 的成功码。业务失败为 400（HTTP 仍是 200）。 */
const OK_CODE = 200;

/**
 * 档案未给出插槽数量时的兜底路数。
 * 用户可在页面上手动改选，避免「插槽数未知 ⇒ 整页不可用」。
 */
const DEFAULT_SLOT_COUNT = 4;

/** 可手动选择的插槽路数（仅在档案未给出 slotAmount 时暴露）。 */
const SLOT_COUNT_OPTIONS: number[] = [1, 2, 4, 6, 8, 12, 16];

/**
 * 写后刷新延迟（毫秒）。
 *
 * 【为什么要等】设备权威 + 异步刷新：action/actions 在命令出网即返回，此刻设备应答尚未到达；
 * start/stop 的写后回读由后端在 ≥100ms 后另起作用域触发。立即重查必然读到旧镜像，
 * 故延后一次补刷，把设备真值兜回来。
 */
const REFRESH_DELAY_MS = 1500;

/** 命令日志最大保留条数。 */
const MAX_LOG_ENTRIES = 50;

/** 拒绝原因 → 中文提示。键与后端 AnShengCommandRejectReason 枚举成员名一致。 */
const REJECT_REASON_TEXT: Record<AnShengCommandRejectReason, string> = {
  RejectedByKind: '设备品类不支持该命令（如喇叭类设备无开关能力）',
  RejectedByValidation: '参数校验不通过（含插槽编号越界）',
  RejectedByFirmware: '设备固件版本过低，请先升级固件',
  RejectedByOffline: '设备离线或适配器未连接',
  RejectedByUnknownMethod: '协议目录中不存在该方法，或该方法仅为设备上报事件',
  RejectedByConfirm: '高危命令缺少二次确认',
};

/** 开关动作 → 中文。 */
const ACTION_TEXT: Record<string, string> = {
  on: '开',
  off: '关',
  toggle: '翻转',
  none: '不动作',
};

// ── 本地视图模型 ─────────────────────────────────────────────────

/** 内联结果提示。 */
interface Notice {
  kind: 'success' | 'error';
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

// ── 纯函数工具 ───────────────────────────────────────────────────

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
 * 从 axios 异常里抽取可展示的错误文案。
 * @param err 未知异常对象。
 * @param fallback 兜底文案。
 * @returns 错误文案。
 */
function extractErrorMessage(err: unknown, fallback: string): string {
  const anyErr = err as { response?: { data?: { message?: string } }; message?: string };
  return anyErr?.response?.data?.message || anyErr?.message || fallback;
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
 * 把秒数格式化成「x 分 y 秒」。
 * @param secs 秒数。
 * @returns 可读时长。
 */
function formatSeconds(secs: number): string {
  if (!Number.isFinite(secs) || secs <= 0) {
    return `${secs} 秒`;
  }
  if (secs < 60) {
    return `${secs} 秒`;
  }
  const minutes = Math.floor(secs / 60);
  const rest = secs % 60;
  return rest === 0 ? `${minutes} 分` : `${minutes} 分 ${rest} 秒`;
}

/**
 * 判断一个设备是否像安圣设备（SerialNumber 为纯数字 IMEI）。
 * 与 AnShengManagementPage.loadAnShengDevices 保持同一判据。
 * @param device 设备。
 * @returns 是否为安圣设备。
 */
function isAnShengDevice(device: DeviceDto): boolean {
  return Boolean(device.serialNumber && /^\d{10,20}$/.test(device.serialNumber));
}

// ── 主组件 ───────────────────────────────────────────────────────

/**
 * 安圣开关控制面板。
 *
 * 页面可见需 VIEW_DEVICES；所有下发动作需 SEND_DEVICE_COMMANDS，
 * 缺权限时按钮 disabled 而非隐藏，让用户知道「功能存在但你没权限」。
 */
export function SwitchControlPage() {
  const { hasPermission } = useAuth();
  const canView = hasPermission(PERMISSIONS.VIEW_DEVICES);
  const canSend = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS);

  // ── 设备列表 ───────────────────────────────────────────────
  const [devices, setDevices] = useState<DeviceDto[]>([]);
  const [devicesLoading, setDevicesLoading] = useState<boolean>(false);
  const [devicesError, setDevicesError] = useState<string | null>(null);
  const [deviceSearch, setDeviceSearch] = useState<string>('');
  const [selectedDeviceId, setSelectedDeviceId] = useState<number | null>(null);

  // ── 插槽矩阵（通断态来自 Profile.SlotsSnapshot）─────────────
  const [profile, setProfile] = useState<AnShengDeviceProfileDto | null>(null);
  const [slots, setSlots] = useState<number[]>([]);
  const [slotsAt, setSlotsAt] = useState<string | null>(null);
  const [profileLoading, setProfileLoading] = useState<boolean>(false);
  const [manualSlotCount, setManualSlotCount] = useState<number>(DEFAULT_SLOT_COUNT);
  const [selectedSlots, setSelectedSlots] = useState<number[]>([]);
  const [busySlot, setBusySlot] = useState<number | null>(null);
  const [batchBusy, setBatchBusy] = useState<boolean>(false);
  const [hasStopDelayTask, setHasStopDelayTask] = useState<boolean>(false);

  // ── 延时任务 ───────────────────────────────────────────────
  const [delayTasks, setDelayTasks] = useState<AnShengDelayTaskDto[]>([]);
  const [delayLoading, setDelayLoading] = useState<boolean>(false);
  const [delayBusySlot, setDelayBusySlot] = useState<number | null>(null);
  const [startBusy, setStartBusy] = useState<boolean>(false);
  const [delaySlotNum, setDelaySlotNum] = useState<number>(1);
  const [delayEnable, setDelayEnable] = useState<boolean>(true);
  const [delayStartAction, setDelayStartAction] = useState<AnShengDelayStartAction>('on');
  const [delayEndAction, setDelayEndAction] = useState<AnShengSwitchAction>('off');
  const [delaySecs, setDelaySecs] = useState<string>('60');

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

  // ── 加载能力档案（插槽数量 + 通断快照）─────────────────────
  const loadProfile = useCallback(async (deviceId: number, silent = false): Promise<void> => {
    if (!silent) {
      setProfileLoading(true);
    }
    try {
      const response = await anshengApi.getProfile(deviceId);
      if (response.data.code === OK_CODE && response.data.data) {
        const dto = response.data.data;
        setProfile(dto);
        setSlots(Array.isArray(dto.slots) ? dto.slots : []);
        setSlotsAt(dto.slotsSnapshotAt ?? null);
      } else if (!silent) {
        // 档案缺失不是致命错误：设备可能尚未探测过，矩阵按兜底路数渲染即可。
        setProfile(null);
        setSlots([]);
        setSlotsAt(null);
      }
    } catch (err: unknown) {
      if (!silent) {
        setProfile(null);
        setSlots([]);
        setSlotsAt(null);
        appendLog('getProfile', false, extractErrorMessage(err, '读取设备档案失败'));
      }
    } finally {
      if (!silent) {
        setProfileLoading(false);
      }
    }
  }, [appendLog]);

  // ── 加载延时任务镜像 ───────────────────────────────────────
  const loadDelayTasks = useCallback(async (deviceId: number, silent = false): Promise<void> => {
    if (!silent) {
      setDelayLoading(true);
    }
    try {
      const response = await anshengApi.getDelayTasks(deviceId);
      if (response.data.code === OK_CODE && Array.isArray(response.data.data)) {
        setDelayTasks(response.data.data);
      } else if (!silent) {
        setDelayTasks([]);
      }
    } catch (err: unknown) {
      if (!silent) {
        setDelayTasks([]);
        appendLog('getDelayTasks', false, extractErrorMessage(err, '读取延时任务镜像失败'));
      }
    } finally {
      if (!silent) {
        setDelayLoading(false);
      }
    }
  }, [appendLog]);

  /**
   * 延后一次静默补刷（设备权威 + 异步刷新，见 REFRESH_DELAY_MS 注释）。
   * @param deviceId 设备主键。
   * @param withDelayTasks 是否同时补刷延时任务镜像。
   */
  const scheduleRefresh = useCallback((deviceId: number, withDelayTasks: boolean): void => {
    const timerId = window.setTimeout(() => {
      void loadProfile(deviceId, true);
      if (withDelayTasks) {
        void loadDelayTasks(deviceId, true);
      }
    }, REFRESH_DELAY_MS);
    refreshTimersRef.current.push(timerId);
  }, [loadProfile, loadDelayTasks]);

  // ── 初始加载 ───────────────────────────────────────────────
  useEffect(() => {
    void loadDevices();
  }, [loadDevices]);

  // ── 切换设备：重置面板并拉取该设备数据 ─────────────────────
  useEffect(() => {
    clearRefreshTimers();
    setSelectedSlots([]);
    setNotice(null);
    setProfile(null);
    setSlots([]);
    setSlotsAt(null);
    setDelayTasks([]);
    setDelaySlotNum(1);

    if (selectedDeviceId === null) {
      return;
    }
    void loadProfile(selectedDeviceId);
    void loadDelayTasks(selectedDeviceId);
  }, [selectedDeviceId, loadProfile, loadDelayTasks, clearRefreshTimers]);

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
   * 插槽路数是否由设备档案给出（否则提供手动兜底值）。
   *
   * 【关键】delayTasks 是**稀疏集合**（只含曾经配置过延时任务的插槽行，见
   * AnShengScheduleService.GetDelayTasksAsync 的 Where(DeviceId) 查询），其 length
   * 不等于插槽路数，绝不能据此推断路数，否则稀疏镜像会让 slotCountKnown 误判为 true、
   * 隐藏手动改路数的逃生通道并漏渲染插槽。故「已知路数」只认两个权威源：
   * 档案 slotAmount 与 slots 快照。
   */
  const slotCountKnown = useMemo<boolean>(() => {
    const fromProfile = profile?.slotAmount ?? 0;
    return fromProfile > 0 || slots.length > 0;
  }, [profile, slots]);

  /**
   * 最终渲染的插槽路数。
   *
   * delayTasks 为稀疏集合，用「最大 slotNum」而非 length 作为上界，避免漏渲染插槽。
   */
  const slotCount = useMemo<number>(() => {
    const maxTaskSlot = delayTasks.reduce((max, task) => Math.max(max, task.slotNum), 0);
    const resolved = Math.max(profile?.slotAmount ?? 0, slots.length, maxTaskSlot);
    return resolved > 0 ? resolved : manualSlotCount;
  }, [profile, slots, delayTasks, manualSlotCount]);

  /** 插槽编号列表（1..slotCount）。 */
  const slotNumbers = useMemo<number[]>(
    () => Array.from({ length: slotCount }, (_, i) => i + 1),
    [slotCount]
  );

  /** slotNum → 延时任务镜像。 */
  const delayTaskBySlot = useMemo<Map<number, AnShengDelayTaskDto>>(() => {
    const map = new Map<number, AnShengDelayTaskDto>();
    delayTasks.forEach(task => map.set(task.slotNum, task));
    return map;
  }, [delayTasks]);

  /** 是否存在陈旧镜像（>24h 未与设备同步）。 */
  const hasStaleTask = useMemo<boolean>(
    () => delayTasks.some(task => task.isStale),
    [delayTasks]
  );

  /**
   * 读取某插槽的通断态。
   * 【下标约定】slots[i] 对应 slotNum = i + 1（设计 §7.7）。
   * @param slotNum 插槽编号（从 1 开始）。
   * @returns 1=开，0=关，null=未知（尚未收到过带 slots[] 的应答）。
   */
  const getSlotState = useCallback((slotNum: number): number | null => {
    const value = slots[slotNum - 1];
    return typeof value === 'number' ? value : null;
  }, [slots]);

  // ── 动作处理 ───────────────────────────────────────────────

  /**
   * 消化一次开关动作结果：更新插槽快照、写日志、给出提示。
   * @param method 协议方法名（action / actions）。
   * @param code 信封状态码。
   * @param envelopeMessage 信封 message。
   * @param result 下发结果 DTO。
   * @param successTitle 成功提示标题。
   * @returns 是否成功受理。
   */
  const consumeSwitchResult = useCallback((
    method: string,
    code: number,
    envelopeMessage: string,
    result: AnShengSwitchResultDto | null | undefined,
    successTitle: string
  ): boolean => {
    // 无论成败，后端都会带回当前插槽快照（形状稳定），能用就先用。
    if (result && Array.isArray(result.slots)) {
      setSlots(result.slots);
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
    setNotice({ kind: 'error', title: '下发被拒绝', detail });
    appendLog(method, false, detail);
    return false;
  }, [appendLog]);

  /**
   * 消化一次延时任务结果。
   * @param method 协议方法名（startDelayTask / stopDelayTask）。
   * @param code 信封状态码。
   * @param envelopeMessage 信封 message。
   * @param tasks 乐观镜像快照。
   * @param accepted 平台是否受理。
   * @param rejectReason 机器可读拒绝原因。
   * @param errorMessage 人类可读失败原因。
   * @param successTitle 成功提示标题。
   * @returns 是否成功受理。
   */
  const consumeDelayResult = useCallback((
    method: string,
    code: number,
    envelopeMessage: string,
    tasks: AnShengDelayTaskDto[] | null | undefined,
    accepted: boolean,
    rejectReason: AnShengCommandRejectReason | null | undefined,
    errorMessage: string | null | undefined,
    successTitle: string
  ): boolean => {
    if (Array.isArray(tasks)) {
      setDelayTasks(tasks);
    }

    if (code === OK_CODE && accepted) {
      setNotice({ kind: 'success', title: successTitle, detail: envelopeMessage });
      appendLog(method, true, successTitle);
      return true;
    }

    const detail = buildFailureDetail(rejectReason, errorMessage, envelopeMessage);
    setNotice({ kind: 'error', title: '延时任务下发被拒绝', detail });
    appendLog(method, false, detail);
    return false;
  }, [appendLog]);

  /**
   * 单插槽通断。
   * @param slotNum 插槽编号。
   * @param action 动作。
   */
  const handleSlotAction = useCallback(async (
    slotNum: number,
    action: AnShengSwitchAction
  ): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setBusySlot(slotNum);
    setNotice(null);
    try {
      const response = await anshengApi.switchAction(selectedDeviceId, {
        slotNum,
        action,
        ...(hasStopDelayTask ? { hasStopDelayTask: true } : {}),
      });
      consumeSwitchResult(
        'action',
        response.data.code,
        response.data.message,
        response.data.data,
        `插槽 ${slotNum} 已下发「${ACTION_TEXT[action] ?? action}」`
      );
      scheduleRefresh(selectedDeviceId, hasStopDelayTask);
    } catch (err: unknown) {
      const message = extractErrorMessage(err, '开关动作请求失败');
      setNotice({ kind: 'error', title: '请求失败', detail: message });
      appendLog('action', false, message);
    } finally {
      setBusySlot(null);
    }
  }, [selectedDeviceId, canSend, hasStopDelayTask, consumeSwitchResult, scheduleRefresh, appendLog]);

  /**
   * 批量通断。slotNums 以**整数数组**下发（验收 #2 断言点）。
   * @param action 动作。
   */
  const handleBatchAction = useCallback(async (action: AnShengSwitchAction): Promise<void> => {
    if (selectedDeviceId === null || !canSend || selectedSlots.length === 0) {
      return;
    }
    setBatchBusy(true);
    setNotice(null);
    // 升序去重，保证报文里的 slotNums 稳定可读。
    const slotNums = Array.from(new Set(selectedSlots)).sort((a, b) => a - b);
    try {
      const response = await anshengApi.switchActions(selectedDeviceId, {
        slotNums,
        action,
        ...(hasStopDelayTask ? { hasStopDelayTask: true } : {}),
      });
      consumeSwitchResult(
        'actions',
        response.data.code,
        response.data.message,
        response.data.data,
        `插槽 [${slotNums.join(', ')}] 已下发「${ACTION_TEXT[action] ?? action}」`
      );
      scheduleRefresh(selectedDeviceId, hasStopDelayTask);
    } catch (err: unknown) {
      const message = extractErrorMessage(err, '批量开关动作请求失败');
      setNotice({ kind: 'error', title: '请求失败', detail: message });
      appendLog('actions', false, message);
    } finally {
      setBatchBusy(false);
    }
  }, [
    selectedDeviceId, canSend, selectedSlots, hasStopDelayTask,
    consumeSwitchResult, scheduleRefresh, appendLog,
  ]);

  /** 启动 / 配置延时任务。 */
  const handleStartDelayTask = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    const secs = Number.parseInt(delaySecs, 10);
    if (!Number.isFinite(secs) || secs <= 0) {
      setNotice({ kind: 'error', title: '参数不合法', detail: '延时秒数必须为大于 0 的整数' });
      return;
    }
    setStartBusy(true);
    setNotice(null);
    try {
      const response = await anshengApi.startDelayTask(selectedDeviceId, {
        slotNum: delaySlotNum,
        enable: delayEnable,
        sAction: delayStartAction,
        eAction: delayEndAction,
        secs,
      });
      const result = response.data.data;
      consumeDelayResult(
        'startDelayTask',
        response.data.code,
        response.data.message,
        result?.tasks,
        Boolean(result?.accepted),
        result?.rejectReason,
        result?.errorMessage,
        `插槽 ${delaySlotNum} 延时任务已下发（${formatSeconds(secs)}）`
      );
      scheduleRefresh(selectedDeviceId, true);
    } catch (err: unknown) {
      const message = extractErrorMessage(err, '延时任务下发失败');
      setNotice({ kind: 'error', title: '请求失败', detail: message });
      appendLog('startDelayTask', false, message);
    } finally {
      setStartBusy(false);
    }
  }, [
    selectedDeviceId, canSend, delaySlotNum, delayEnable,
    delayStartAction, delayEndAction, delaySecs,
    consumeDelayResult, scheduleRefresh, appendLog,
  ]);

  /**
   * 停止某插槽的延时任务。
   * @param slotNum 插槽编号。
   */
  const handleStopDelayTask = useCallback(async (slotNum: number): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setDelayBusySlot(slotNum);
    setNotice(null);
    try {
      const response = await anshengApi.stopDelayTask(selectedDeviceId, { slotNum });
      const result = response.data.data;
      consumeDelayResult(
        'stopDelayTask',
        response.data.code,
        response.data.message,
        result?.tasks,
        Boolean(result?.accepted),
        result?.rejectReason,
        result?.errorMessage,
        `插槽 ${slotNum} 延时任务已停止`
      );
      scheduleRefresh(selectedDeviceId, true);
    } catch (err: unknown) {
      const message = extractErrorMessage(err, '停止延时任务失败');
      setNotice({ kind: 'error', title: '请求失败', detail: message });
      appendLog('stopDelayTask', false, message);
    } finally {
      setDelayBusySlot(null);
    }
  }, [selectedDeviceId, canSend, consumeDelayResult, scheduleRefresh, appendLog]);

  /** 手动同步：重新拉取档案与延时任务镜像。 */
  const handleManualSync = useCallback((): void => {
    if (selectedDeviceId === null) {
      return;
    }
    void loadProfile(selectedDeviceId);
    void loadDelayTasks(selectedDeviceId);
  }, [selectedDeviceId, loadProfile, loadDelayTasks]);

  /**
   * 切换某插槽的多选状态。
   * @param slotNum 插槽编号。
   */
  const toggleSlotSelection = useCallback((slotNum: number): void => {
    setSelectedSlots(prev =>
      prev.includes(slotNum) ? prev.filter(n => n !== slotNum) : [...prev, slotNum]
    );
  }, []);

  /** 全选 / 全不选。 */
  const toggleSelectAll = useCallback((): void => {
    setSelectedSlots(prev => (prev.length === slotNumbers.length ? [] : [...slotNumbers]));
  }, [slotNumbers]);

  // ── 无查看权限：直接门控整页 ───────────────────────────────
  if (!canView) {
    return (
      <div className="p-6">
        <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
          <ShieldAlert className="w-12 h-12 text-amber-500/70 mx-auto mb-3" />
          <p className="text-slate-300 text-sm font-medium">无权访问「安圣开关控制」</p>
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
          <div className="p-2 rounded-lg bg-emerald-500/20 border border-emerald-500/30">
            <Power className="w-6 h-6 text-emerald-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-white">安圣开关控制</h1>
            <p className="text-sm text-slate-400">插槽通断 · 批量动作 · 延时任务</p>
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
            disabled={selectedDeviceId === null || profileLoading || delayLoading}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-emerald-600/20 border border-emerald-500/30
                       text-emerald-400 hover:bg-emerald-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
          >
            <RefreshCw className={`w-4 h-4 ${(profileLoading || delayLoading) ? 'animate-spin' : ''}`} />
            手动同步
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        {/* ────────────── 左栏：设备选择 ────────────── */}
        <div className="space-y-4">
          <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-3">
            <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
              <Cpu className="w-4 h-4 text-emerald-400" />
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
                           placeholder:text-slate-500 focus:outline-none focus:border-emerald-500/50 transition-all"
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
                          ? 'bg-emerald-600/20 border border-emerald-500/30 text-emerald-300'
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

          {/* 设备档案摘要 */}
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
              ) : profile ? (
                <dl className="space-y-1.5 text-xs">
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">品类</dt>
                    <dd className="text-slate-300">{profile.kindName || profile.kind}</dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">插槽数</dt>
                    <dd className="text-slate-300">{profile.slotAmount ?? '未知'}</dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">固件</dt>
                    <dd className="text-slate-300 font-mono">{profile.version || '—'}</dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">联网</dt>
                    <dd className="text-slate-300">{profile.netType || '—'}</dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-slate-500">快照时间</dt>
                    <dd className="text-slate-400">{formatDateTime(slotsAt)}</dd>
                  </div>
                </dl>
              ) : (
                <p className="text-xs text-slate-500 py-1">暂无档案（设备可能尚未探测）</p>
              )}
            </div>
          )}
        </div>

        {/* ────────────── 右栏：插槽矩阵 + 延时任务 + 日志 ────────────── */}
        <div className="xl:col-span-2 space-y-4">
          {selectedDeviceId === null ? (
            <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
              <Cpu className="w-12 h-12 text-slate-600 mx-auto mb-3" />
              <p className="text-slate-500 text-sm">请先选择一个安圣设备</p>
              <p className="text-slate-600 text-xs mt-1">选择设备后即可查看插槽状态并下发开关 / 延时任务</p>
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

              {/* ── 插槽矩阵 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-4">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                    <Zap className="w-4 h-4 text-amber-400" />
                    插槽矩阵
                    {selectedDevice && (
                      <span className="text-xs text-slate-500 font-normal">
                        · {selectedDevice.name}
                      </span>
                    )}
                  </h3>
                  <div className="flex items-center gap-2">
                    {!slotCountKnown && (
                      <label className="flex items-center gap-1.5 text-xs text-slate-500">
                        路数
                        <select
                          value={manualSlotCount}
                          onChange={e => setManualSlotCount(Number(e.target.value))}
                          className="px-2 py-1 rounded bg-slate-900/60 border border-slate-700 text-slate-300
                                     focus:outline-none focus:border-emerald-500/50"
                        >
                          {SLOT_COUNT_OPTIONS.map(n => (
                            <option key={n} value={n}>{n}</option>
                          ))}
                        </select>
                      </label>
                    )}
                    <button
                      onClick={toggleSelectAll}
                      className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs
                                 text-slate-400 border border-slate-700/50 hover:text-slate-200 hover:bg-slate-700/50 transition-all"
                    >
                      <ListChecks className="w-3.5 h-3.5" />
                      {selectedSlots.length === slotNumbers.length ? '全不选' : '全选'}
                    </button>
                  </div>
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

                {slots.length === 0 && (
                  <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-slate-700/20 border border-slate-700/50 text-slate-400 text-xs">
                    <Info className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                    <span>
                      尚未收到设备的插槽通断快照（设备权威 + 异步刷新）。
                      下发一次动作或点「手动同步」后即可看到真值。
                    </span>
                  </div>
                )}

                <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
                  {slotNumbers.map(slotNum => {
                    const state = getSlotState(slotNum);
                    const checked = selectedSlots.includes(slotNum);
                    const busy = busySlot === slotNum;
                    const task = delayTaskBySlot.get(slotNum);
                    return (
                      <motion.div
                        key={slotNum}
                        initial={{ opacity: 0, y: 8 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ delay: slotNum * 0.02 }}
                        className={`rounded-lg border p-3 space-y-2.5 transition-all ${
                          checked
                            ? 'bg-emerald-600/10 border-emerald-500/40'
                            : 'bg-slate-900/40 border-slate-700/50'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <label className="flex items-center gap-2 cursor-pointer">
                            <input
                              type="checkbox"
                              checked={checked}
                              onChange={() => toggleSlotSelection(slotNum)}
                              className="w-3.5 h-3.5 rounded border-slate-600 bg-slate-800
                                         text-emerald-500 focus:ring-0 focus:ring-offset-0 cursor-pointer"
                            />
                            <span className="text-sm font-medium text-slate-200 inline-flex items-center gap-1">
                              <Hash className="w-3 h-3 text-slate-500" />
                              插槽 {slotNum}
                            </span>
                          </label>
                          {state === 1 ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium
                                             bg-green-500/20 text-green-400 border border-green-500/30">
                              <Power className="w-3 h-3" /> 开
                            </span>
                          ) : state === 0 ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium
                                             bg-slate-600/30 text-slate-400 border border-slate-600/40">
                              <PowerOff className="w-3 h-3" /> 关
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium
                                             bg-slate-700/30 text-slate-500 border border-slate-700/50">
                              <XCircle className="w-3 h-3" /> 未知
                            </span>
                          )}
                        </div>

                        {task?.enable && (
                          <div className="flex items-center gap-1 text-[11px] text-cyan-400/90">
                            <Timer className="w-3 h-3" />
                            延时 {formatSeconds(task.secs)} 后「{ACTION_TEXT[task.eAction] ?? task.eAction}」
                          </div>
                        )}

                        <div className="flex items-center gap-1.5">
                          <button
                            onClick={() => { void handleSlotAction(slotNum, 'on'); }}
                            disabled={!canSend || busy}
                            className="flex-1 inline-flex items-center justify-center gap-1 px-2 py-1.5 rounded-md text-xs font-medium
                                       bg-green-600/20 text-green-400 border border-green-500/30
                                       hover:bg-green-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                          >
                            {busy ? <Loader2 className="w-3 h-3 animate-spin" /> : <Power className="w-3 h-3" />}
                            开
                          </button>
                          <button
                            onClick={() => { void handleSlotAction(slotNum, 'off'); }}
                            disabled={!canSend || busy}
                            className="flex-1 inline-flex items-center justify-center gap-1 px-2 py-1.5 rounded-md text-xs font-medium
                                       bg-red-600/20 text-red-400 border border-red-500/30
                                       hover:bg-red-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                          >
                            {busy ? <Loader2 className="w-3 h-3 animate-spin" /> : <PowerOff className="w-3 h-3" />}
                            关
                          </button>
                        </div>
                      </motion.div>
                    );
                  })}
                </div>

                {/* 批量动作条 */}
                <div className="flex items-center justify-between flex-wrap gap-3 pt-3 border-t border-slate-700/50">
                  <div className="flex items-center gap-3 flex-wrap">
                    <span className="text-xs text-slate-500">
                      已选 <span className="text-emerald-400 font-medium">{selectedSlots.length}</span> 个插槽
                      {selectedSlots.length > 0 && (
                        <span className="ml-1 font-mono text-slate-400">
                          [{Array.from(new Set(selectedSlots)).sort((a, b) => a - b).join(', ')}]
                        </span>
                      )}
                    </span>
                    <label className="flex items-center gap-1.5 text-xs text-slate-400 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={hasStopDelayTask}
                        onChange={e => setHasStopDelayTask(e.target.checked)}
                        className="w-3.5 h-3.5 rounded border-slate-600 bg-slate-800
                                   text-emerald-500 focus:ring-0 focus:ring-offset-0 cursor-pointer"
                      />
                      同时停止延时任务
                    </label>
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => { void handleBatchAction('on'); }}
                      disabled={!canSend || batchBusy || selectedSlots.length === 0}
                      className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-medium
                                 bg-green-600/25 text-green-400 border border-green-500/30
                                 hover:bg-green-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                    >
                      {batchBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Power className="w-3.5 h-3.5" />}
                      批量开
                    </button>
                    <button
                      onClick={() => { void handleBatchAction('off'); }}
                      disabled={!canSend || batchBusy || selectedSlots.length === 0}
                      className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-medium
                                 bg-red-600/25 text-red-400 border border-red-500/30
                                 hover:bg-red-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                    >
                      {batchBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <PowerOff className="w-3.5 h-3.5" />}
                      批量关
                    </button>
                  </div>
                </div>
              </div>

              {/* ── 延时任务面板 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-4">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                    <Timer className="w-4 h-4 text-cyan-400" />
                    延时任务
                    <span className="text-xs text-slate-500 font-normal">（平台镜像，非实时穿透）</span>
                  </h3>
                  <button
                    onClick={() => { if (selectedDeviceId !== null) { void loadDelayTasks(selectedDeviceId); } }}
                    disabled={delayLoading}
                    className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs
                               text-slate-400 border border-slate-700/50 hover:text-slate-200 hover:bg-slate-700/50
                               disabled:opacity-40 transition-all"
                  >
                    <RefreshCw className={`w-3.5 h-3.5 ${delayLoading ? 'animate-spin' : ''}`} />
                    刷新镜像
                  </button>
                </div>

                {hasStaleTask && (
                  <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-400 text-xs">
                    <Clock className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                    <span>部分镜像超过 24 小时未与设备同步，建议点「手动同步」核对。</span>
                  </div>
                )}

                {/* 启动延时任务表单 */}
                <div className="rounded-lg bg-slate-900/40 border border-slate-700/50 p-4 space-y-3">
                  <div className="text-xs font-medium text-slate-500 uppercase tracking-wider">启动 / 配置延时任务</div>
                  <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">插槽编号</label>
                      <select
                        value={delaySlotNum}
                        onChange={e => setDelaySlotNum(Number(e.target.value))}
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700 text-sm text-slate-200
                                   focus:outline-none focus:border-cyan-500/50 transition-all"
                      >
                        <option value={0}>0（全部插槽）</option>
                        {slotNumbers.map(n => (
                          <option key={n} value={n}>{n}</option>
                        ))}
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">启用</label>
                      <select
                        value={delayEnable ? 'true' : 'false'}
                        onChange={e => setDelayEnable(e.target.value === 'true')}
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700 text-sm text-slate-200
                                   focus:outline-none focus:border-cyan-500/50 transition-all"
                      >
                        <option value="true">启用</option>
                        <option value="false">停用</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">开始动作</label>
                      <select
                        value={delayStartAction}
                        onChange={e => setDelayStartAction(e.target.value as AnShengDelayStartAction)}
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700 text-sm text-slate-200
                                   focus:outline-none focus:border-cyan-500/50 transition-all"
                      >
                        <option value="on">on（开）</option>
                        <option value="off">off（关）</option>
                        <option value="toggle">toggle（翻转）</option>
                        <option value="none">none（不动作）</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">结束动作</label>
                      <select
                        value={delayEndAction}
                        onChange={e => setDelayEndAction(e.target.value as AnShengSwitchAction)}
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700 text-sm text-slate-200
                                   focus:outline-none focus:border-cyan-500/50 transition-all"
                      >
                        <option value="on">on（开）</option>
                        <option value="off">off（关）</option>
                        <option value="toggle">toggle（翻转）</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">延时秒数</label>
                      <input
                        type="number"
                        min={1}
                        value={delaySecs}
                        onChange={e => setDelaySecs(e.target.value)}
                        placeholder="60"
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700 text-sm text-slate-200
                                   placeholder:text-slate-600 focus:outline-none focus:border-cyan-500/50 transition-all"
                      />
                    </div>
                  </div>
                  <button
                    onClick={() => { void handleStartDelayTask(); }}
                    disabled={!canSend || startBusy}
                    className="inline-flex items-center gap-2 px-5 py-2 rounded-lg text-sm font-medium
                               bg-cyan-600/25 text-cyan-400 border border-cyan-500/30
                               hover:bg-cyan-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {startBusy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Play className="w-4 h-4" />}
                    启动延时任务
                  </button>
                </div>

                {/* 延时任务镜像表 */}
                <div className="rounded-lg border border-slate-700/50 overflow-hidden">
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="border-b border-slate-700/50 bg-slate-800/60">
                          <th className="text-left px-3 py-2.5 text-slate-400 font-medium text-xs">插槽</th>
                          <th className="text-left px-3 py-2.5 text-slate-400 font-medium text-xs">状态</th>
                          <th className="text-left px-3 py-2.5 text-slate-400 font-medium text-xs">开始动作</th>
                          <th className="text-left px-3 py-2.5 text-slate-400 font-medium text-xs">结束动作</th>
                          <th className="text-left px-3 py-2.5 text-slate-400 font-medium text-xs">延时</th>
                          <th className="text-left px-3 py-2.5 text-slate-400 font-medium text-xs">计数</th>
                          <th className="text-left px-3 py-2.5 text-slate-400 font-medium text-xs">同步时间</th>
                          <th className="text-right px-3 py-2.5 text-slate-400 font-medium text-xs">操作</th>
                        </tr>
                      </thead>
                      <tbody>
                        {delayLoading ? (
                          <tr>
                            <td colSpan={8} className="px-3 py-8 text-center">
                              <Loader2 className="w-5 h-5 animate-spin text-slate-400 mx-auto mb-2" />
                              <span className="text-slate-400 text-xs">加载镜像...</span>
                            </td>
                          </tr>
                        ) : delayTasks.length === 0 ? (
                          <tr>
                            <td colSpan={8} className="px-3 py-8 text-center">
                              <Timer className="w-8 h-8 text-slate-600 mx-auto mb-2" />
                              <div className="text-slate-500 text-xs">暂无延时任务镜像</div>
                              <div className="text-slate-600 text-[11px] mt-0.5">
                                启动一次延时任务后，平台会写入乐观镜像并在写后回读时用设备真值覆盖
                              </div>
                            </td>
                          </tr>
                        ) : (
                          delayTasks.map(task => (
                            <tr
                              key={task.slotNum}
                              className="border-b border-slate-800/50 hover:bg-slate-800/30 transition-colors"
                            >
                              <td className="px-3 py-2.5 text-slate-200 font-mono">#{task.slotNum}</td>
                              <td className="px-3 py-2.5">
                                {task.enable ? (
                                  <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium
                                                   bg-cyan-500/20 text-cyan-400 border border-cyan-500/30">
                                    <Activity className="w-3 h-3" /> 运行中
                                  </span>
                                ) : (
                                  <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium
                                                   bg-slate-600/30 text-slate-400 border border-slate-600/40">
                                    <Square className="w-3 h-3" /> 已停用
                                  </span>
                                )}
                              </td>
                              <td className="px-3 py-2.5 text-slate-300 text-xs">
                                {ACTION_TEXT[task.sAction] ?? task.sAction}
                              </td>
                              <td className="px-3 py-2.5 text-slate-300 text-xs">
                                {ACTION_TEXT[task.eAction] ?? task.eAction}
                              </td>
                              <td className="px-3 py-2.5 text-slate-300 text-xs">{formatSeconds(task.secs)}</td>
                              <td className="px-3 py-2.5 text-slate-400 text-xs">{task.cnt}</td>
                              <td className="px-3 py-2.5 text-xs">
                                <span className={task.isStale ? 'text-amber-400' : 'text-slate-400'}>
                                  {formatDateTime(task.syncedAt)}
                                </span>
                                {task.isStale && (
                                  <span className="ml-1.5 text-[10px] text-amber-500/80">陈旧</span>
                                )}
                              </td>
                              <td className="px-3 py-2.5 text-right">
                                <button
                                  onClick={() => { void handleStopDelayTask(task.slotNum); }}
                                  disabled={!canSend || delayBusySlot === task.slotNum}
                                  className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium
                                             bg-red-600/20 text-red-400 border border-red-500/30
                                             hover:bg-red-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                                >
                                  {delayBusySlot === task.slotNum
                                    ? <Loader2 className="w-3 h-3 animate-spin" />
                                    : <Square className="w-3 h-3" />}
                                  停止
                                </button>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>

              {/* ── 命令日志 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2 mb-3">
                  <Activity className="w-4 h-4 text-purple-400" />
                  命令日志
                  <span className="text-xs text-slate-500 font-normal">（最近 {MAX_LOG_ENTRIES} 条）</span>
                </h3>
                {commandLog.length === 0 ? (
                  <p className="text-sm text-slate-600 text-center py-4">暂无命令记录</p>
                ) : (
                  <div className="space-y-1.5 max-h-[260px] overflow-y-auto">
                    {commandLog.map((log, idx) => (
                      <div
                        key={`${log.time.getTime()}-${idx}`}
                        className="flex items-start gap-2 px-3 py-1.5 rounded text-xs bg-slate-700/20"
                      >
                        <span className="text-slate-500 font-mono flex-shrink-0">{formatLogTime(log.time)}</span>
                        <span className={`w-2 h-2 rounded-full flex-shrink-0 mt-1 ${log.success ? 'bg-green-400' : 'bg-red-400'}`} />
                        <span className="text-slate-300 font-medium flex-shrink-0">{log.method}</span>
                        <span className={`${log.success ? 'text-green-400/80' : 'text-red-400/80'} break-all`}>
                          {log.message}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

export default SwitchControlPage;

