// T12：安圣水流量面板。
//
// 【消费的后端端点（T12，镜像 T11「电量统计」模式）】
//   POST /ansheng/{deviceId}/water/flow/info           下发 getFlowInfo（拉取实时流量）
//   GET  /ansheng/{deviceId}/water/flow/info           读平台实时流量镜像（iFlow / tFlow / tPulses）
//   POST /ansheng/{deviceId}/water/flow/reset          下发 resetTotalFlow（清零累计流量）
//   GET  /ansheng/{deviceId}/water/flow/config         读平台流量配置镜像（k / offset / minFlow / maxFlow）
//   POST /ansheng/{deviceId}/water/flow/config         设流量配置（仅传非空字段）
//   POST /ansheng/{deviceId}/water/flow/config/reset   下发 resetFlowConfig（重置出厂配置）
//
// 【响应信封（铁律②）】ApiResponse<T> = { code, message, data, timestamp }。
//   水流量**没有乐观并发令牌**，六个端点结果**全部 HTTP 200**：
//   成功 code=200，被拒 code=400 + data.rejectReason。
//   **绝不**读 data.success。T12 不存在 409，无需 catch 并发冲突分支。
//
// 【权限门控】整页进入需 VIEW_DEVICES（与 T11 同口径）；
//   POST 三个写端点（flow/info 下发、flow/reset、flow/config、flow/config/reset）需 SEND_DEVICE_COMMANDS，
//   写按钮 disabled 而非隐藏。「读取配置」是 GET 读端点，随页面只读可见，不受 send 门控。

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Droplet, Gauge, RefreshCw, Loader2,
  AlertCircle, CheckCircle, Cpu, Radio, Clock,
  ShieldAlert, Search, Activity, RotateCcw, Save, SlidersHorizontal, Waves,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';
import { anshengApi } from '@/app/services/api/anshengApi';
import { deviceApi } from '@/app/services/api/deviceApi';
import type {
  AnShengCommandRejectReason,
  AnShengEnergyResultDto,
  AnShengWaterFlowInfoDto,
  AnShengWaterFlowConfigDto,
  AnShengWaterFlowConfigRequest,
} from '@/app/services/api/types/ansheng.types';
import type { DeviceDto } from '@/app/services/api/types/device.types';

// ── 常量 ─────────────────────────────────────────────────────────

/** ApiResponse 的成功码。被拒时 HTTP 仍是 200，靠 code=400 表达。 */
const OK_CODE = 200;

/**
 * 写后刷新延迟（毫秒）。
 *
 * 【为什么要等】下发 getFlowInfo / setWaterFlowConfig / reset 只是下发命令，真值由设备应答
 * 经 Router 钩子异步 UPSERT 进镜像表。立即查 GET 多半读不到刚下发的本轮数据，故延后一次补查。
 */
const REFRESH_DELAY_MS = 1500;

/** 命令日志最大保留条数。 */
const MAX_LOG_ENTRIES = 50;

/** 拒绝原因 → 中文提示。键与后端 AnShengCommandRejectReason 枚举成员名一致。 */
const REJECT_REASON_TEXT: Record<AnShengCommandRejectReason, string> = {
  RejectedByKind: '设备品类不支持（仅开关类 Switch4G 放行，喇叭类 Speaker4G 被结构性拒绝）',
  RejectedByValidation: '参数校验不通过（含流量系数 / 阈值非法）',
  RejectedByFirmware: '设备固件版本过低，请先升级固件',
  RejectedByOffline: '设备离线或适配器未连接',
  RejectedByUnknownMethod: '协议目录中不存在该方法，或该方法仅为设备上报事件',
  RejectedByConfirm: '高危命令缺少二次确认',
};

/** 喇叭类设备被拒时的额外提示。 */
const SPEAKER_TIP =
  '水流量命令仅对开关类（Switch4G）设备放行；喇叭类（Speaker4G）会被结构性拒绝（RejectedByKind）。';

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

// ── 纯函数工具 ───────────────────────────────────────────────────

/**
 * 把拒绝原因翻译成中文。
 */
function describeRejectReason(reason?: AnShengCommandRejectReason | null): string | null {
  if (!reason) {
    return null;
  }
  return REJECT_REASON_TEXT[reason] ?? `未知拒绝原因：${String(reason)}`;
}

/**
 * 从一次失败的下发结果里拼出面向人的原因文案。
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
 */
function extractErrorMessage(err: unknown, fallback: string): string {
  const anyErr = err as { response?: { data?: { message?: string } }; message?: string };
  return anyErr?.response?.data?.message || anyErr?.message || fallback;
}

/**
 * 格式化数字为文本；非法 / 空值返回「—」。
 */
function formatFlowNumber(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined || typeof value !== 'number' || Number.isNaN(value)) {
    return '—';
  }
  return value.toFixed(digits);
}

/**
 * 格式化 UNIX 毫秒时间戳为本地短时间；空值返回「—」。
 */
function formatTimestamp(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
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
 */
function formatLogTime(date: Date): string {
  return date.toLocaleTimeString('zh-CN', {
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

/**
 * 判断一个设备是否像安圣设备（SerialNumber 为纯数字 IMEI）。与 T9 / T10 / T11 同一判据。
 */
function isAnShengDevice(device: DeviceDto): boolean {
  return Boolean(device.serialNumber && /^\d{10,20}$/.test(device.serialNumber));
}

// ── 主组件 ───────────────────────────────────────────────────────

/**
 * 安圣水流量面板。
 *
 * 页面可见需 VIEW_DEVICES；写类动作需 SEND_DEVICE_COMMANDS，缺权限时按钮 disabled 而非隐藏。
 */
export function WaterFlowPage() {
  const { hasPermission } = useAuth();
  const canView = hasPermission(PERMISSIONS.VIEW_DEVICES);
  const canSend = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS);

  // ── 设备列表 ───────────────────────────────────────────────
  const [devices, setDevices] = useState<DeviceDto[]>([]);
  const [devicesLoading, setDevicesLoading] = useState<boolean>(false);
  const [devicesError, setDevicesError] = useState<string | null>(null);
  const [deviceSearch, setDeviceSearch] = useState<string>('');
  const [selectedDeviceId, setSelectedDeviceId] = useState<number | null>(null);

  // ── 实时流量镜像 ───────────────────────────────────────────
  const [flowInfo, setFlowInfo] = useState<AnShengWaterFlowInfoDto | null>(null);
  const [flowInfoLoading, setFlowInfoLoading] = useState<boolean>(false);
  const [pullBusy, setPullBusy] = useState<boolean>(false);
  const [pullNote, setPullNote] = useState<string | null>(null);

  // ── 流量配置 ───────────────────────────────────────────────
  const [config, setConfig] = useState<AnShengWaterFlowConfigDto | null>(null);
  const [kInput, setKInput] = useState<string>('');
  const [offsetInput, setOffsetInput] = useState<string>('');
  const [minFlowInput, setMinFlowInput] = useState<string>('');
  const [maxFlowInput, setMaxFlowInput] = useState<string>('');
  const [configBusy, setConfigBusy] = useState<boolean>(false);

  // ── 结果反馈 ───────────────────────────────────────────────
  const [notice, setNotice] = useState<Notice | null>(null);
  const [commandLog, setCommandLog] = useState<LogEntry[]>([]);

  /** 挂起的补刷定时器；设备切换 / 卸载时统一清理，避免 setState on unmounted。 */
  const refreshTimersRef = useRef<number[]>([]);

  /** 始终指向最新的 queryFlowInfo，避免延后补查闭包捕获过期设备 id。 */
  const queryFlowInfoRef = useRef<(deviceId: number) => void>(() => {});

  /** 始终指向最新的 queryConfig，避免延后补查闭包捕获过期设备 id。 */
  const queryConfigRef = useRef<(deviceId: number) => void>(() => {});

  /** 清空所有挂起的补刷定时器。 */
  const clearRefreshTimers = useCallback((): void => {
    refreshTimersRef.current.forEach(id => window.clearTimeout(id));
    refreshTimersRef.current = [];
  }, []);

  /**
   * 追加一条命令日志。
   */
  const appendLog = useCallback((method: string, success: boolean, message: string): void => {
    setCommandLog(prev =>
      [{ time: new Date(), method, success, message }, ...prev].slice(0, MAX_LOG_ENTRIES)
    );
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

  // ── 下发 ───────────────────────────────────────────────────

  /**
   * 消化一次水流量下发结果：写日志、给提示，返回是否受理成功。
   */
  const consumeWaterResult = useCallback((
    method: string,
    code: number,
    envelopeMessage: string,
    result: AnShengEnergyResultDto | null | undefined,
    successTitle: string
  ): boolean => {
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
    setNotice({ kind: 'error', title: '操作被拒绝', detail });
    appendLog(method, false, detail);
    return false;
  }, [appendLog]);

  /**
   * 统一处理一次下发异常（T12 无 409，此处仅兜底网络 / 传输错误）。
   */
  const handleWaterSendError = useCallback((err: unknown, method: string, fallback: string): void => {
    const message = extractErrorMessage(err, fallback);
    setNotice({ kind: 'error', title: '请求失败', detail: message });
    appendLog(method, false, message);
  }, [appendLog]);

  /** 读取实时流量镜像（GET /water/flow/info）。 */
  const queryFlowInfo = useCallback(async (deviceId: number): Promise<void> => {
    setFlowInfoLoading(true);
    try {
      const response = await anshengApi.getWaterFlowInfo(deviceId);
      if (response.data.code === OK_CODE) {
        setFlowInfo(response.data.data ?? null);
        if (!response.data.data) {
          appendLog('getWaterFlowInfo', false, '暂无实时流量镜像数据');
        } else {
          appendLog('getWaterFlowInfo', true, '已读取实时流量镜像');
        }
      } else {
        setFlowInfo(null);
        setNotice({ kind: 'error', title: '读取实时流量失败', detail: response.data.message });
        appendLog('getWaterFlowInfo', false, response.data.message || '读取实时流量失败');
      }
    } catch (err: unknown) {
      setFlowInfo(null);
      appendLog('getWaterFlowInfo', false, extractErrorMessage(err, '读取实时流量失败'));
    } finally {
      setFlowInfoLoading(false);
    }
  }, [appendLog]);

  /** 读取流量配置镜像（GET /water/flow/config），并回填编辑表单。 */
  const queryConfig = useCallback(async (deviceId: number): Promise<void> => {
    try {
      const response = await anshengApi.getWaterFlowConfig(deviceId);
      if (response.data.code === OK_CODE) {
        const data = response.data.data ?? null;
        setConfig(data);
        if (data) {
          setKInput(String(data.k));
          setOffsetInput(String(data.offset));
          setMinFlowInput(String(data.minFlow));
          setMaxFlowInput(String(data.maxFlow));
          appendLog('getWaterFlowConfig', true, '已读取流量配置镜像');
        } else {
          appendLog('getWaterFlowConfig', false, '暂无流量配置镜像数据');
        }
      } else {
        setConfig(null);
        setNotice({ kind: 'error', title: '读取配置失败', detail: response.data.message });
        appendLog('getWaterFlowConfig', false, response.data.message || '读取配置失败');
      }
    } catch (err: unknown) {
      setConfig(null);
      appendLog('getWaterFlowConfig', false, extractErrorMessage(err, '读取配置失败'));
    }
  }, [appendLog]);

  /** 保持 refs 指向最新实现，供延后补查使用。 */
  useEffect(() => { queryFlowInfoRef.current = queryFlowInfo; }, [queryFlowInfo]);
  useEffect(() => { queryConfigRef.current = queryConfig; }, [queryConfig]);

  /** 延后一次静默实时流量补查（异步镜像，见 REFRESH_DELAY_MS 注释）。 */
  const scheduleFlowInfoRefresh = useCallback((deviceId: number): void => {
    const timerId = window.setTimeout(() => {
      void queryFlowInfoRef.current(deviceId);
    }, REFRESH_DELAY_MS);
    refreshTimersRef.current.push(timerId);
  }, []);

  /** 延后一次静默配置补查（异步镜像，见 REFRESH_DELAY_MS 注释）。 */
  const scheduleConfigRefresh = useCallback((deviceId: number): void => {
    const timerId = window.setTimeout(() => {
      void queryConfigRef.current(deviceId);
    }, REFRESH_DELAY_MS);
    refreshTimersRef.current.push(timerId);
  }, []);

  // ── ① 实时流量：下发 + 补查 ────────────────────────────────
  const handlePullFlow = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setPullNote(null);
    setPullBusy(true);
    setNotice(null);
    try {
      const response = await anshengApi.requestWaterFlowInfo(selectedDeviceId);
      const ok = consumeWaterResult(
        'requestWaterFlowInfo',
        response.data.code,
        response.data.message,
        response.data.data,
        '已下发 getFlowInfo，设备将回读实时流量'
      );
      if (ok) {
        setPullNote('实时流量请求已受理；真值将经镜像表异步回写，1.5s 后自动补查显示。');
        scheduleFlowInfoRefresh(selectedDeviceId);
      } else {
        setPullNote('实时流量请求被拒绝（见上方提示）。');
      }
    } catch (err: unknown) {
      handleWaterSendError(err, 'requestWaterFlowInfo', '实时流量请求失败');
    } finally {
      setPullBusy(false);
    }
  }, [selectedDeviceId, canSend, consumeWaterResult, scheduleFlowInfoRefresh, handleWaterSendError]);

  // ── ② 流量配置：读取 / 设置 / 重置 ────────────────────────

  /** 读取配置（GET /water/flow/config），并延后一次补查。 */
  const handleReadConfig = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null) {
      return;
    }
    setConfigBusy(true);
    setNotice(null);
    try {
      await queryConfig(selectedDeviceId);
      scheduleConfigRefresh(selectedDeviceId);
    } catch {
      /* queryConfig 内部已写日志 */
    } finally {
      setConfigBusy(false);
    }
  }, [selectedDeviceId, queryConfig, scheduleConfigRefresh]);

  /** 设置配置（POST /water/flow/config）：仅传非空字段。 */
  const handleSetConfig = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setConfigBusy(true);
    setNotice(null);
    try {
      const request: AnShengWaterFlowConfigRequest = {};
      const filled: string[] = [];

      if (kInput.trim() !== '') {
        const v = Number(kInput);
        if (!Number.isFinite(v)) {
          setNotice({ kind: 'error', title: '参数非法', detail: '流量系数 K 必须是数字。' });
          appendLog('setWaterFlowConfig', false, 'K 非法');
          return;
        }
        request.k = v;
        filled.push('k');
      }
      if (offsetInput.trim() !== '') {
        const v = Number(offsetInput);
        if (!Number.isFinite(v)) {
          setNotice({ kind: 'error', title: '参数非法', detail: '流量偏移 offset 必须是数字。' });
          appendLog('setWaterFlowConfig', false, 'offset 非法');
          return;
        }
        request.offset = v;
        filled.push('offset');
      }
      if (minFlowInput.trim() !== '') {
        const v = Number(minFlowInput);
        if (!Number.isFinite(v)) {
          setNotice({ kind: 'error', title: '参数非法', detail: '最小流量 minFlow 必须是数字。' });
          appendLog('setWaterFlowConfig', false, 'minFlow 非法');
          return;
        }
        request.minFlow = v;
        filled.push('minFlow');
      }
      if (maxFlowInput.trim() !== '') {
        const v = Number(maxFlowInput);
        if (!Number.isFinite(v)) {
          setNotice({ kind: 'error', title: '参数非法', detail: '最大流量 maxFlow 必须是数字。' });
          appendLog('setWaterFlowConfig', false, 'maxFlow 非法');
          return;
        }
        request.maxFlow = v;
        filled.push('maxFlow');
      }

      if (filled.length === 0) {
        setNotice({
          kind: 'error',
          title: '参数缺失',
          detail: '至少需要填写一项配置（k / offset / minFlow / maxFlow）。',
        });
        appendLog('setWaterFlowConfig', false, '空请求被拒');
        return;
      }

      const response = await anshengApi.setWaterFlowConfig(selectedDeviceId, request);
      const ok = consumeWaterResult(
        'setWaterFlowConfig',
        response.data.code,
        response.data.message,
        response.data.data,
        `已下发 setWaterFlowConfig（${filled.join(' / ')}）`
      );
      if (ok) {
        scheduleConfigRefresh(selectedDeviceId);
      }
    } catch (err: unknown) {
      handleWaterSendError(err, 'setWaterFlowConfig', '设置配置失败');
    } finally {
      setConfigBusy(false);
    }
  }, [
    selectedDeviceId, canSend, kInput, offsetInput, minFlowInput, maxFlowInput,
    consumeWaterResult, scheduleConfigRefresh, appendLog, handleWaterSendError,
  ]);

  /** 重置配置（POST /water/flow/config/reset）。 */
  const handleResetConfig = useCallback(async (): Promise<void> => {
    if (selectedDeviceId === null || !canSend) {
      return;
    }
    setConfigBusy(true);
    setNotice(null);
    try {
      const response = await anshengApi.resetWaterFlowConfig(selectedDeviceId);
      const ok = consumeWaterResult(
        'resetWaterFlowConfig',
        response.data.code,
        response.data.message,
        response.data.data,
        '已下发 resetFlowConfig，重置流量配置'
      );
      if (ok) {
        scheduleConfigRefresh(selectedDeviceId);
      }
    } catch (err: unknown) {
      handleWaterSendError(err, 'resetWaterFlowConfig', '重置配置失败');
    } finally {
      setConfigBusy(false);
    }
  }, [selectedDeviceId, canSend, consumeWaterResult, scheduleConfigRefresh, handleWaterSendError]);

  // ── 初始加载 ───────────────────────────────────────────────
  useEffect(() => {
    void loadDevices();
  }, [loadDevices]);

  // ── 切换设备：重置面板并拉取该设备数据 ─────────────────────
  useEffect(() => {
    clearRefreshTimers();
    setNotice(null);
    setFlowInfo(null);
    setConfig(null);
    setKInput('');
    setOffsetInput('');
    setMinFlowInput('');
    setMaxFlowInput('');
    setPullNote(null);
    if (selectedDeviceId === null) {
      return;
    }
    void queryFlowInfoRef.current(selectedDeviceId);
    void queryConfigRef.current(selectedDeviceId);
  }, [selectedDeviceId, clearRefreshTimers]);

  // ── 卸载清理 ───────────────────────────────────────────────
  useEffect(() => clearRefreshTimers, [clearRefreshTimers]);

  // ── 无查看权限：直接门控整页 ───────────────────────────────
  if (!canView) {
    return (
      <div className="p-6">
        <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
          <ShieldAlert className="w-12 h-12 text-amber-500/70 mx-auto mb-3" />
          <p className="text-slate-300 text-sm font-medium">无权访问「安圣水流量」</p>
          <p className="text-slate-500 text-xs mt-1">需要「查看设备」（view_devices）权限，请联系管理员开通</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      {/* 页面标题 */}
      <div className="flex items-center justify-between flex-wrap gap-3">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-lg bg-amber-500/20 border border-amber-500/30">
            <Droplet className="w-6 h-6 text-amber-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-white">安圣水流量</h1>
            <p className="text-sm text-slate-400">实时流量 · 流量配置</p>
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
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        {/* 左栏：设备选择 */}
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
              <div className="space-y-1 max-h-[420px] overflow-y-auto">
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
        </div>

        {/* 右栏：两块功能 */}
        <div className="xl:col-span-2 space-y-4">
          {selectedDeviceId === null ? (
            <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
              <Droplet className="w-12 h-12 text-slate-600 mx-auto mb-3" />
              <p className="text-slate-500 text-sm">请先选择一个安圣设备</p>
              <p className="text-slate-600 text-xs mt-1">选择设备后即可查看实时流量与流量配置</p>
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

              {/* ① 实时流量 */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                  <Gauge className="w-4 h-4 text-amber-400" />
                  实时流量
                  <span className="text-xs text-slate-500 font-normal">（下发 getFlowInfo）</span>
                </h3>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5">
                  <div className="rounded-lg bg-slate-900/40 border border-slate-700/40 p-3">
                    <div className="text-[11px] text-slate-500 mb-1 flex items-center gap-1">
                      <Droplet className="w-3 h-3" /> 瞬时流量
                    </div>
                    <div className="text-lg font-semibold text-slate-200">
                      {formatFlowNumber(flowInfo?.iFlow)}
                      <span className="text-xs text-slate-500 ml-1">L/min</span>
                    </div>
                  </div>
                  <div className="rounded-lg bg-slate-900/40 border border-slate-700/40 p-3">
                    <div className="text-[11px] text-slate-500 mb-1 flex items-center gap-1">
                      <Waves className="w-3 h-3" /> 累计流量
                    </div>
                    <div className="text-lg font-semibold text-slate-200">
                      {formatFlowNumber(flowInfo?.tFlow)}
                      <span className="text-xs text-slate-500 ml-1">L</span>
                    </div>
                  </div>
                  <div className="rounded-lg bg-slate-900/40 border border-slate-700/40 p-3">
                    <div className="text-[11px] text-slate-500 mb-1 flex items-center gap-1">
                      <Activity className="w-3 h-3" /> 总脉冲
                    </div>
                    <div className="text-lg font-semibold text-slate-200">
                      {formatFlowNumber(flowInfo?.tPulses, 0)}
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-3 flex-wrap">
                  <button
                    onClick={() => { void handlePullFlow(); }}
                    disabled={!canSend || pullBusy}
                    className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-medium
                               bg-amber-600/25 text-amber-300 border border-amber-500/30
                               hover:bg-amber-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {pullBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <RefreshCw className="w-3.5 h-3.5" />}
                    拉取实时流量
                  </button>
                  {flowInfoLoading && (
                    <span className="text-xs text-slate-400 flex items-center gap-1.5">
                      <Loader2 className="w-3.5 h-3.5 animate-spin" /> 读取镜像中...
                    </span>
                  )}
                  {pullNote && (
                    <span className="text-xs text-slate-400">{pullNote}</span>
                  )}
                </div>
                {flowInfo?.timestamp !== undefined && flowInfo?.timestamp !== null && (
                  <p className="text-[11px] text-slate-600 flex items-center gap-1">
                    <Clock className="w-3 h-3" /> 镜像同步时间：{formatTimestamp(flowInfo.timestamp)}
                  </p>
                )}
              </div>

              {/* ② 流量配置 */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                  <SlidersHorizontal className="w-4 h-4 text-amber-400" />
                  流量配置
                  <span className="text-xs text-slate-500 font-normal">（k · offset · minFlow · maxFlow）</span>
                </h3>

                {/* 当前配置镜像 */}
                <div className="rounded-lg bg-slate-900/40 border border-slate-700/40 p-3 space-y-1">
                  <div className="text-[11px] text-slate-500 mb-1">当前平台镜像</div>
                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
                    <div>
                      <div className="text-slate-500">K</div>
                      <div className="text-slate-200 font-mono">{formatFlowNumber(config?.k)}</div>
                    </div>
                    <div>
                      <div className="text-slate-500">offset</div>
                      <div className="text-slate-200 font-mono">{formatFlowNumber(config?.offset)}</div>
                    </div>
                    <div>
                      <div className="text-slate-500">minFlow</div>
                      <div className="text-slate-200 font-mono">{formatFlowNumber(config?.minFlow)}</div>
                    </div>
                    <div>
                      <div className="text-slate-500">maxFlow</div>
                      <div className="text-slate-200 font-mono">{formatFlowNumber(config?.maxFlow)}</div>
                    </div>
                  </div>
                  {config?.timestamp !== undefined && config?.timestamp !== null && (
                    <p className="text-[11px] text-slate-600 flex items-center gap-1 pt-1">
                      <Clock className="w-3 h-3" /> 镜像同步时间：{formatTimestamp(config.timestamp)}
                    </p>
                  )}
                </div>

                <div className="flex items-center gap-2">
                  <button
                    onClick={() => { void handleReadConfig(); }}
                    disabled={configBusy}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs
                               text-cyan-400 border border-cyan-500/30 bg-cyan-600/15
                               hover:bg-cyan-600/25 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {configBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}
                    读取配置
                  </button>
                  <span className="text-[11px] text-slate-600">读取后自动回填下方表单</span>
                </div>

                {/* 四个配置输入框 */}
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">流量系数 K</label>
                    <input
                      type="number" step="any"
                      value={kInput}
                      disabled={!canSend || configBusy}
                      onChange={e => setKInput(e.target.value)}
                      placeholder="如 1.0"
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                    />
                  </div>
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">流量偏移 offset</label>
                    <input
                      type="number" step="any"
                      value={offsetInput}
                      disabled={!canSend || configBusy}
                      onChange={e => setOffsetInput(e.target.value)}
                      placeholder="如 0"
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                    />
                  </div>
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">最小流量 minFlow (L/min)</label>
                    <input
                      type="number" step="any"
                      value={minFlowInput}
                      disabled={!canSend || configBusy}
                      onChange={e => setMinFlowInput(e.target.value)}
                      placeholder="如 0"
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                    />
                  </div>
                  <div>
                    <label className="block text-[11px] text-slate-500 mb-1">最大流量 maxFlow (L/min)</label>
                    <input
                      type="number" step="any"
                      value={maxFlowInput}
                      disabled={!canSend || configBusy}
                      onChange={e => setMaxFlowInput(e.target.value)}
                      placeholder="如 9999"
                      className="w-full px-2.5 py-1.5 rounded-md bg-slate-900/60 border border-slate-700 text-xs text-slate-200
                                 focus:outline-none focus:border-amber-500/50 disabled:opacity-50 transition-all"
                    />
                  </div>
                </div>

                <div className="flex items-center gap-2 flex-wrap pt-1">
                  <button
                    onClick={() => { void handleSetConfig(); }}
                    disabled={!canSend || configBusy}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium
                               bg-amber-600/25 text-amber-300 border border-amber-500/30
                               hover:bg-amber-600/35 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {configBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
                    设置配置
                  </button>
                  <button
                    onClick={() => { void handleResetConfig(); }}
                    disabled={!canSend || configBusy}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs
                               text-slate-300 border border-slate-700/50
                               hover:bg-slate-700/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                  >
                    {configBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <RotateCcw className="w-3.5 h-3.5" />}
                    重置配置
                  </button>
                  <span className="text-[11px] text-slate-600">仅填写的字段会被下发；重置为出厂默认值</span>
                </div>
              </div>

              {/* 命令日志 */}
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-3">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                  <Activity className="w-4 h-4 text-amber-400" />
                  命令日志
                  <span className="text-xs text-slate-500 font-normal">（记录每次下发 / 读取的方法与结果）</span>
                </h3>

                {commandLog.length === 0 ? (
                  <div className="text-center py-6 text-slate-600 text-sm">暂无命令记录</div>
                ) : (
                  <div className="space-y-1.5 max-h-[280px] overflow-y-auto pr-1">
                    {commandLog.map((entry, index) => (
                      <div
                        key={`${entry.method}-${entry.time.getTime()}-${index}`}
                        className="flex items-start gap-2 px-3 py-2 rounded-lg bg-slate-900/40 border border-slate-700/30"
                      >
                        {entry.success ? (
                          <CheckCircle className="w-4 h-4 text-green-400 flex-shrink-0 mt-0.5" />
                        ) : (
                          <AlertCircle className="w-4 h-4 text-red-400 flex-shrink-0 mt-0.5" />
                        )}
                        <div className="min-w-0 flex-1">
                          <div className="text-xs">
                            <span className="text-slate-300 font-mono">{entry.method}</span>
                            <span
                              className={`ml-1.5 px-1.5 py-0.5 rounded text-[10px] ${
                                entry.success
                                  ? 'bg-green-500/15 text-green-400'
                                  : 'bg-red-500/15 text-red-400'
                              }`}
                            >
                              {entry.success ? '成功' : '失败'}
                            </span>
                          </div>
                          <div className="text-[11px] text-slate-400 mt-0.5 break-all">{entry.message}</div>
                        </div>
                        <span className="text-[10px] text-slate-600 flex-shrink-0">
                          {formatLogTime(entry.time)}
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

export default WaterFlowPage;
