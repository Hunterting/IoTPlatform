import { useState, useMemo, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  ChevronRight, ChevronDown, Grid3x3, Play, Pause, Volume2, VolumeX,
  Settings, AlertCircle, Clock, Camera as CameraIcon, X, Download,
  Calendar, Search, Check, Plus, ArrowUp, ArrowDown, ArrowLeft, ArrowRight,
  ShieldAlert,
} from 'lucide-react';
import { PieChart, Pie, Cell } from 'recharts';
import { useAuth } from '@/app/contexts/AuthContext';
import { useArea } from '@/app/contexts/AreaContext';
import { HlsPlayer } from '@/app/components/HlsPlayer';
import {
  ezvizApi,
  type EzvizDevice,
  type EzvizConfigStatus,
  type EzvizPlaybackRecord,
  type EzvizLiveAddress,
} from '@/app/services/api/ezvizApi';
import { dashboardApi, type AlertDto } from '@/app/services/api/dashboardApi';

type GridMode = '1x1' | '2x2' | '3x3' | '4x4';
const GRID_COUNT: Record<GridMode, number> = { '1x1': 1, '2x2': 4, '3x3': 9, '4x4': 16 };
const GRID_COLS: Record<GridMode, number> = { '1x1': 1, '2x2': 2, '3x3': 3, '4x4': 4 };

/** 真实告警 → 右侧列表展示项 */
interface AlertView {
  id: string;
  type: string;
  level: string;
  deviceName: string;
  area: string;
  time: string;
}
const ALERT_TYPE_LABEL: Record<string, string> = {
  temperature: '温度异常', pressure: '压力异常', offline: '设备离线',
  power: '功率异常', smoke: '烟雾异常', door: '门体异常',
};
function toAlertView(a: AlertDto): AlertView {
  const d = new Date(a.alertTime);
  const time = Number.isNaN(d.getTime())
    ? String(a.alertTime ?? '')
    : `${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  return {
    id: String(a.id),
    type: ALERT_TYPE_LABEL[a.alertType] || a.alertType || '异常',
    level: a.level,
    deviceName: a.deviceName || '未知设备',
    area: a.area || '—',
    time,
  };
}

export function MonitoringPage() {
  const { currentCustomer } = useAuth();
  const { areas, flattenAreas } = useArea();
  const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set());
  const [selectedArea, setSelectedArea] = useState<string | null>(null);

  // ── 真实萤石数据 ──
  const [configStatus, setConfigStatus] = useState<EzvizConfigStatus | null>(null);
  const [devices, setDevices] = useState<EzvizDevice[]>([]);
  // 绑定弹窗专用：萤石账号下全部设备（含未绑定），与宫格的租户隔离列表分开
  const [bindableDevices, setBindableDevices] = useState<EzvizDevice[]>([]);
  const [alerts, setAlerts] = useState<AlertView[]>([]);
  const [alertTotal, setAlertTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [liveUrls, setLiveUrls] = useState<Record<string, EzvizLiveAddress>>({});
  const [panelError, setPanelError] = useState<Record<string, string>>({});

  // 工具栏
  const [gridMode, setGridMode] = useState<GridMode>('4x4');
  const [isPlaying, setIsPlaying] = useState(true);
  const [isMuted, setIsMuted] = useState(true);
  const [showGridModal, setShowGridModal] = useState(false);
  const [showControlModal, setShowControlModal] = useState(false);
  const [showScreenshotModal, setShowScreenshotModal] = useState(false);
  const [showPlaybackModal, setShowPlaybackModal] = useState(false);
  const [showSettingsModal, setShowSettingsModal] = useState(false);
  const [showAddCameraModal, setShowAddCameraModal] = useState(false);
  const [selectedCameras, setSelectedCameras] = useState<string[]>([]);
  const [playbackDate, setPlaybackDate] = useState(new Date().toISOString().split('T')[0]);
  const [playbackTime, setPlaybackTime] = useState('00:00');
  const [playbackDevice, setPlaybackDevice] = useState('');
  const [playbackRecords, setPlaybackRecords] = useState<EzvizPlaybackRecord[] | null>(null);
  const [captureResults, setCaptureResults] = useState<{ name: string; picUrl: string }[] | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [actionMsg, setActionMsg] = useState<string | null>(null);

  // 添加摄像头（真实：把萤石设备绑定到本地区域）
  const [bindForm, setBindForm] = useState({ deviceSerial: '', name: '', areaId: '', ewcode: '' });

  // ── 数据加载（每源独立容错，单接口失败不影响整页） ──
  const loadConfig = useCallback(async () => {
    try {
      const r = await ezvizApi.getConfigStatus();
      if (r.code === 200 && r.data) setConfigStatus(r.data);
      else setLoadError(r.message || '萤石配置状态查询失败');
    } catch { setLoadError('萤石配置状态查询失败'); }
  }, []);

  const loadDevices = useCallback(async () => {
    try {
      const r = await ezvizApi.getDevices({ pageStart: 0, pageSize: 50 });
      if (r.code === 200 && r.data) { setDevices(r.data.items || []); setPanelError(p => { const n = { ...p }; delete n.devices; return n; }); }
      else setPanelError(p => ({ ...p, devices: r.message || '设备列表不可用' }));
    } catch { setPanelError(p => ({ ...p, devices: '无权限或网络错误' })); }
  }, []);

  // 绑定弹窗：拉取萤石账号全部设备（含未绑定），供下拉选择
  const loadBindableDevices = useCallback(async () => {
    try {
      const r = await ezvizApi.getDevices({ pageStart: 0, pageSize: 50, includeUnbound: true });
      if (r.code === 200 && r.data) setBindableDevices(r.data.items || []);
    } catch { /* 绑定弹窗加载失败时保留空列表，不阻断其它面板 */ }
  }, []);

  const loadAlerts = useCallback(async () => {
    try {
      const r = await dashboardApi.getAlerts({ page: 1, pageSize: 6, status: 'pending' });
      let items = r.code === 200 && r.data ? (r.data.items || []) : [];
      let total = r.code === 200 && r.data ? (r.data.totalCount || 0) : 0;
      if (items.length === 0) {
        const fb = await dashboardApi.getAlerts({ page: 1, pageSize: 6 });
        items = fb.code === 200 && fb.data ? (fb.data.items || []) : [];
        total = fb.code === 200 && fb.data ? (fb.data.totalCount || 0) : 0;
      }
      setAlerts(items.map(toAlertView));
      setAlertTotal(total);
      setPanelError(p => { const n = { ...p }; delete n.alerts; return n; });
    } catch { setPanelError(p => ({ ...p, alerts: '告警数据不可用' })); }
  }, []);

  useEffect(() => {
    let cancelled = false;
    const initial = async () => {
      setLoading(true);
      await Promise.all([loadConfig(), loadDevices(), loadAlerts()]);
      if (!cancelled) setLoading(false);
    };
    initial();
    const timer = setInterval(() => { if (!cancelled) { loadDevices(); loadAlerts(); } }, 60000);
    return () => { cancelled = true; clearInterval(timer); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const configured = configStatus?.configured === true;

  // ── 直播地址：仅为当前分屏内可见设备拉取，避免 16 路并发触发萤石频控 ──
  useEffect(() => {
    if (!configured || devices.length === 0) return;
    let cancelled = false;
    const visible = filterDevices(devices, selectedArea, areas, flattenAreas).slice(0, GRID_COUNT[gridMode]);
    // 分享设备（operable=false）真机实测直播/抓拍被萤石拒绝，不发起无谓请求
    const missing = visible.filter(d => d.deviceSerial && d.operable !== false && !liveUrls[d.deviceSerial]);
    if (missing.length === 0) return;
    (async () => {
      for (const d of missing) {
        if (cancelled) return;
        try {
          const r = await ezvizApi.getLiveAddress({ deviceSerial: d.deviceSerial, channelNo: d.channelNo || 1 });
          if (r.code === 200 && r.data?.url) {
            setLiveUrls(prev => ({ ...prev, [d.deviceSerial]: r.data! }));
          } else {
            setPanelError(p => ({ ...p, [`live:${d.deviceSerial}`]: r.message || '直播地址获取失败' }));
          }
        } catch {
          setPanelError(p => ({ ...p, [`live:${d.deviceSerial}`]: '直播地址请求失败' }));
        }
      }
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [configured, devices, selectedArea, gridMode]);

  // ── 派生 ──
  const filtered = useMemo(
    () => filterDevices(devices, selectedArea, areas, flattenAreas),
    [devices, selectedArea, areas, flattenAreas]
  );
  const displayCameras = filtered.slice(0, GRID_COUNT[gridMode]);
  const onlineCount = devices.filter(d => d.status === 1).length;
  const offlineCount = devices.filter(d => d.status !== 1).length;
  const deviceStats = [
    { name: '在线数', value: onlineCount, color: '#00ff9d' },
    { name: '离线数', value: offlineCount, color: '#666' },
  ];
  const todayAlertCount = alerts.filter(a => {
    const d = new Date(); return a.time.startsWith(`${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`);
  }).length;

  const areaTree = useMemo(() => {
    if (!currentCustomer || areas.length === 0) return [];
    return areas.filter(a => a.appCode === currentCustomer.appCode);
  }, [areas, currentCustomer]);

  const toggleNode = (id: string) => setExpandedNodes(prev => {
    const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n;
  });

  const renderAreaTree = (list: typeof areas, level = 0): React.ReactNode => list.map(area => {
    const hasChildren = area.children && area.children.length > 0;
    const isExpanded = expandedNodes.has(area.id);
    const isSelected = selectedArea === area.id;
    return (
      <div key={area.id}>
        <motion.div
          className={`flex items-center gap-2 px-2 py-1.5 rounded cursor-pointer transition-colors ${isSelected ? 'bg-blue-500/20 text-blue-400' : 'hover:bg-white/5 text-gray-300'}`}
          style={{ paddingLeft: `${level * 16 + 8}px` }}
          onClick={() => { setSelectedArea(area.id); if (hasChildren) toggleNode(area.id); }}
        >
          {hasChildren ? (isExpanded ? <ChevronDown className="w-4 h-4 flex-shrink-0" /> : <ChevronRight className="w-4 h-4 flex-shrink-0" />) : <div className="w-4" />}
          {area.type === 'level1' && <div className="w-2 h-2 rounded-full bg-purple-500 flex-shrink-0" />}
          {area.type === 'level2' && <div className="w-2 h-2 rounded-full bg-yellow-500 flex-shrink-0" />}
          {area.type === 'level3' && <div className="w-2 h-2 rounded-full bg-blue-500 flex-shrink-0" />}
          <span className="text-sm truncate">{area.name}</span>
        </motion.div>
        {hasChildren && isExpanded && (
          <motion.div initial={{ height: 0, opacity: 0 }} animate={{ height: 'auto', opacity: 1 }} exit={{ height: 0, opacity: 0 }}>
            {renderAreaTree(area.children || [], level + 1)}
          </motion.div>
        )}
      </div>
    );
  });

  // ── 工具栏真实动作 ──
  const handleCapture = async () => {
    if (selectedCameras.length === 0) return;
    setActionBusy(true); setCaptureResults(null);
    const results: { name: string; picUrl: string }[] = [];
    for (const serial of selectedCameras) {
      const dev = devices.find(d => d.deviceSerial === serial);
      try {
        const r = await ezvizApi.capture({ deviceSerial: serial, channelNo: 1 });
        if (r.code === 200 && r.data?.picUrl) results.push({ name: dev?.deviceName || serial, picUrl: r.data.picUrl });
        else setPanelError(p => ({ ...p, [`cap:${serial}`]: r.message || '抓拍失败' }));
      } catch { setPanelError(p => ({ ...p, [`cap:${serial}`]: '抓拍请求失败' })); }
    }
    setCaptureResults(results); setActionBusy(false);
  };

  const handlePlayback = async () => {
    const serial = playbackDevice || displayCameras[0]?.deviceSerial;
    if (!serial) { setActionMsg('请先选择摄像头'); return; }
    const start = new Date(`${playbackDate}T${playbackTime}:00`);
    const end = new Date(start.getTime() + 60 * 60 * 1000);
    if (Number.isNaN(start.getTime())) { setActionMsg('日期/时间无效'); return; }
    setActionBusy(true); setActionMsg(null);
    try {
      const r = await ezvizApi.getPlayback({
        deviceSerial: serial,
        channelNo: 1,
        startTime: String(start.getTime()),
        endTime: String(end.getTime()),
        recType: 0,
      });
      if (r.code === 200) setPlaybackRecords(r.data || []);
      else setActionMsg(r.message || '回放查询失败');
    } catch { setActionMsg('回放查询请求失败'); }
    setActionBusy(false);
  };

  const handlePtz = async (direction: number) => {
    const serial = displayCameras[0]?.deviceSerial;
    if (!serial) { setActionMsg('无可用设备'); return; }
    setActionBusy(true); setActionMsg(null);
    try {
      const req = { deviceSerial: serial, channelNo: 1, direction, speed: 1 };
      const start = await ezvizApi.ptzStart(req);
      if (start.code !== 200) { setActionMsg(start.message || '云台指令失败'); }
      else {
        await new Promise(res => setTimeout(res, 500));
        await ezvizApi.ptzStop(req);
        setActionMsg('云台指令已下发');
      }
    } catch { setActionMsg('云台控制请求失败'); }
    setActionBusy(false);
  };

  const handleBind = async () => {
    if (!bindForm.deviceSerial) { setActionMsg('请选择萤石设备'); return; }
    setActionBusy(true); setActionMsg(null);
    try {
      const r = await ezvizApi.bindCamera({
        deviceSerial: bindForm.deviceSerial,
        name: bindForm.name || bindForm.deviceSerial,
        areaId: bindForm.areaId ? Number(bindForm.areaId) : null,
        channelNo: 1,
        ewcode: bindForm.ewcode || undefined,
      });
      if (r.code === 200) {
        setActionMsg('设备已绑定区域');
        setShowAddCameraModal(false);
        setBindForm({ deviceSerial: '', name: '', areaId: '', ewcode: '' });
        await loadDevices();
      } else setActionMsg(r.message || '绑定失败');
    } catch { setActionMsg('绑定请求失败'); }
    setActionBusy(false);
  };

  const closeActionMsg = () => setActionMsg(null);

  return (
    <div className="h-screen flex flex-col bg-[#0a0e1a] overflow-hidden">
      {/* 未配置凭证：诚实提示，不显示任何模拟画面 */}
      {!loading && !configured && (
        <div className="flex-shrink-0 mx-2 mt-2 px-4 py-2 rounded-lg flex items-start gap-3"
          style={{ background: 'rgba(255,170,0,0.08)', border: '1px solid rgba(255,170,0,0.35)' }}>
          <ShieldAlert className="w-4 h-4 mt-0.5 flex-shrink-0" style={{ color: '#ffaa00' }} />
          <div className="text-xs" style={{ color: '#ffcc66' }}>
            <p className="font-semibold mb-0.5">未配置萤石开放平台凭证</p>
            <p className="opacity-80">{configStatus?.message || '请在后端配置 appKey / appSecret'}</p>
          </div>
        </div>
      )}

      <div className="flex-1 flex gap-2 p-2 overflow-hidden">
        {/* 左侧面板 */}
        <div className="w-56 flex flex-col gap-2 overflow-hidden">
          <div className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3 overflow-hidden flex flex-col">
            <div className="flex items-center justify-between mb-2 flex-shrink-0">
              <h3 className="text-sm font-bold text-white">区域导航</h3>
              {selectedArea && (
                <button onClick={() => setSelectedArea(null)} className="text-[10px] text-gray-400 hover:text-white">清除</button>
              )}
            </div>
            <div className="flex-1 overflow-y-auto custom-scrollbar">
              {areaTree.length > 0 ? renderAreaTree(areaTree) : (
                <p className="text-xs text-gray-500 text-center py-4">暂无区域数据</p>
              )}
            </div>
          </div>

          <div className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3">
            <h3 className="text-sm font-bold text-white mb-2">区域设备筛选</h3>
            <p className="text-xs text-gray-400">
              {selectedArea
                ? `当前区域：${flattenAreas(areas).find(a => a.id === selectedArea)?.name || '—'}`
                : '点击上方区域可筛选已绑定该区域的萤石设备'}
            </p>
            <p className="text-xs mt-2" style={{ color: '#5a7a90' }}>
              萤石设备无区域属性，需在「添加摄像头/绑定」中绑定本地区域。
            </p>
          </div>

          {/* 设备状态（真实在线/离线） */}
          <div className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3">
            <h3 className="text-sm font-bold text-white mb-2">设备状态</h3>
            <div className="flex items-center justify-center">
              <div className="relative w-32 h-32">
                <PieChart width={128} height={128}>
                  <Pie data={deviceStats} cx={64} cy={64} innerRadius={35} outerRadius={55} paddingAngle={2} dataKey="value">
                    {deviceStats.map((entry, index) => <Cell key={`cell-${index}`} fill={entry.color} />)}
                  </Pie>
                </PieChart>
                <div className="absolute inset-0 flex flex-col items-center justify-center">
                  <div className="text-lg font-bold text-white">{devices.length}</div>
                  <div className="text-xs text-gray-400">设备总数</div>
                  {devices.length > 0 && (
                    <div className="text-xs text-gray-500 mt-1">({((onlineCount / devices.length) * 100).toFixed(0)}% 在线)</div>
                  )}
                </div>
              </div>
            </div>
            <div className="mt-2 flex items-center justify-between text-xs">
              <div className="flex items-center gap-1">
                <div className="w-2 h-2 rounded-full bg-[#00ff9d]" />
                <span className="text-gray-400">在线</span>
                <span className="text-white font-bold">{onlineCount}</span>
              </div>
              <div className="flex items-center gap-1">
                <div className="w-2 h-2 rounded-full bg-[#666]" />
                <span className="text-gray-400">离线</span>
                <span className="text-white font-bold">{offlineCount}</span>
              </div>
            </div>
            {panelError.devices && <p className="text-xs mt-2" style={{ color: '#ffaa00' }}>{panelError.devices}</p>}
          </div>
        </div>

        {/* 中间：实时画面网格 */}
        <div className="flex-1 flex flex-col gap-2 overflow-hidden">
          <div className="flex-1 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3 overflow-hidden flex flex-col">
            <div className="flex items-center justify-between mb-2 flex-shrink-0">
              <h3 className="text-sm font-bold text-white">
                实时画面 · {gridMode}
                <span className="text-xs font-normal text-gray-400 ml-2">{displayCameras.length} 路</span>
              </h3>
              <span className="text-[10px]" style={{ color: devices.some(d => d.isShared) ? '#ffaa00' : '#5a7a90' }}>
                {configured
                  ? (devices.some(d => d.isShared)
                      ? '数据源：萤石开放平台（含分享设备，部分操作受账号权限限制）'
                      : '数据源：萤石开放平台 HLS')
                  : '数据源：未接入'}
              </span>
            </div>

            <div className="flex-1 overflow-hidden">
              {loading ? (
                <div className="h-full flex items-center justify-center text-xs text-gray-500">加载萤石设备…</div>
              ) : !configured ? (
                <div className="h-full flex flex-col items-center justify-center gap-2">
                  <CameraIcon className="w-14 h-14" style={{ color: '#3a5a70' }} />
                  <p className="text-sm text-gray-400">配置萤石凭证后显示真实设备画面</p>
                  <p className="text-xs" style={{ color: '#5a7a90' }}>当前不展示任何模拟画面</p>
                </div>
              ) : displayCameras.length === 0 ? (
                <div className="h-full flex flex-col items-center justify-center gap-2">
                  <CameraIcon className="w-14 h-14" style={{ color: '#3a5a70' }} />
                  <p className="text-sm text-gray-400">
                    {panelError.devices || (devices.length === 0 ? '萤石账号下暂无设备' : '当前区域无已绑定设备')}
                  </p>
                </div>
              ) : (
                <div className="h-full grid gap-1 overflow-hidden"
                  style={{ gridTemplateColumns: `repeat(${GRID_COLS[gridMode]}, minmax(0,1fr))`, gridTemplateRows: `repeat(${GRID_COLS[gridMode]}, minmax(0,1fr))` }}>
                  {displayCameras.map((dev) => {
                    const live = liveUrls[dev.deviceSerial];
                    const err = panelError[`live:${dev.deviceSerial}`];
                    const isOnline = dev.status === 1;
                    const notOperable = dev.operable === false;
                    const isEncryptErr = !!err && (err.includes('加密') || err.includes('60019'));
                    const hint = notOperable
                      ? '分享设备：账号无直播/抓拍权限（萤石侧限制）'
                      : !isOnline ? '设备离线' : isEncryptErr ? '该设备已加密，请到「添加/绑定摄像头」处录入设备验证码' : err || '获取直播地址中…';
                    return (
                      <div key={dev.deviceSerial} className="relative bg-black rounded overflow-hidden border border-white/10 group">
                        {live?.url ? (
                          <HlsPlayer url={live.url} playing={isPlaying} muted={isMuted} />
                        ) : dev.picUrl ? (
                          <div className="w-full h-full relative">
                            <img src={dev.picUrl} alt={dev.deviceName} className="w-full h-full object-cover opacity-70" />
                            <div className="absolute inset-0 flex items-end justify-center pb-4 px-2">
                              <span className="text-[10px] text-center leading-tight" style={{ color: '#ffcc66', textShadow: '0 0 4px #000' }}>{hint}</span>
                            </div>
                          </div>
                        ) : (
                          <div className="w-full h-full flex flex-col items-center justify-center gap-1 px-2 text-center">
                            <CameraIcon className="w-6 h-6" style={{ color: '#3a5a70' }} />
                            <span className="text-[10px] leading-tight" style={{ color: notOperable || err ? '#ffaa00' : '#5a7a90' }}>
                              {hint}
                            </span>
                          </div>
                        )}
                        <div className="absolute inset-0 bg-gradient-to-t from-black/70 via-transparent to-black/30 pointer-events-none" />
                        <div className="absolute top-1 left-1 bg-black/60 px-1.5 py-0.5 rounded text-[10px] text-white truncate max-w-[70%]">
                          {dev.deviceName || dev.deviceSerial}
                        </div>
                        <div className="absolute top-1 right-1">
                          <div className={`w-2 h-2 rounded-full ${isOnline ? 'bg-green-500' : 'bg-red-500'}`} />
                        </div>
                        <div className="absolute bottom-1 left-1 bg-black/60 px-1.5 py-0.5 rounded text-[9px] text-white/70">
                          {dev.areaName || dev.deviceSerial}
                        </div>
                        <div className="absolute bottom-1 right-1 bg-black/60 px-1.5 py-0.5 rounded text-[9px] text-white/70">
                          {new Date().toLocaleTimeString('zh-CN', { hour12: false })}
                        </div>
                      </div>
                    );
                  })}
                  {Array.from({ length: Math.max(0, GRID_COUNT[gridMode] - displayCameras.length) }).map((_, i) => (
                    <div key={`empty-${i}`} className="bg-black/30 rounded border border-white/5 flex items-center justify-center">
                      <span className="text-gray-600 text-xs">无信号</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          {/* 底部工具栏（全部接真实功能） */}
          <div className="h-12 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 px-4 flex items-center justify-center gap-4 flex-shrink-0 flex-wrap">
            <ToolButton icon={<Grid3x3 className="w-3 h-3" />} label="分屏切换" onClick={() => setShowGridModal(true)} />
            <ToolButton icon={<Play className="w-3 h-3" />} label="画面控制" onClick={() => setShowControlModal(true)} disabled={!configured} />
            <ToolButton icon={<CameraIcon className="w-3 h-3" />} label="截图取证" onClick={() => { setCaptureResults(null); setShowScreenshotModal(true); }} disabled={!configured} />
            <ToolButton icon={<Clock className="w-3 h-3" />} label="回放查询" onClick={() => { setPlaybackRecords(null); setShowPlaybackModal(true); }} disabled={!configured} />
            <ToolButton icon={<Settings className="w-3 h-3" />} label="系统设置" onClick={() => setShowSettingsModal(true)} />
            <ToolButton icon={<Plus className="w-3 h-3" />} label="添加/绑定" onClick={() => { loadBindableDevices(); setBindableDevices([]); setShowAddCameraModal(true); }} disabled={!configured} />
          </div>
        </div>

        {/* 右侧：实时报警（接本平台 /alerts，不伪造） */}
        <div className="w-64 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3 flex flex-col overflow-hidden">
          <h3 className="text-sm font-bold text-white mb-3 flex-shrink-0">实时报警</h3>
          <div className="grid grid-cols-2 gap-2 mb-3 flex-shrink-0">
            <div className="bg-gradient-to-br from-red-500/20 to-red-600/10 border border-red-500/30 rounded-lg p-2">
              <div className="flex items-center gap-1 mb-1">
                <AlertCircle className="w-3 h-3 text-red-400" />
                <span className="text-[10px] text-red-300">当前待处理</span>
              </div>
              <div className="text-xl font-bold text-white">{alerts.length}</div>
            </div>
            <div className="bg-gradient-to-br from-blue-500/20 to-blue-600/10 border border-blue-500/30 rounded-lg p-2">
              <div className="flex items-center gap-1 mb-1">
                <AlertCircle className="w-3 h-3 text-blue-400" />
                <span className="text-[10px] text-blue-300">告警总数</span>
              </div>
              <div className="text-xl font-bold text-white">{alertTotal}</div>
            </div>
          </div>
          <div className="flex-1 overflow-y-auto custom-scrollbar space-y-2">
            {alerts.map(alarm => (
              <div key={alarm.id} className="bg-white/5 border border-white/10 rounded-lg p-2">
                <div className="flex items-start justify-between gap-2 mb-1">
                  <div className="flex items-center gap-1.5 min-w-0">
                    <div className={`w-2 h-2 rounded-full flex-shrink-0 ${alarm.level === 'critical' ? 'bg-red-500' : alarm.level === 'warning' ? 'bg-yellow-500' : 'bg-blue-500'}`} />
                    <span className="text-xs font-medium text-white truncate">事件类型：{alarm.type}</span>
                  </div>
                </div>
                <p className="text-[10px] text-gray-400">设备：{alarm.deviceName}</p>
                <p className="text-[10px] text-gray-500">区域：{alarm.area}</p>
                <p className="text-[10px] text-gray-500">时间：{alarm.time}</p>
              </div>
            ))}
            {alerts.length === 0 && (
              <div className="h-full flex flex-col items-center justify-center gap-2 py-8">
                <AlertCircle className="w-10 h-10" style={{ color: '#3a5a70' }} />
                <p className="text-xs" style={{ color: panelError.alerts ? '#ffaa00' : '#5a7a90' }}>
                  {panelError.alerts || '暂无待处理告警'}
                </p>
                <p className="text-[10px] text-center px-2" style={{ color: '#4a6a80' }}>
                  萤石告警走消息回调，暂无拉取接口；此处展示本平台告警。
                </p>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* 通用动作提示 */}
      <AnimatePresence>
        {actionMsg && (
          <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
            className="fixed bottom-20 left-1/2 -translate-x-1/2 z-[60] px-4 py-2 rounded-lg text-xs text-white"
            style={{ background: 'rgba(0,20,50,0.95)', border: '1px solid rgba(0,195,255,0.4)' }}
            onClick={closeActionMsg}>
            {actionMsg}
          </motion.div>
        )}
      </AnimatePresence>

      {/* 1. 分屏切换 */}
      <Modal show={showGridModal} title="分屏切换" onClose={() => setShowGridModal(false)}>
        <div className="grid grid-cols-2 gap-4">
          {(['1x1', '2x2', '3x3', '4x4'] as GridMode[]).map(mode => (
            <button key={mode} onClick={() => { setGridMode(mode); setShowGridModal(false); }}
              className={`p-4 rounded-lg border-2 transition-all ${gridMode === mode ? 'border-blue-500 bg-blue-500/20' : 'border-white/10 bg-white/5 hover:bg-white/10'}`}>
              <div className="flex flex-col items-center gap-2">
                <Grid3x3 className="w-8 h-8 text-white" />
                <span className="text-white font-medium">{mode}</span>
                <span className="text-[10px] text-gray-400">最多 {GRID_COUNT[mode]} 路</span>
                {gridMode === mode && <Check className="w-4 h-4 text-blue-400" />}
              </div>
            </button>
          ))}
        </div>
      </Modal>

      {/* 2. 画面控制（播放/静音/云台，真实作用） */}
      <Modal show={showControlModal} title="画面控制" onClose={() => setShowControlModal(false)}>
        <div className="space-y-4">
          <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
            <span className="text-white text-sm">播放/暂停（全部画面）</span>
            <button onClick={() => setIsPlaying(v => !v)} className="p-2 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded-lg">
              {isPlaying ? <Pause className="w-5 h-5 text-blue-300" /> : <Play className="w-5 h-5 text-blue-300" />}
            </button>
          </div>
          <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
            <span className="text-white text-sm">静音控制（全部画面）</span>
            <button onClick={() => setIsMuted(v => !v)} className="p-2 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded-lg">
              {isMuted ? <VolumeX className="w-5 h-5 text-blue-300" /> : <Volume2 className="w-5 h-5 text-blue-300" />}
            </button>
          </div>
          <div className="p-4 bg-white/5 rounded-lg">
            <p className="text-sm text-white mb-3">云台控制（第一路设备）</p>
            <div className="grid grid-cols-3 gap-2 max-w-[180px] mx-auto">
              <div />
              <PtzButton icon={<ArrowUp className="w-4 h-4" />} onClick={() => handlePtz(0)} busy={actionBusy} />
              <div />
              <PtzButton icon={<ArrowLeft className="w-4 h-4" />} onClick={() => handlePtz(2)} busy={actionBusy} />
              <div className="flex items-center justify-center text-[10px] text-gray-500">PTZ</div>
              <PtzButton icon={<ArrowRight className="w-4 h-4" />} onClick={() => handlePtz(3)} busy={actionBusy} />
              <div />
              <PtzButton icon={<ArrowDown className="w-4 h-4" />} onClick={() => handlePtz(1)} busy={actionBusy} />
              <div />
            </div>
            <p className="text-[10px] text-gray-500 mt-2 text-center">方向：0上 1下 2左 3右（萤石文档）</p>
          </div>
          <p className="text-xs text-gray-400">当前状态：{isPlaying ? '播放中' : '已暂停'} / {isMuted ? '静音' : '有声音'}</p>
        </div>
      </Modal>

      {/* 3. 截图取证（device/capture 真实 picUrl） */}
      <Modal show={showScreenshotModal} title="截图取证" onClose={() => setShowScreenshotModal(false)} wide>
        <div className="space-y-4">
          <p className="text-sm text-gray-400">选择要抓拍的萤石通道（可多选）</p>
          <div className="grid grid-cols-2 gap-3 max-h-72 overflow-y-auto custom-scrollbar">
            {displayCameras.map(dev => (
              <div key={dev.deviceSerial}
                onClick={() => setSelectedCameras(prev => prev.includes(dev.deviceSerial) ? prev.filter(x => x !== dev.deviceSerial) : [...prev, dev.deviceSerial])}
                className={`relative p-2 rounded-lg cursor-pointer transition-all ${selectedCameras.includes(dev.deviceSerial) ? 'bg-blue-500/20 border-2 border-blue-500' : 'bg-white/5 border-2 border-white/10 hover:bg-white/10'}`}>
                <div className="w-full aspect-video bg-black/60 rounded mb-2 flex items-center justify-center">
                  <CameraIcon className="w-6 h-6" style={{ color: '#3a5a70' }} />
                </div>
                <p className="text-xs text-white truncate">{dev.deviceName || dev.deviceSerial}</p>
                <p className="text-[10px] text-gray-500 font-mono truncate">{dev.deviceSerial}</p>
                {selectedCameras.includes(dev.deviceSerial) && (
                  <div className="absolute top-4 right-4 w-6 h-6 bg-blue-500 rounded-full flex items-center justify-center"><Check className="w-4 h-4 text-white" /></div>
                )}
              </div>
            ))}
            {displayCameras.length === 0 && <p className="col-span-2 text-xs text-gray-500 text-center py-6">暂无可抓拍设备</p>}
          </div>
          {captureResults && (
            <div className="space-y-2">
              <p className="text-xs text-green-300">抓拍完成，共 {captureResults.length} 张（萤石 picUrl，有效期有限请尽快保存）</p>
              <div className="grid grid-cols-3 gap-2 max-h-48 overflow-y-auto">
                {captureResults.map((c, i) => (
                  <a key={i} href={c.picUrl} target="_blank" rel="noreferrer" className="block">
                    <img src={c.picUrl} alt={c.name} className="w-full aspect-video object-cover rounded" />
                    <p className="text-[10px] text-gray-400 truncate">{c.name}</p>
                  </a>
                ))}
              </div>
            </div>
          )}
          <div className="flex gap-3 pt-2">
            <button onClick={() => { setShowScreenshotModal(false); setSelectedCameras([]); setCaptureResults(null); }}
              className="flex-1 px-4 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg text-white text-sm">关闭</button>
            <button onClick={handleCapture} disabled={selectedCameras.length === 0 || actionBusy}
              className="flex-1 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 rounded-lg font-semibold text-white text-sm disabled:opacity-50 flex items-center justify-center gap-2">
              <Download className="w-4 h-4" />{actionBusy ? '抓拍中…' : `抓拍 (${selectedCameras.length})`}
            </button>
          </div>
        </div>
      </Modal>

      {/* 4. 回放查询（video/by/time 真实） */}
      <Modal show={showPlaybackModal} title="回放查询" onClose={() => setShowPlaybackModal(false)}>
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">选择日期</label>
            <div className="relative">
              <input type="date" value={playbackDate} onChange={e => setPlaybackDate(e.target.value)} className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white text-sm" />
              <Calendar className="absolute right-3 top-2.5 w-4 h-4 text-gray-400 pointer-events-none" />
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">起始时间</label>
            <div className="relative">
              <input type="time" value={playbackTime} onChange={e => setPlaybackTime(e.target.value)} className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white text-sm" />
              <Clock className="absolute right-3 top-2.5 w-4 h-4 text-gray-400 pointer-events-none" />
            </div>
            <p className="text-[10px] text-gray-500 mt-1">查询起始后 1 小时内的录像片段</p>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">选择设备</label>
            <select value={playbackDevice} onChange={e => setPlaybackDevice(e.target.value)} className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white text-sm [&>option]:bg-gray-800">
              <option value="">第一路（默认）</option>
              {displayCameras.map(d => <option key={d.deviceSerial} value={d.deviceSerial}>{d.deviceName || d.deviceSerial}</option>)}
            </select>
          </div>
          {playbackRecords && (
            <div className="max-h-48 overflow-y-auto custom-scrollbar space-y-2">
              <p className="text-xs text-green-300">查询到 {playbackRecords.length} 段录像</p>
              {playbackRecords.map((rec, i) => (
                <div key={i} className="bg-white/5 rounded-lg p-2 flex items-center gap-2">
                  {rec.coverPic && <img src={rec.coverPic} alt="cover" className="w-16 h-10 object-cover rounded flex-shrink-0" />}
                  <div className="min-w-0 flex-1">
                    <p className="text-xs text-white truncate">{rec.fileName || `录像片段 ${i + 1}`}</p>
                    <p className="text-[10px] text-gray-500">
                      {rec.startTime ? new Date(rec.startTime).toLocaleString('zh-CN') : '—'} ~ {rec.endTime ? new Date(rec.endTime).toLocaleTimeString('zh-CN') : '—'}
                    </p>
                  </div>
                  {rec.downloadPath && (
                    <a href={rec.downloadPath} target="_blank" rel="noreferrer" className="p-1.5 bg-blue-500/20 rounded text-blue-300 flex-shrink-0"><Download className="w-3 h-3" /></a>
                  )}
                </div>
              ))}
              {playbackRecords.length === 0 && <p className="text-xs text-gray-500 text-center py-3">该时段暂无录像</p>}
            </div>
          )}
          <div className="flex gap-3 pt-2">
            <button onClick={() => setShowPlaybackModal(false)} className="flex-1 px-4 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg text-white text-sm">关闭</button>
            <button onClick={handlePlayback} disabled={actionBusy} className="flex-1 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 rounded-lg font-semibold text-white text-sm flex items-center justify-center gap-2">
              <Search className="w-4 h-4" />{actionBusy ? '查询中…' : '查询'}
            </button>
          </div>
        </div>
      </Modal>

      {/* 5. 系统设置（展示真实配置状态，不伪造开关） */}
      <Modal show={showSettingsModal} title="系统设置" onClose={() => setShowSettingsModal(false)}>
        <div className="space-y-3">
          <SettingRow label="接入状态" value={configured ? '已配置萤石凭证' : '未配置凭证'} />
          <SettingRow label="网关地址" value={configStatus?.baseUrl || '—'} />
          <SettingRow label="直播协议" value={configStatus?.liveProtocol === 2 ? 'HLS (m3u8) / hls.js' : `protocol=${configStatus?.liveProtocol ?? '—'}`} />
          <SettingRow label="设备数量" value={`${devices.length} 台（在线 ${onlineCount} / 离线 ${offlineCount}）`} />
          <SettingRow label="凭证管理" value="appsettings.json → Ezviz，或环境变量 Ezviz__AppKey / Ezviz__AppSecret" />
          <div className="p-3 rounded-lg text-[11px]" style={{ background: 'rgba(0,195,255,0.06)', color: '#8ecfff', border: '1px solid rgba(0,195,255,0.2)' }}>
            自动录像 / 布撤防 / 运动检测等属萤石设备侧能力（见开放 API 文档 defence / fullday record 等接口），
            当前未纳入本期范围，故不提供虚假开关。如需接入请提出具体接口。
          </div>
        </div>
      </Modal>

      {/* 6. 添加/绑定摄像头（真实：萤石设备 → 本地区域） */}
      <Modal show={showAddCameraModal} title="添加摄像头 / 绑定区域" onClose={() => setShowAddCameraModal(false)}>
        <div className="space-y-4">
          <p className="text-xs text-gray-400">萤石设备来自开放平台账号，此处把设备绑定到本地「区域导航」以便按区域筛选。</p>
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">萤石设备</label>
            <select value={bindForm.deviceSerial} onChange={e => setBindForm({ ...bindForm, deviceSerial: e.target.value, name: bindableDevices.find(d => d.deviceSerial === e.target.value)?.deviceName || '' })}
              className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white text-sm [&>option]:bg-gray-800">
              <option value="">请选择设备</option>
              {bindableDevices.map(d => <option key={d.deviceSerial} value={d.deviceSerial}>{d.deviceName || d.deviceSerial} ({d.deviceSerial})</option>)}
            </select>
            {bindableDevices.length === 0 && <p className="text-[10px] text-gray-500 mt-1">未加载到设备，请确认萤石已配置且账号下有设备</p>}
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">显示名称</label>
            <input type="text" value={bindForm.name} onChange={e => setBindForm({ ...bindForm, name: e.target.value })}
              placeholder="默认使用萤石设备名" className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white text-sm" />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">所属区域</label>
            <select value={bindForm.areaId} onChange={e => setBindForm({ ...bindForm, areaId: e.target.value })}
              className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white text-sm [&>option]:bg-gray-800">
              <option value="">不绑定（全局）</option>
              {flattenAreas(areas).map(a => <option key={a.id} value={a.id}>{a.name}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">设备验证码 (ewcode，可选)</label>
            <input type="text" value={bindForm.ewcode} onChange={e => setBindForm({ ...bindForm, ewcode: e.target.value })}
              placeholder="设备开启视频加密时必填，可在萤石APP-设备-安全中查看" className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white text-sm" />
            <p className="text-[10px] text-gray-500 mt-1">未加密设备可留空；加密设备不填将无法取流（报错 60019）</p>
          </div>
          <div className="flex gap-3 pt-2">
            <button onClick={() => setShowAddCameraModal(false)} className="flex-1 px-4 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg text-white text-sm">取消</button>
            <button onClick={handleBind} disabled={actionBusy} className="flex-1 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 rounded-lg font-semibold text-white text-sm flex items-center justify-center gap-2">
              <Plus className="w-4 h-4" />{actionBusy ? '处理中…' : '绑定'}
            </button>
          </div>
        </div>
      </Modal>

      <style>{`
        .custom-scrollbar::-webkit-scrollbar { width: 4px; height: 4px; }
        .custom-scrollbar::-webkit-scrollbar-track { background: rgba(255,255,255,0.05); border-radius: 2px; }
        .custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(59,130,246,0.5); border-radius: 2px; }
      `}</style>
    </div>
  );
}

// ── 辅助组件 ──────────────────────────────────────────────────────────────────

function filterDevices(
  devices: EzvizDevice[], selectedArea: string | null,
  areas: any[], flattenAreas: (a: any[]) => any[]
): EzvizDevice[] {
  if (!selectedArea) return devices;
  const flat = flattenAreas(areas);
  const area = flat.find((a: any) => String(a.id) === String(selectedArea));
  if (!area) return devices.filter(d => String(d.areaId) === String(selectedArea));
  const ids = new Set<string>([String(selectedArea)]);
  const collect = (children: any[]) => children.forEach((c: any) => {
    ids.add(String(c.id));
    if (c.children) collect(c.children);
  });
  if (area.children) collect(area.children);
  return devices.filter(d => d.areaId != null && ids.has(String(d.areaId)));
}

function ToolButton({ icon, label, onClick, disabled }: { icon: React.ReactNode; label: string; onClick: () => void; disabled?: boolean }) {
  return (
    <button onClick={onClick} disabled={disabled}
      className="flex items-center gap-1.5 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors disabled:opacity-40 disabled:cursor-not-allowed">
      {icon}<span>{label}</span>
    </button>
  );
}

function PtzButton({ icon, onClick, busy }: { icon: React.ReactNode; onClick: () => void; busy: boolean }) {
  return (
    <button onClick={onClick} disabled={busy}
      className="p-2 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-blue-300 disabled:opacity-40 flex items-center justify-center">
      {icon}
    </button>
  );
}

function SettingRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="p-3 bg-white/5 rounded-lg flex items-start justify-between gap-3">
      <span className="text-sm text-white flex-shrink-0">{label}</span>
      <span className="text-xs text-gray-400 text-right break-all">{value}</span>
    </div>
  );
}

function Modal({ show, title, onClose, children, wide }: {
  show: boolean; title: string; onClose: () => void; children: React.ReactNode; wide?: boolean;
}) {
  return (
    <AnimatePresence>
      {show && (
        <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4" onClick={onClose}>
          <motion.div initial={{ scale: 0.9, y: 20 }} animate={{ scale: 1, y: 0 }} exit={{ scale: 0.9, y: 20 }}
            onClick={e => e.stopPropagation()}
            className={`bg-gradient-to-br from-[#1a1f35] to-[#0f1321] border border-blue-500/30 rounded-xl w-full p-6 ${wide ? 'max-w-2xl' : 'max-w-md'}`}>
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-xl font-bold text-white">{title}</h3>
              <button onClick={onClose} className="p-2 hover:bg-white/10 rounded-lg"><X className="w-5 h-5 text-gray-400" /></button>
            </div>
            {children}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
