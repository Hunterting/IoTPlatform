// T11：安圣电量计面板。
//
// 【消费的后端端点（T11 已交付并 QA 验收通过）】
//   POST /ansheng/{deviceId}/energy/realtime           拉取实时读数（getEMRealtime）
//   POST /ansheng/{deviceId}/energy/statistics/refresh 下发统计采集（getEMStatistics）
//   POST /ansheng/{deviceId}/energy/statistics/clear   清设备侧统计（clearEMStatistics）
//   GET  /ansheng/{deviceId}/energy/statistics          读平台聚合表（设备权威镜像）
//   GET  /ansheng/{deviceId}/energy/cal-params         读校准参数（getCalParams）
//   POST /ansheng/{deviceId}/energy/cal-params         设校准参数（setCalParams）
//   POST /ansheng/{deviceId}/energy/cal-params/reset   重置校准参数
//   POST /ansheng/{deviceId}/energy/cal-params/auto    按已知负载自动校准
//
// 【响应信封（铁律②）】ApiResponse<T> = { code, message, data, timestamp }。
//   电量计**没有乐观并发令牌**，八个端点结果**全部 HTTP 200**：
//   成功 code=200，被拒 code=400 + data.rejectReason（喇叭类为 "RejectedByKind"）。
//   **绝不**读 data.success。T11 不存在 409，无需 catch 并发冲突分支。
//
// 【枚举按字符串分支（铁律④）】granularity 是 'Total'|'HourSum'|'Hour'|'Day'|'Month'，不按整数。
//
// 【权限门控】GET /energy/statistics 走 VIEW_DEVICES（只读镜像，菜单即可进入）；
//   GET/POST /energy/cal-params 与其余写类动作（realtime/refresh/clear/setCalParams/reset/auto）
//   均走 SEND_DEVICE_COMMANDS，按钮 disabled 而非隐藏（与 T9/T10 同口径）。
//   注：cal-params 的 GET 是「读参数」但后端归为命令语义（需 SEND_DEVICE_COMMANDS），
//       页面 handleReadCalParams 仅在 !canSend 守卫后才可调用，故不属 VIEW_DEVICES。

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Zap, Gauge, RefreshCw, Trash2, Loader2,
  AlertCircle, CheckCircle, Cpu, Radio, Hash,
  Clock, SlidersHorizontal, Info,
  ShieldAlert, Search, Activity, RotateCcw, Send,
  Plus, Table2, Power, Wrench, CircleSlash2, Save,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';
import { anshengApi } from '@/app/services/api/anshengApi';
import { deviceApi } from '@/app/services/api/deviceApi';
import type {
  AnShengCommandRejectReason,
  AnShengDeviceProfileDto,
  AnShengEmGranularity,
  AnShengEmStatisticDto,
  AnShengEnergyResultDto,
  AnShengGetEMStatisticsRequest,
  AnShengClearEMStatisticsRequest,
  AnShengSetCalParamsRequest,
  AnShengAutoCalRequest,
  AnShengEnergyStatisticsQueryParams,
} from '@/app/services/api/types/ansheng.types';
import type { DeviceDto } from '@/app/services/api/types/device.types';

// ── 常量 ─────────────────────────────────────────────────────────

/** ApiResponse 的成功码。电量计被拒时 HTTP 仍是 200，靠 code=400 表达。 */
const OK_CODE = 200;

/** 档案未给出插槽数量时的兜底路数。 */
const DEFAULT_SLOT_COUNT = 4;

/** 可手动选择的插槽路数（仅在档案未给出 slotAmount 时暴露）。 */
const SLOT_COUNT_OPTIONS: number[] = [1, 2, 4, 6, 8, 12, 16];

/**
 * 写后刷新延迟（毫秒）。
 *
 * 【为什么要等】refresh 只是下发采集命令，真值由设备应答经 Router 钩子异步 UPSERT 进聚合表。
 * 立即查 GET /energy/statistics 多半读不到刚下发的本轮数据，故延后一次补查把它兜回来。
 */
const REFRESH_DELAY_MS = 1500;

/** 命令日志最大保留条数。 */
const MAX_LOG_ENTRIES = 50;

/** 拒绝原因 → 中文提示。键与后端 AnShengCommandRejectReason 枚举成员名一致。 */
const REJECT_REASON_TEXT: Record<AnShengCommandRejectReason, string> = {
  RejectedByKind: '设备品类不支持（仅开关类 Switch4G 放行，喇叭类 Speaker4G 被结构性拒绝）',
  RejectedByValidation: '参数校验不通过（含插槽编号越界、功率/电阻非法）',
  RejectedByFirmware: '设备固件版本过低，请先升级固件',
  RejectedByOffline: '设备离线或适配器未连接',
  RejectedByUnknownMethod: '协议目录中不存在该方法，或该方法仅为设备上报事件',
  RejectedByConfirm: '高危命令缺少二次确认（clear 需 confirm=true）',
};

/** 喇叭类设备被拒时的额外提示。 */
const SPEAKER_TIP = '电量计命令仅对开关类（Switch4G）设备放行；喇叭类（Speaker4G）会被结构性拒绝（RejectedByKind）。';

/** 统计粒度 → 中文短名。枚举以字符串出网，按字符串分支（铁律④）。 */
const GRANULARITY_LABELS: Record<AnShengEmGranularity, string> = {
  Total: '累计',
  HourSum: '日内分布',
  Hour: '半小时',
  Day: '日',
  Month: '月',
};

/** 统计查询的粒度过滤候选项。 */
const GRANULARITY_FILTER_OPTIONS: (AnShengEmGranularity | '')[] = [
  '', 'Total', 'HourSum', 'Hour', 'Day', 'Month',
];

/** refresh 下发的 q 查询串候选项（对应后端 AnShengGetEMStatisticsRequest.q）。 */
const REFRESH_Q_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: '默认集合' },
  { value: 'total', label: '累计' },
  { value: 'day', label: '日' },
  { value: 'hour', label: '半小时' },
  { value: 'hourSum', label: '日内分布' },
  { value: 'month', label: '月' },
  { value: 'total,day,hour', label: '累计 + 日 + 半小时' },
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

/** 校准参数字典的一行可编辑项（key/value 均以字符串暂存，提交时解析为 number）。 */
interface CalParamRow {
  key: string;
  value: string;
}

// ── 纯函数工具 ───────────────────────────────────────────────────

/** 单调递增的本地行标识计数器，保证校准字典的 React key 稳定唯一。 */
let calParamKeySeed = 0;

/**
 * 把拒绝原因翻译成中文。
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
    const tail = rejectReason === 'RejectedByKind' ? `｜${SPEAKER_TIP}` : '';
    return `${rejectReason}：${reasonText}${tail}`;
  }
  return errorMessage || fallbackMessage || '操作失败';
}

/**
 * 从异常里抽取可展示的错误文案。
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
 * 把电量格式化为带单位的文本。
 * @param kwh 电量（kWh）。
 * @returns 文本。
 */
function formatKwh(kwh: number): string {
  if (typeof kwh !== 'number' || Number.isNaN(kwh)) {
    return '—';
  }
  return `${kwh.toFixed(3)} kWh`;
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
 * 与 T9 / T10 保持同一判据。
 * @param device 设备。
 * @returns 是否为安圣设备。
 */
function isAnShengDevice(device: DeviceDto): boolean {
  return Boolean(device.serialNumber && /^\d{10,20}$/.test(device.serialNumber));
}

/**
 * 尽力从 getCalParams 的 payload 回显里解析出当前校准参数。
 *
 * 后端 AnShengEnergyResultDto 的 payload 是出网报文回显；若其 JSON 含 rl / calParams
 * 字段（或整体即 calParams 字典），则据此回填编辑表单，让「查看」有意义。解析失败或形状不符时
 * 一律返回 null，不污染本地表单。
 * @param payload 回显字符串，可能为 null。
 * @returns { rl, rows } 或 null。
 */
function parseCalParamsFromPayload(payload?: string | null): { rl: string; rows: CalParamRow[] } | null {
  if (!payload) {
    return null;
  }
  try {
    const parsed = JSON.parse(payload) as Record<string, unknown>;
    if (!parsed || typeof parsed !== 'object') {
      return null;
    }
    let rl: number | null | undefined;
    let calParams: Record<string, number> = {};
    if (typeof parsed.calParams === 'object' && parsed.calParams !== null) {
      rl = typeof parsed.rl === 'number' ? (parsed.rl as number) : null;
      calParams = parsed.calParams as Record<string, number>;
    } else {
      // 整体即 calParams 字典（无 rl）。
      calParams = parsed as Record<string, number>;
    }
    const rows: CalParamRow[] = Object.entries(calParams)
      .filter(([k]) => k !== undefined)
      .map(([k, v]) => ({ key: String(k), value: String(v) }));
    return {
      rl: rl === null || rl === undefined ? '' : String(rl),
      rows,
    };
  } catch {
    return null;
  }
}

// ── 主组件 ───────────────────────────────────────────────────────

/**
 * 安圣电量计面板。
 *
 * 页面可见需 VIEW_DEVICES；所有写类动作需 SEND_DEVICE_COMMANDS，
 * 缺权限时按钮 disabled 而非隐藏（与 T9 / T10 同口径）。
 */
export function EnergyStatisticsPage() {
  const { hasPermission } = useAuth();
  const canView = hasPermission(PERMISSIONS.VIEW_DEVICES);
  const canSend = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS);

  // ── 设备列表 ───────────────────────────────────────────────
  const [devices, setDevices] = useState<DeviceDto[]>([]);
  const [devicesLoading, setDevicesLoading] = useState<boolean>(false);
  const [devicesError, setDevicesError] = useState<string | null>(null);
  const [deviceSearch, setDeviceSearch] = useState<string>('');
  const [selectedDeviceId, setSelectedDeviceId] = useState<number | null>(null);

  // ── 能力档案（只用来确定插槽数量）───────────────────────────
  const [profile, setProfile] = useState<AnShengDeviceProfileDto | null>(null);
  const [profileLoading, setProfileLoading] = useState<boolean>(false);
  const [manualSlotCount, setManualSlotCount] = useState<number>(DEFAULT_SLOT_COUNT);

  // ── 插槽作用域（清零 / 统计查询用）──────────────────────────
  const [activeSlotScope, setActiveSlotScope] = useState<number | 'all'>('all');

  // ── 统计 ───────────────────────────────────────────────────
  const [statistics, setStatistics] = useState<AnShengEmStatisticDto[]>([]);
  const [statsLoading, setStatsLoading] = useState<boolean>(false);
  const [statsRefreshing, setStatsRefreshing] = useState<boolean>(false);
  const [qSelect, setQSelect] = useState<string>('');
  const [granularityFilter, setGranularityFilter] = useState<AnShengEmGranularity | ''>('');
  const [realtimeNote, setRealtimeNote] = useState<string | null>(null);

  // ── 校准参数 ───────────────────────────────────────────────
  const [rlInput, setRlInput] = useState<string>('');
  const [calParamRows, setCalParamRows] = useState<CalParamRow[]>([]);
  const [powerInput, setPowerInput] = useState<string>('');
  const [calBusy, setCalBusy] = useState<boolean>(false);

  // ── 结果反馈 ───────────────────────────────────────────────
  const [notice, setNotice] = useState<Notice | null>(null);
  const [commandLog, setCommandLog] = useState<LogEntry[]>([]);
  const [clearDialogOpen, setClearDialogOpen] = useState<boolean>(false);

  /** 挂起的补刷定时器；设备切换 / 卸载时统一清理，避免 setState on unmounted。 */
  const refreshTimersRef = useRef<number[]>([]);

  /** 始终指向最新的 queryStats，避免延后补查闭包捕获过期过滤器（见 scheduleStatsRefresh）。 */
  const queryStatsRef = useRef<(deviceId: number) => void>(() => {});

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

  /** 插槽路数是否由权威源给出（与 T9 / T10 同口径：只认 profile.slotAmount）。 */
  const slotCountKnown = useMemo<boolean>(
    () => (profile?.slotAmount ?? 0) > 0,
    [profile]
  );

  /** 最终渲染的插槽路数（档案优先，缺省兜底）。 */
  const slotCount = useMemo<number>(() => {
    const fromProfile = profile?.slotAmount ?? 0;
    return fromProfile > 0 ? fromProfile : manualSlotCount;
  }, [profile, manualSlotCount]);

  /** 插槽编号列表（1..slotCount）。 */
  const slotNumbers = useMemo<number[]>(
    () => Array.from({ length: slotCount }, (_, i) => i + 1),
    [slotCount]
  );

  /** 统计表格行（按 插槽 / 粒度 / 周期键 稳定排序）。 */
  const sortedStats = useMemo<AnShengEmStatisticDto[]>(() => {
    return [...statistics].sort((a, b) => {
      if (a.slotNum !== b.slotNum) return a.slotNum - b.slotNum;
      if (a.granularity !== b.granularity) return a.granularity.localeCompare(b.granularity);
      return a.periodKey.localeCompare(b.periodKey);
    });
  }, [statistics]);

  /** 统计中是否存在陈旧行（>24h 未与设备同步）。 */
  const hasStaleStat = useMemo<boolean>(
    () => statistics.some(row => row.isStale),
    [statistics]
  );

  // ── 下发 ───────────────────────────────────────────────────

  /**
   * 消化一次电量计下发结果：写日志、给提示。
   * @param method 协议方法名。
   * @param code 信封状态码。
   * @param envelopeMessage 信封 message。
   * @param result 下发结果 DTO。
   * @param successTitle 成功提示标题。
   * @returns 是否成功受理。
   */
  const consumeEnergyResult = useCallback((
    method: string,
    code: number,
    envelopeMessage: string,
    result: AnShengEnergyResultDto | null | undefined,
    successTitle: string
  ): boolean => {
    const accepted = code === OK_CODE && Boolean(result?.accepted);
    if (accepted) {
      setNotice({ kind: 'success', title: successTitle, detail: result?.commandId ? `命令 ID：${result.commandId}` : envelopeMessage });
      appendLog(method, true, successTitle);
      return true;
    }
    const detail = buildFailureDetail(result?.rejectReason, result?.errorMessage, envelopeMessage);
    setNotice({ kind: 'error', title: '操作被拒绝', detail });
    appendLog(method, false, detail);
    return false;
  }, [appendLog]);

  /**
   * 统一处理一次下发异常（T11 无 409，此处仅兜底网络 / 传输错误）。
   * @param err 异常对象。
   * @param method 协议方法名。
   * @param fallback 兜底文案。
   */
  const handleEnergySendError = useCallback((err: unknown, method: string, fallback: string): void => {
    const message = extractErrorMessage(err, fallback);
    setNotice({ kind: 'error', title: '请求失败', detail: message });
    appendLog(method, false, message);
  }, [appendLog]);

  /** 延后一次静默统计补查（设备权威 + 异步刷新，见 REFRESH_DELAY_MS 注释）。 */
  const scheduleStatsRefresh = useCallback((deviceId: number): void => {
    const timerId = window.setTimeout(() => {
      void queryStatsRef.current(deviceId);
    }, REFRESH_DELAY_MS);
    refreshTimersRef.current.push(timerId);
  }, []);

  // ── ① 实时读数 ────────────────────────────────────────────
  const handleRequestRealtime = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setRealtimeNote(null);
    try {
      const response = await anshengApi.requestEnergyRealtime(selectedDeviceId);
      const ok = consumeEnergyResult(
        'requestEnergyRealtime',
        response.data.code,
        response.data.message,
        response.data.data,
        '已下发 getEMRealtime，设备将回读实时读数'
      );
      setRealtimeNote(ok
        ? '实时读数请求已受理；真值将经数据曲线/聚合表异步回写，稍后可在「统计」区查询。'
        : '实时读数请求被拒绝（见上方提示）。');
    } catch (err: unknown) {
      handleEnergySendError(err, 'requestEnergyRealtime', '实时读数请求失败');
    }
  }, [selectedDeviceId, canSend, consumeEnergyResult, handleEnergySendError]);

  // ── ② 统计：刷新 + 查询 + 清零 ────────────────────────────

  /** 拉取平台聚合表（GET /energy/statistics）。 */
  const queryStats = useCallback(async (deviceId: number): Promise<void> => {
    setStatsLoading(true);
    try {
      const params: AnShengEnergyStatisticsQueryParams = {};
      if (activeSlotScope !== 'all') {
        params.slotNum = activeSlotScope;
      }
      if (granularityFilter) {
        params.granularity = granularityFilter;
      }
      const response = await anshengApi.getEnergyStatistics(deviceId, params);
      if (response.data.code === OK_CODE && Array.isArray(response.data.data)) {
        setStatistics(response.data.data);
        if (response.data.data.length === 0) {
          setNotice({ kind: 'warning', title: '暂无统计数据', detail: '该筛选条件下聚合表为空，请先点「刷新统计」下发采集命令。' });
        }
      } else {
        setStatistics([]);
        setNotice({ kind: 'error', title: '查询统计失败', detail: response.data.message });
        appendLog('getEnergyStatistics', false, response.data.message || '查询统计失败');
      }
    } catch (err: unknown) {
      setStatistics([]);
      appendLog('getEnergyStatistics', false, extractErrorMessage(err, '查询统计失败'));
    } finally {
      setStatsLoading(false);
    }
  }, [activeSlotScope, granularityFilter, appendLog]);

  /** 保持 queryStatsRef 指向最新实现，供延后补查使用。 */
  useEffect(() => {
    queryStatsRef.current = queryStats;
  }, [queryStats]);

  /** 下发统计采集（refresh），成功后延时补查聚合表。 */
  const handleRefreshStats = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setStatsRefreshing(true);
    setNotice(null);
    try {
      const request: AnShengGetEMStatisticsRequest = {};
      if (qSelect) {
        request.q = qSelect;
      }
      const response = await anshengApi.refreshEnergyStatistics(selectedDeviceId, request);
      const ok = consumeEnergyResult(
        'refreshEnergyStatistics',
        response.data.code,
        response.data.message,
        response.data.data,
        '已下发 getEMStatistics，正在采集统计'
      );
      if (ok) {
        scheduleStatsRefresh(selectedDeviceId);
      }
    } catch (err: unknown) {
      handleEnergySendError(err, 'refreshEnergyStatistics', '刷新统计失败');
    } finally {
      setStatsRefreshing(false);
    }
  }, [selectedDeviceId, canSend, qSelect, consumeEnergyResult, scheduleStatsRefresh, handleEnergySendError]);

  /** 手动查询统计（不触发下发，仅读聚合表）。 */
  const handleManualQuery = useCallback((): void => {
    if (selectedDeviceId === null) {
      return;
    }
    void queryStats(selectedDeviceId);
  }, [selectedDeviceId, queryStats]);

  /** 下发清零（clear），需经二次确认弹窗。 */
  const handleClearStats = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setClearDialogOpen(false);
    setNotice(null);
    try {
      const request: AnShengClearEMStatisticsRequest = {
        confirm: true,
        slotNum: activeSlotScope === 'all' ? null : activeSlotScope,
      };
      const response = await anshengApi.clearEnergyStatistics(selectedDeviceId, request);
      consumeEnergyResult(
        'clearEnergyStatistics',
        response.data.code,
        response.data.message,
        response.data.data,
        activeSlotScope === 'all' ? '已下发 clearEMStatistics（清空全部插槽）' : `已下发 clearEMStatistics（清空插槽 ${activeSlotScope}）`
      );
    } catch (err: unknown) {
      handleEnergySendError(err, 'clearEnergyStatistics', '清零统计失败');
    }
  }, [selectedDeviceId, canSend, activeSlotScope, consumeEnergyResult, handleEnergySendError]);

  // ── ③ 校准参数：查看 / 设置 / 重置 / 自动 ──────────────────

  /** 读校准参数（getCalParams），尽力从 payload 回填表单。 */
  const handleReadCalParams = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setCalBusy(true);
    setNotice(null);
    try {
      const response = await anshengApi.getCalParams(selectedDeviceId);
      const ok = consumeEnergyResult(
        'getCalParams',
        response.data.code,
        response.data.message,
        response.data.data,
        '已下发 getCalParams，读取校准参数'
      );
      if (ok) {
        const parsed = parseCalParamsFromPayload(response.data.data?.payload);
        if (parsed) {
          setRlInput(parsed.rl);
          setCalParamRows(parsed.rows.length > 0 ? parsed.rows : [{ key: '', value: '' }]);
        }
      }
    } catch (err: unknown) {
      handleEnergySendError(err, 'getCalParams', '读取校准参数失败');
    } finally {
      setCalBusy(false);
    }
  }, [selectedDeviceId, canSend, consumeEnergyResult, handleEnergySendError]);

  /** 设置校准参数（setCalParams）：校验 RL + 字典后下发。 */
  const handleSetCalParams = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setCalBusy(true);
    setNotice(null);
    try {
      const rlNum = rlInput.trim() === '' ? null : Number(rlInput);
      if (rlNum !== null && !Number.isFinite(rlNum)) {
        setNotice({ kind: 'error', title: '参数非法', detail: 'RL 必须是数字。' });
        appendLog('setCalParams', false, 'RL 非法');
        return;
      }
      const calParams: Record<string, number> = {};
      for (const row of calParamRows) {
        const k = row.key.trim();
        if (k === '') {
          continue;
        }
        const v = Number(row.value);
        if (!Number.isFinite(v)) {
          setNotice({ kind: 'error', title: '参数非法', detail: `校准参数「${k}」的值必须是数字。` });
          appendLog('setCalParams', false, `校准参数 ${k} 非法`);
          return;
        }
        calParams[k] = v;
      }
      if (rlNum === null && Object.keys(calParams).length === 0) {
        setNotice({ kind: 'error', title: '参数缺失', detail: '至少需要提供 RL 或一项校准参数（后端将拒收空字典）。' });
        appendLog('setCalParams', false, '空字典被拒');
        return;
      }
      const request: AnShengSetCalParamsRequest = { rl: rlNum, calParams };
      const response = await anshengApi.setCalParams(selectedDeviceId, request);
      consumeEnergyResult(
        'setCalParams',
        response.data.code,
        response.data.message,
        response.data.data,
        '已下发 setCalParams，写入校准参数'
      );
    } catch (err: unknown) {
      handleEnergySendError(err, 'setCalParams', '设置校准参数失败');
    } finally {
      setCalBusy(false);
    }
  }, [selectedDeviceId, canSend, rlInput, calParamRows, consumeEnergyResult, handleEnergySendError]);

  /** 重置校准参数（reset）。 */
  const handleResetCalParams = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setCalBusy(true);
    setNotice(null);
    try {
      const response = await anshengApi.resetCalParams(selectedDeviceId);
      consumeEnergyResult(
        'resetCalParams',
        response.data.code,
        response.data.message,
        response.data.data,
        '已下发 resetCalParams，重置校准参数'
      );
    } catch (err: unknown) {
      handleEnergySendError(err, 'resetCalParams', '重置校准参数失败');
    } finally {
      setCalBusy(false);
    }
  }, [selectedDeviceId, canSend, consumeEnergyResult, handleEnergySendError]);

  /** 按已知负载功率自动校准（auto）。 */
  const handleAutoCal = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    const power = Number(powerInput);
    if (!Number.isFinite(power) || power <= 0) {
      setNotice({ kind: 'error', title: '参数非法', detail: '自动校准功率必须是大于 0 的数字（W）。' });
      appendLog('autoCalParams', false, '功率非法');
      return;
    }
    setCalBusy(true);
    setNotice(null);
    try {
      const request: AnShengAutoCalRequest = { power };
      const response = await anshengApi.autoCalParams(selectedDeviceId, request);
      consumeEnergyResult(
        'autoCalParams',
        response.data.code,
        response.data.message,
        response.data.data,
        `已下发 autoCal（功率 ${power} W）`
      );
    } catch (err: unknown) {
      handleEnergySendError(err, 'autoCalParams', '自动校准失败');
    } finally {
      setCalBusy(false);
    }
  }, [selectedDeviceId, canSend, powerInput, consumeEnergyResult, handleEnergySendError]);

  /** 校准字典新增一行。 */
  const addCalParamRow = useCallback((): void => {
    calParamKeySeed += 1;
    setCalParamRows(prev => [...prev, { key: '', value: '' }]);
  }, []);

  /** 校准字典删除一行。 */
  const removeCalParamRow = useCallback((index: number): void => {
    setCalParamRows(prev => prev.filter((_, i) => i !== index));
  }, []);

  /** 校准字典更新一行。 */
  const updateCalParamRow = useCallback((index: number, patch: Partial<CalParamRow>): void => {
    setCalParamRows(prev => prev.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }, []);

  // ── 手动同步 ───────────────────────────────────────────────
  const handleManualSync = useCallback((): void => {
    if (selectedDeviceId === null) {
      return;
    }
    void loadProfile(selectedDeviceId);
    void queryStats(selectedDeviceId);
  }, [selectedDeviceId, loadProfile, queryStats]);

  // ── 初始加载 ───────────────────────────────────────────────
  useEffect(() => {
    void loadDevices();
  }, [loadDevices]);

  // ── 切换设备：重置面板并拉取该设备数据 ─────────────────────
  useEffect(() => {
    clearRefreshTimers();
    setNotice(null);
    setProfile(null);
    setStatistics([]);
    setRealtimeNote(null);
    setActiveSlotScope('all');
    setClearDialogOpen(false);
    if (selectedDeviceId === null) {
      return;
    }
    void loadProfile(selectedDeviceId);
    void queryStatsRef.current(selectedDeviceId);
  }, [selectedDeviceId, loadProfile, clearRefreshTimers]);

  // ── 卸载清理 ───────────────────────────────────────────────
  useEffect(() => clearRefreshTimers, [clearRefreshTimers]);

  // ── 无查看权限：直接门控整页 ───────────────────────────────
  if (!canView) {
    return (
      <div className="p-6">
        <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
          <ShieldAlert className="w-12 h-12 text-amber-500/70 mx-auto mb-3" />
          <p className="text-slate-300 text-sm font-medium">无权访问「安圣电量统计」</p>
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
          <div className="p-2 rounded-lg bg-amber-500/20 border border-amber-500/30">
            <Zap className="w-6 h-6 text-amber-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-white">安圣电量统计</h1>
            <p className="text-sm text-slate-400">实时读数 · 统计采集 · 校准参数</p>
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
            disabled={selectedDeviceId === null || profileLoading || statsLoading}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-amber-600/20 border border-amber-500/30
                       text-amber-400 hover:bg-amber-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
          >
            <RefreshCw className={`w-4 h-4 ${(profileLoading || statsLoading) ? 'animate-spin' : ''}`} />
            手动同步
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        {/* ────────────── 左栏：设备选择 ────────────── */}
        <div className="space-y-4">
          <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-3">
            <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
              <Cpu className="w-4 h-4 text-amber-400" />
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
                           placeholder:text-slate-500 focus:outline-none focus:border-amber-500/50 transition-all"
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
                          ? 'bg-amber-600/20 border border-amber-500/30 text-amber-300'
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
                </dl>
              )}
              <p className="text-[11px] text-slate-600 leading-relaxed pt-1">
                电量计命令仅开关类（Switch4G）设备放行；喇叭类下发会被结构性拒绝并返回 RejectedByKind。
              </p>
            </div>
          )}

          {/* 插槽作用域（清零 / 统计查询用） */}
          {selectedDeviceId !== null && (
            <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-2">
              <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                <Hash className="w-4 h-4 text-amber-400" />
                插槽作用域
              </h3>
              {!slotCountKnown && (
                <label className="flex items-center gap-1.5 text-xs text-slate-500 mb-1">
                  路数
                  <select
                    value={manualSlotCount}
                    onChange={e => setManualSlotCount(Number(e.target.value))}
                    className="px-2 py-1 rounded bg-slate-900/60 border border-slate-700 text-slate-300
                               focus:outline-none focus:border-amber-500/50"
                  >
                    {SLOT_COUNT_OPTIONS.map(n => (
                      <option key={n} value={n}>{n}</option>
                    ))}
                  </select>
                </label>
              )}
              <select
                value={activeSlotScope}
                onChange={e => setActiveSlotScope(e.target.value === 'all' ? 'all' : Number(e.target.value))}
                className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                           focus:outline-none focus:border-amber-500/50"
              >
                <option value="all">全部插槽</option>
                {slotNumbers.map(n => (
                  <option key={n} value={n}>插槽 {n}</option>
                ))}
              </select>
              <p className="text-[11px] text-slate-600 leading-relaxed">
                用于「清零统计」与「统计查询」的插槽过滤；选「全部插槽」时 clear 不清平台聚合表、只清设备侧。
              </p>
            </div>
          )}
        </div>

        {/* ────────────── 右栏：三块功能 ────────────── */}
        <div className="xl:col-span-2 space-y-4">
          {selectedDeviceId === null ? (
            <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
              <Zap className="w-12 h-12 text-slate-600 mx-auto mb-3" />
              <p className="text-slate-500 text-sm">请先选择一个安圣设备</p>
              <p className="text-slate-600 text-xs mt-1">选择设备后即可查看电量实时读数、统计与校准参数</p>
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

              {/* ── ① 实时读数 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                  <Gauge className="w-4 h-4 text-amber-400" />
                  实时读数
                  <span className="text-xs text-slate-500 font-normal">（下发 getEMRealtime）</span>
                </h3>
                <div className="flex items-center gap-3 flex-wrap">
                  <button
                    onClick={() => { void handleRequestRealtime(); }}
                    disabled={!canSend}
                    className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-medium
                               bg-amber-600/25 text-amber-300 border border-amber-500/30
                               hover:bg-amber-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    <Power className="w-3.5 h-3.5" />
                    拉取实时读数
                  </button>
                  {realtimeNote && (
                    <span className="text-xs text-slate-400">{realtimeNote}</span>
                  )}
                </div>
              </div>

              {/* ── ② 统计：刷新 / 查询 / 清零 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                  <Table2 className="w-4 h-4 text-amber-400" />
                  电量统计
                  <span className="text-xs text-slate-500 font-normal">（refresh → 延时查询 / 手动查询 / 清零）</span>
                </h3>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5">
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">采集范围（q）</label>
                    <select
                      value={qSelect}
                      onChange={e => setQSelect(e.target.value)}
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50"
                    >
                      {REFRESH_Q_OPTIONS.map(opt => (
                        <option key={opt.value} value={opt.value}>{opt.label}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">粒度过滤</label>
                    <select
                      value={granularityFilter}
                      onChange={e => setGranularityFilter(e.target.value as AnShengEmGranularity | '')}
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50"
                    >
                      {GRANULARITY_FILTER_OPTIONS.map(opt => (
                        <option key={opt || 'all'} value={opt}>
                          {opt === '' ? '全部粒度' : GRANULARITY_LABELS[opt as AnShengEmGranularity]}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="flex items-end gap-2">
                    <button
                      onClick={() => { void handleRefreshStats(); }}
                      disabled={!canSend || statsRefreshing}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium
                                 bg-amber-600/25 text-amber-300 border border-amber-500/30
                                 hover:bg-amber-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                    >
                      {statsRefreshing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
                      刷新统计
                    </button>
                    <button
                      onClick={handleManualQuery}
                      disabled={statsLoading}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs
                                 text-slate-300 border border-slate-700/50
                                 hover:bg-slate-700/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                    >
                      {statsLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}
                      查询
                    </button>
                  </div>
                </div>

                <div className="flex items-center justify-end">
                  <button
                    onClick={() => setClearDialogOpen(true)}
                    disabled={!canSend}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium
                               text-red-400 border border-red-500/30 bg-red-600/10
                               hover:bg-red-600/20 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                    清零统计
                  </button>
                </div>

                {hasStaleStat && (
                  <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-400 text-xs">
                    <Clock className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                    <span>部分统计行超过 24 小时未与设备同步（isStale），建议点「刷新统计」重新采集。</span>
                  </div>
                )}

                {/* 统计表格 */}
                <div className="rounded-lg border border-slate-700/50 overflow-hidden">
                  <div className="max-h-[360px] overflow-y-auto">
                    <table className="w-full text-xs">
                      <thead className="sticky top-0 bg-slate-900/80 text-slate-400">
                        <tr>
                          <th className="text-left px-3 py-2 font-medium">插槽</th>
                          <th className="text-left px-3 py-2 font-medium">粒度</th>
                          <th className="text-left px-3 py-2 font-medium">周期键</th>
                          <th className="text-right px-3 py-2 font-medium">电量</th>
                          <th className="text-left px-3 py-2 font-medium">同步时间</th>
                        </tr>
                      </thead>
                      <tbody>
                        {sortedStats.length === 0 ? (
                          <tr>
                            <td colSpan={5} className="text-center py-8 text-slate-600">
                              <CircleSlash2 className="w-7 h-7 mx-auto mb-2 text-slate-600" />
                              暂无统计数据，点「刷新统计」下发采集命令
                            </td>
                          </tr>
                        ) : (
                          sortedStats.map((row, idx) => (
                            <tr key={`${row.slotNum}-${row.granularity}-${row.periodKey}-${idx}`}
                                className="border-t border-slate-700/40 hover:bg-slate-800/40">
                              <td className="px-3 py-2 text-slate-300">{row.slotNum}</td>
                              <td className="px-3 py-2 text-slate-300">
                                {GRANULARITY_LABELS[row.granularity] ?? row.granularity}
                              </td>
                              <td className="px-3 py-2 font-mono text-slate-400">{row.periodKey}</td>
                              <td className="px-3 py-2 text-right text-slate-200">{formatKwh(row.kwh)}</td>
                              <td className="px-3 py-2 text-slate-500">
                                <span className="inline-flex items-center gap-1">
                                  <Clock className="w-3 h-3" />
                                  {formatDateTime(row.syncedAt)}
                                  {row.isStale && (
                                    <span className="ml-1 px-1 rounded bg-amber-500/20 text-amber-400 text-[10px]">陈旧</span>
                                  )}
                                </span>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>

              {/* ── ③ 校准参数 ── */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                  <SlidersHorizontal className="w-4 h-4 text-amber-400" />
                  校准参数
                  <span className="text-xs text-slate-500 font-normal">（RL + 字典 · 仅开关类放行）</span>
                </h3>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">校准电阻 RL（Ω）</label>
                    <input
                      type="number"
                      min={0}
                      step="any"
                      value={rlInput}
                      disabled={!canSend || calBusy}
                      onChange={e => setRlInput(e.target.value)}
                      placeholder="如 0.001"
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                    />
                  </div>
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">自动校准功率（W）</label>
                    <input
                      type="number"
                      min={0}
                      step="any"
                      value={powerInput}
                      disabled={!canSend || calBusy}
                      onChange={e => setPowerInput(e.target.value)}
                      placeholder="接在插槽上的真实负载功率"
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                    />
                  </div>
                </div>

                {/* 校准参数字典编辑 */}
                <div className="space-y-1.5">
                  <div className="flex items-center justify-between">
                    <span className="text-[11px] text-slate-500">校准参数字典（key → 数值）</span>
                    <button
                      onClick={addCalParamRow}
                      disabled={!canSend || calBusy}
                      className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-[11px
                                 text-amber-400 border border-amber-500/30 bg-amber-600/15
                                 hover:bg-amber-600/25 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                    >
                      <Plus className="w-3 h-3" />
                      加一项
                    </button>
                  </div>
                  {calParamRows.length === 0 ? (
                    <p className="text-[11px] text-slate-600 text-center py-2">暂无字典项，点「加一项」添加</p>
                  ) : (
                    calParamRows.map((row, idx) => (
                      <div key={idx} className="flex items-center gap-2">
                        <input
                          type="text"
                          value={row.key}
                          disabled={!canSend || calBusy}
                          onChange={e => updateCalParamRow(idx, { key: e.target.value })}
                          placeholder="键名"
                          className="flex-1 px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                     focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                        />
                        <input
                          type="number"
                          step="any"
                          value={row.value}
                          disabled={!canSend || calBusy}
                          onChange={e => updateCalParamRow(idx, { value: e.target.value })}
                          placeholder="数值"
                          className="flex-1 px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                     focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                        />
                        <button
                          onClick={() => removeCalParamRow(idx)}
                          disabled={!canSend || calBusy}
                          className="inline-flex items-center justify-center px-2 py-1.5 rounded-md text-[11px]
                                     text-red-400 border border-red-500/30 bg-red-600/10
                                     hover:bg-red-600/20 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                        >
                          <Trash2 className="w-3 h-3" />
                          删除
                        </button>
                      </div>
                    ))
                  )}
                </div>

                {/* 校准动作按钮 */}
                <div className="flex items-center gap-2 flex-wrap pt-1">
                  <button
                    onClick={() => { void handleReadCalParams(); }}
                    disabled={!canSend || calBusy}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs
                               text-cyan-400 border border-cyan-500/30 bg-cyan-600/15
                               hover:bg-cyan-600/25 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {calBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Wrench className="w-3.5 h-3.5" />}
                    读取参数
                  </button>
                  <button
                    onClick={() => { void handleSetCalParams(); }}
                    disabled={!canSend || calBusy}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium
                               bg-amber-600/25 text-amber-300 border border-amber-500/30
                               hover:bg-amber-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {calBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
                    设置参数
                  </button>
                  <button
                    onClick={() => { void handleResetCalParams(); }}
                    disabled={!canSend || calBusy}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs
                               text-slate-300 border border-slate-700/50
                               hover:bg-slate-700/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {calBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <RotateCcw className="w-3.5 h-3.5" />}
                    重置
                  </button>
                  <button
                    onClick={() => { void handleAutoCal(); }}
                    disabled={!canSend || calBusy || powerInput.trim() === ''}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium
                               bg-emerald-600/20 text-emerald-300 border border-emerald-500/30
                               hover:bg-emerald-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {calBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Zap className="w-3.5 h-3.5" />}
                    自动校准
                  </button>
                </div>
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
                  <div className="space-y-1 max-h-[200px] overflow-y-auto">
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

              {/* ── 清零二次确认弹窗 ── */}
              {clearDialogOpen && (
                <div
                  className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
                  onClick={() => setClearDialogOpen(false)}
                >
                  <div
                    className="w-full max-w-md rounded-xl bg-slate-800 border border-slate-700 p-5 space-y-4"
                    onClick={e => e.stopPropagation()}
                  >
                    <div className="flex items-center gap-2 text-amber-400">
                      <ShieldAlert className="w-5 h-5" />
                      <h4 className="text-base font-semibold">确认清零统计</h4>
                    </div>
                    <p className="text-sm text-slate-300 leading-relaxed">
                      此操作将清空
                      <span className="text-amber-400 font-medium">
                        {activeSlotScope === 'all' ? '全部插槽' : `插槽 ${activeSlotScope}`}
                      </span>
                      的<span className="font-medium">设备侧</span>统计。
                      平台聚合表一行不删，但设备侧计数归零且不可撤销。
                    </p>
                    <div className="flex items-center justify-end gap-2 pt-1">
                      <button
                        onClick={() => setClearDialogOpen(false)}
                        className="px-4 py-2 rounded-lg text-sm text-slate-300 border border-slate-600
                                   hover:bg-slate-700/50 transition-all"
                      >
                        取消
                      </button>
                      <button
                        onClick={() => { void handleClearStats(); }}
                        disabled={calBusy}
                        className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium
                                   bg-red-600/30 text-red-300 border border-red-500/40
                                   hover:bg-red-600/40 disabled:opacity-50 disabled:cursor-not-allowed transition-all"
                      >
                        {calBusy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
                        确认清零
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
