import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  AlertTriangle, ClipboardCheck, Search, Download, RefreshCw,
  CheckCircle, XCircle, Clock, Eye, User, Cpu, Thermometer, Zap, MapPin, Bell, AlertCircle,
  Info, Play, CheckCircle2, Calendar, Building2,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import {
  alertApi,
  type AlertRecord,
  type AlertSummary,
} from '@/app/services/api/alertApi';

// ── 展示映射 ────────────────────────────────────────────────────────────────────

const levelInfo: Record<string, { label: string; color: string; bgColor: string; icon: JSX.Element }> = {
  info: { label: '信息', color: '#3b82f6', bgColor: 'rgba(59,130,246,0.15)', icon: <Info className="w-4 h-4" /> },
  warning: { label: '警告', color: '#f59e0b', bgColor: 'rgba(245,158,11,0.15)', icon: <AlertCircle className="w-4 h-4" /> },
  critical: { label: '严重', color: '#ef4444', bgColor: 'rgba(239,68,68,0.15)', icon: <AlertTriangle className="w-4 h-4" /> },
};

const statusInfo: Record<string, { label: string; color: string; icon: JSX.Element }> = {
  pending: { label: '待处理', color: '#f59e0b', icon: <Clock className="w-4 h-4" /> },
  processing: { label: '处理中', color: '#3b82f6', icon: <Play className="w-4 h-4" /> },
  resolved: { label: '已解决', color: '#10b981', icon: <CheckCircle className="w-4 h-4" /> },
  ignored: { label: '已忽略', color: '#6b7280', icon: <XCircle className="w-4 h-4" /> },
};

/** 告警类型 → 中文标签 + 图标（后端出网为枚举字符串，如 temperature/smoke/power） */
const alertTypeInfo: Record<string, { label: string; icon: JSX.Element }> = {
  temperature: { label: '温度异常', icon: <Thermometer className="w-4 h-4" /> },
  humidity: { label: '湿度异常', icon: <Thermometer className="w-4 h-4" /> },
  pm25: { label: 'PM2.5异常', icon: <AlertTriangle className="w-4 h-4" /> },
  co2: { label: 'CO₂异常', icon: <AlertTriangle className="w-4 h-4" /> },
  co: { label: '一氧化碳异常', icon: <AlertTriangle className="w-4 h-4" /> },
  smoke: { label: '烟雾异常', icon: <AlertTriangle className="w-4 h-4" /> },
  power: { label: '功率异常', icon: <Zap className="w-4 h-4" /> },
  pressure: { label: '压力异常', icon: <Zap className="w-4 h-4" /> },
  offline: { label: '设备离线', icon: <Cpu className="w-4 h-4" /> },
  door: { label: '门体异常', icon: <Cpu className="w-4 h-4" /> },
  authorization_expiring: { label: '授权即将过期', icon: <Clock className="w-4 h-4" /> },
  authorization_expired: { label: '授权已过期', icon: <AlertTriangle className="w-4 h-4" /> },
};

function typeLabel(t: string) { return alertTypeInfo[t]?.label ?? t; }
function typeIcon(t: string) { return alertTypeInfo[t]?.icon ?? <Bell className="w-4 h-4" />; }
function lv(k: string) { return levelInfo[k] ?? levelInfo.info; }
function st(k: string) { return statusInfo[k] ?? statusInfo.pending; }

/** 处理进度由真实 status 派生（无假工单流） */
function progressFromStatus(status: string): number {
  switch (status) {
    case 'resolved': case 'ignored': return 100;
    case 'processing': return 60;
    default: return 20;
  }
}

function exportCsv(rows: AlertRecord[]) {
  const header = ['告警编号', '设备名称', '区域', '告警类型', '级别', '状态', '触发时间', '租户', '当前值', '阈值', '备注'];
  const lines = rows.map(r => [
    r.alertNo, r.deviceName, r.area ?? '', typeLabel(r.alertType), lv(r.level).label, st(r.status).label,
    r.alertTime, r.appCode ?? '', r.value ?? '', r.threshold ?? '', r.remark ?? '',
  ].map(v => `"${String(v).replace(/"/g, '""')}"`).join(','));
  const blob = new Blob(['\uFEFF' + [header.join(','), ...lines].join('\n')], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = `alerts-${new Date().toISOString().slice(0, 10)}.csv`;
  a.click(); URL.revokeObjectURL(url);
}

const inputStyle: React.CSSProperties = {
  background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0,195,255,0.2)', color: '#c0d8e8',
};

// ── 设备预警记录（真实接口） ────────────────────────────────────────────────────
function AlertRecordsView() {
  const { user } = useAuth();
  const isSuperAdmin = user?.role === 'super_admin';

  const [records, setRecords] = useState<AlertRecord[]>([]);
  const [summary, setSummary] = useState<AlertSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMsg, setActionMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [search, setSearch] = useState('');
  const [levelFilter, setLevelFilter] = useState<string>('all');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [selectedRecord, setSelectedRecord] = useState<AlertRecord | null>(null);

  const load = useCallback(async () => {
    try {
      const [lr, sr] = await Promise.all([
        alertApi.getAlerts({ page: 1, pageSize: 100 }),
        alertApi.getSummary(),
      ]);
      if (lr.code === 200 && lr.data) { setRecords(lr.data.items || []); setError(null); }
      else setError(lr.message || '告警列表加载失败');
      if (sr.code === 200 && sr.data) setSummary(sr.data);
    } catch {
      setError('无权限或网络错误');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    const wrapped = async () => { if (!cancelled) await load(); };
    wrapped();
    const timer = setInterval(() => { if (!cancelled) load(); }, 30000);
    return () => { cancelled = true; clearInterval(timer); };
  }, [load]);

  const filtered = records.filter(r => {
    const matchSearch =
      (r.deviceName || '').toLowerCase().includes(search.toLowerCase()) ||
      typeLabel(r.alertType).toLowerCase().includes(search.toLowerCase()) ||
      (r.alertNo || '').toLowerCase().includes(search.toLowerCase());
    const matchLevel = levelFilter === 'all' || r.level === levelFilter;
    const matchStatus = statusFilter === 'all' || r.status === statusFilter;
    return matchSearch && matchLevel && matchStatus;
  });

  const stats = {
    total: summary?.totalAlerts ?? records.length,
    pending: summary?.pendingAlerts ?? records.filter(r => r.status === 'pending').length,
    processing: summary?.processingAlerts ?? records.filter(r => r.status === 'processing').length,
    resolved: summary?.resolvedAlerts ?? records.filter(r => r.status === 'resolved').length,
    ignored: summary?.ignoredAlerts ?? records.filter(r => r.status === 'ignored').length,
  };

  const doAction = async (r: AlertRecord, action: 'resolve' | 'ignore') => {
    setBusy(true); setActionMsg(null);
    try {
      const res = action === 'resolve'
        ? await alertApi.resolveAlert(r.id, '在告警中心标记为已解决')
        : await alertApi.ignoreAlert(r.id, '在告警中心标记为已忽略');
      if (res.code === 200) {
        setActionMsg(res.message || '操作成功');
        setSelectedRecord(null);
        await load();
      } else setActionMsg(res.message || '操作失败');
    } catch { setActionMsg('操作请求失败'); }
    setBusy(false);
  };

  return (
    <div className="space-y-4">
      {/* 租户范围提示 */}
      <div className="flex items-center gap-2 px-3 py-2 rounded-lg text-xs"
        style={{ background: 'rgba(0,195,255,0.06)', border: '1px solid rgba(0,195,255,0.2)', color: '#8ecfff' }}>
        <Building2 className="w-3.5 h-3.5" />
        {isSuperAdmin
          ? '超级管理员视图：展示全部租户告警（列表含「租户」列）'
          : `当前租户视图：仅展示本租户设备的告警（${records[0]?.appCode || '按登录账号自动隔离'}）`}
      </div>

      {/* Stats */}
      <div className="grid grid-cols-4 gap-3">
        {[
          { label: '总告警数', value: stats.total, color: '#00c3ff', icon: <Bell className="w-5 h-5" /> },
          { label: '待处理', value: stats.pending, color: '#f59e0b', icon: <Clock className="w-5 h-5" /> },
          { label: '处理中', value: stats.processing, color: '#3b82f6', icon: <Play className="w-5 h-5" /> },
          { label: '已解决', value: stats.resolved, color: '#10b981', icon: <CheckCircle className="w-5 h-5" /> },
        ].map((s, i) => (
          <motion.div key={i} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.05 }}
            className="p-4 rounded-lg" style={{ background: 'rgba(255,255,255,0.02)', border: `1px solid ${s.color}30` }}>
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm" style={{ color: '#5a7a90' }}>{s.label}</span>
              <span style={{ color: s.color }}>{s.icon}</span>
            </div>
            <p className="text-2xl font-bold" style={{ color: s.color }}>{s.value}</p>
          </motion.div>
        ))}
      </div>

      {/* action msg */}
      <AnimatePresence>
        {actionMsg && (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            onClick={() => setActionMsg(null)}
            className="px-3 py-2 rounded-lg text-xs text-white cursor-pointer"
            style={{ background: 'rgba(0,20,50,0.9)', border: '1px solid rgba(0,195,255,0.4)' }}>{actionMsg}</motion.div>
        )}
      </AnimatePresence>

      {/* Filters */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="flex-1 min-w-[200px] relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: '#3a6e9a' }} />
          <input type="text" placeholder="搜索设备 / 告警类型 / 编号..."
            value={search} onChange={e => setSearch(e.target.value)}
            className="w-full pl-10 pr-3 py-2 rounded-lg text-sm" style={inputStyle} />
        </div>
        <select value={levelFilter} onChange={e => setLevelFilter(e.target.value)}
          className="px-3 py-2 rounded-lg text-sm [&>option]:bg-gray-800" style={inputStyle}>
          <option value="all">所有级别</option><option value="info">信息</option>
          <option value="warning">警告</option><option value="critical">严重</option>
        </select>
        <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}
          className="px-3 py-2 rounded-lg text-sm [&>option]:bg-gray-800" style={inputStyle}>
          <option value="all">所有状态</option><option value="pending">待处理</option>
          <option value="processing">处理中</option><option value="resolved">已解决</option>
          <option value="ignored">已忽略</option>
        </select>
        <button onClick={() => exportCsv(filtered)} disabled={filtered.length === 0}
          className="px-4 py-2 rounded-lg flex items-center gap-2 text-sm hover:brightness-110 transition-all disabled:opacity-40"
          style={{ background: 'linear-gradient(90deg, rgba(0,195,255,0.15), rgba(0,195,255,0.08))', border: '1px solid rgba(0,195,255,0.3)', color: '#00c3ff' }}>
          <Download className="w-4 h-4" />导出
        </button>
        <button onClick={load} disabled={loading} className="p-2 rounded-lg hover:brightness-110 disabled:opacity-40"
          style={{ color: '#00c3ff', border: '1px solid rgba(0,195,255,0.2)' }}>
          <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
        </button>
      </div>

      {error && <div className="text-xs py-2" style={{ color: '#ffaa00' }}>{error}</div>}

      {/* Table */}
      <div className="rounded-xl overflow-x-auto" style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(0,195,255,0.15)' }}>
        <table className="w-full">
          <thead>
            <tr style={{ background: 'rgba(0,195,255,0.08)', borderBottom: '1px solid rgba(0,195,255,0.2)' }}>
              {['告警编号', '设备名称', '区域', '告警类型', '级别', '状态', '触发时间', ...(isSuperAdmin ? ['租户'] : []), '操作'].map(h => (
                <th key={h} className="px-4 py-3 text-left text-xs font-medium whitespace-nowrap" style={{ color: '#5a7a90' }}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={isSuperAdmin ? 9 : 8} className="px-4 py-10 text-center text-xs text-gray-500">加载告警…</td></tr>
            ) : filtered.length === 0 ? (
              <tr><td colSpan={isSuperAdmin ? 9 : 8} className="px-4 py-10 text-center text-xs text-gray-500">
                {records.length === 0 ? '当前租户暂无告警' : '没有匹配的告警'}
              </td></tr>
            ) : filtered.map((rec, idx) => (
              <motion.tr key={rec.id} initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ delay: idx * 0.02 }}
                className="hover:brightness-110 transition-all cursor-pointer"
                style={{ borderBottom: '1px solid rgba(255,255,255,0.05)' }}
                onClick={() => setSelectedRecord(rec)}>
                <td className="px-4 py-3"><span className="text-xs font-mono" style={{ color: '#00c3ff' }}>{rec.alertNo}</span></td>
                <td className="px-4 py-3"><div className="flex items-center gap-2"><Cpu className="w-4 h-4" style={{ color: '#3a6e9a' }} /><span className="text-sm whitespace-nowrap" style={{ color: '#c0d8e8' }}>{rec.deviceName}</span></div></td>
                <td className="px-4 py-3"><div className="flex items-center gap-1"><MapPin className="w-3 h-3" style={{ color: '#3a6e9a' }} /><span className="text-xs whitespace-nowrap" style={{ color: '#5a7a90' }}>{rec.area || '—'}</span></div></td>
                <td className="px-4 py-3"><div className="flex items-center gap-2" style={{ color: '#c0d8e8' }}>{typeIcon(rec.alertType)}<span className="text-sm whitespace-nowrap">{typeLabel(rec.alertType)}</span></div></td>
                <td className="px-4 py-3"><div className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium whitespace-nowrap" style={{ background: lv(rec.level).bgColor, color: lv(rec.level).color }}>{lv(rec.level).icon}{lv(rec.level).label}</div></td>
                <td className="px-4 py-3"><div className="inline-flex items-center gap-1 text-xs whitespace-nowrap" style={{ color: st(rec.status).color }}>{st(rec.status).icon}{st(rec.status).label}</div></td>
                <td className="px-4 py-3 text-xs whitespace-nowrap" style={{ color: '#5a7a90' }}>{rec.alertTime}</td>
                {isSuperAdmin && <td className="px-4 py-3 text-xs font-mono whitespace-nowrap" style={{ color: '#8ecfff' }}>{rec.appCode || '—'}</td>}
                <td className="px-4 py-3"><button className="p-1 hover:brightness-125 transition-all" style={{ color: '#00c3ff' }}><Eye className="w-4 h-4" /></button></td>
              </motion.tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Detail Modal */}
      <AnimatePresence>
        {selectedRecord && (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4" onClick={() => setSelectedRecord(null)}>
            <motion.div initial={{ scale: 0.9, y: 20 }} animate={{ scale: 1, y: 0 }} exit={{ scale: 0.9, y: 20 }}
              onClick={e => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-2xl w-full p-6">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-xl font-bold text-white">告警详情</h3>
                <button onClick={() => setSelectedRecord(null)} className="p-1 hover:bg-white/10 rounded"><XCircle className="w-5 h-5 text-gray-400" /></button>
              </div>
              <div className="space-y-4 text-sm">
                <div className="grid grid-cols-2 gap-4">
                  <div><p className="text-gray-400 mb-1">告警编号</p><p className="text-white font-mono text-xs">{selectedRecord.alertNo}</p></div>
                  <div><p className="text-gray-400 mb-1">触发时间</p><p className="text-white">{selectedRecord.alertTime}</p></div>
                  <div><p className="text-gray-400 mb-1">设备名称</p><p className="text-white">{selectedRecord.deviceName}</p></div>
                  <div><p className="text-gray-400 mb-1">所属区域</p><p className="text-white">{selectedRecord.area || '—'}</p></div>
                  <div><p className="text-gray-400 mb-1">告警类型</p><p className="text-white">{typeLabel(selectedRecord.alertType)}</p></div>
                  <div><p className="text-gray-400 mb-1">告警级别</p>
                    <span className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium" style={{ background: lv(selectedRecord.level).bgColor, color: lv(selectedRecord.level).color }}>{lv(selectedRecord.level).icon}{lv(selectedRecord.level).label}</span>
                  </div>
                  <div><p className="text-gray-400 mb-1">当前状态</p><p className="text-white">{st(selectedRecord.status).label}</p></div>
                  <div><p className="text-gray-400 mb-1">所属租户</p><p className="text-white font-mono text-xs">{selectedRecord.appCode || '—'}</p></div>
                  {selectedRecord.value != null && (
                    <div>
                      <p className="text-gray-400 mb-1">当前数值</p>
                      <p className="text-white">
                        {selectedRecord.value}
                        {selectedRecord.threshold != null ? `（阈值 ${selectedRecord.threshold}）` : ''}
                      </p>
                    </div>
                  )}
                  {selectedRecord.assignee && <div><p className="text-gray-400 mb-1">处理人</p><p className="text-white">{selectedRecord.assignee}</p></div>}
                </div>
                {selectedRecord.remark && <div><p className="text-gray-400 mb-1">告警描述</p><p className="text-white">{selectedRecord.remark}</p></div>}
                <div className="flex gap-3 pt-2">
                  {(selectedRecord.status === 'pending' || selectedRecord.status === 'processing') && (
                    <>
                      <button onClick={() => doAction(selectedRecord, 'resolve')} disabled={busy}
                        className="flex-1 px-4 py-2 rounded-lg text-sm text-white disabled:opacity-50"
                        style={{ background: 'rgba(16,185,129,0.25)', border: '1px solid rgba(16,185,129,0.5)' }}>标记已解决</button>
                      <button onClick={() => doAction(selectedRecord, 'ignore')} disabled={busy}
                        className="flex-1 px-4 py-2 rounded-lg text-sm text-white disabled:opacity-50"
                        style={{ background: 'rgba(107,114,128,0.25)', border: '1px solid rgba(107,114,128,0.5)' }}>忽略</button>
                    </>
                  )}
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ── 处理进度（由真实告警状态派生） ──────────────────────────────────────────────
function ProcessProgressView() {
  const [records, setRecords] = useState<AlertRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<AlertRecord | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>('all');

  const load = useCallback(async () => {
    try {
      const r = await alertApi.getAlerts({ page: 1, pageSize: 100 });
      if (r.code === 200 && r.data) { setRecords(r.data.items || []); setError(null); }
      else setError(r.message || '加载失败');
    } catch { setError('无权限或网络错误'); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const filtered = records.filter(p => statusFilter === 'all' || p.status === statusFilter);

  const stepsFor = (r: AlertRecord) => {
    const done = r.status === 'resolved' || r.status === 'ignored';
    const processing = r.status === 'processing';
    return [
      { id: 's1', name: '告警触发', status: 'completed' as const, time: r.alertTime, remark: r.remark || `${typeLabel(r.alertType)}：${r.value ?? '—'}${r.threshold != null ? ` / 阈值 ${r.threshold}` : ''}` },
      { id: 's2', name: '告警确认', status: (processing || done ? 'completed' : 'current') as 'completed' | 'current', time: r.updatedAt, operator: r.assignee || undefined },
      { id: 's3', name: '处理完成', status: (done ? 'completed' : 'pending') as 'completed' | 'pending', time: r.resolveTime || undefined, operator: r.assignee || undefined },
    ];
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)} className="px-3 py-2 rounded-lg text-sm [&>option]:bg-gray-800" style={inputStyle}>
          <option value="all">所有状态</option><option value="pending">待处理</option>
          <option value="processing">处理中</option><option value="resolved">已解决</option><option value="ignored">已忽略</option>
        </select>
        <div className="flex-1" />
        <button onClick={load} className="p-2 hover:brightness-110 rounded-lg" style={{ color: '#00c3ff' }}><RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} /></button>
      </div>

      {error && <div className="text-xs py-2" style={{ color: '#ffaa00' }}>{error}</div>}

      <div className="grid grid-cols-3 gap-4">
        {/* List */}
        <div className="col-span-1 space-y-2 max-h-[600px] overflow-y-auto pr-2">
          {loading ? <p className="text-xs text-gray-500 text-center py-6">加载中…</p>
            : filtered.length === 0 ? <p className="text-xs text-gray-500 text-center py-6">暂无告警记录</p>
              : filtered.map(p => (
                <motion.div key={p.id} whileHover={{ scale: 1.02 }} onClick={() => setSelected(p)}
                  className="p-4 rounded-lg cursor-pointer transition-all"
                  style={{ background: selected?.id === p.id ? 'rgba(0,195,255,0.15)' : 'rgba(255,255,255,0.02)', border: `1px solid ${selected?.id === p.id ? 'rgba(0,195,255,0.4)' : 'rgba(255,255,255,0.05)'}` }}>
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-xs font-mono truncate" style={{ color: '#00c3ff' }}>{p.alertNo}</span>
                    <div className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs" style={{ background: `${lv(p.level).color}20`, color: lv(p.level).color }}>{lv(p.level).label}</div>
                  </div>
                  <p className="text-sm font-medium mb-1" style={{ color: '#c0d8e8' }}>{p.deviceName}</p>
                  <div className="flex items-center gap-2 text-xs mb-2" style={{ color: '#5a7a90' }}><MapPin className="w-3 h-3" /><span>{p.area || '—'}</span></div>
                  <div className="flex items-center gap-2">
                    <div className="flex-1 h-1.5 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.1)' }}>
                      <div className="h-full rounded-full transition-all" style={{ width: `${progressFromStatus(p.status)}%`, background: st(p.status).color }} />
                    </div>
                    <span className="text-xs font-medium" style={{ color: st(p.status).color }}>{progressFromStatus(p.status)}%</span>
                  </div>
                </motion.div>
              ))}
        </div>

        {/* Detail */}
        <div className="col-span-2">
          {selected ? (
            <motion.div key={selected.id} initial={{ opacity: 0, x: 10 }} animate={{ opacity: 1, x: 0 }}
              className="p-6 rounded-xl" style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(0,195,255,0.15)' }}>
              <div className="flex items-start justify-between mb-6">
                <div>
                  <div className="flex items-center gap-3 mb-2">
                    <h3 className="text-xl font-bold" style={{ color: '#c0d8e8' }}>{selected.deviceName}</h3>
                    <div className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium" style={{ background: `${lv(selected.level).color}20`, color: lv(selected.level).color }}>{lv(selected.level).label}</div>
                  </div>
                  <div className="flex items-center gap-4 text-sm" style={{ color: '#5a7a90' }}>
                    <span className="flex items-center gap-1"><MapPin className="w-3 h-3" />{selected.area || '—'}</span>
                    <span>{typeLabel(selected.alertType)}</span>
                  </div>
                </div>
                <div className="text-right"><p className="text-xs mb-1" style={{ color: '#5a7a90' }}>告警编号</p><p className="text-sm font-mono" style={{ color: '#00c3ff' }}>{selected.alertNo}</p></div>
              </div>

              <div className="mb-6">
                <div className="flex items-center justify-between mb-2">
                  <span className="text-sm font-medium" style={{ color: '#c0d8e8' }}>处理进度</span>
                  <span className="text-sm font-bold" style={{ color: st(selected.status).color }}>{progressFromStatus(selected.status)}%</span>
                </div>
                <div className="h-2 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.1)' }}>
                  <div className="h-full rounded-full transition-all" style={{ width: `${progressFromStatus(selected.status)}%`, background: st(selected.status).color }} />
                </div>
              </div>

              <div className="grid grid-cols-3 gap-4 mb-6 p-4 rounded-lg" style={{ background: 'rgba(255,255,255,0.03)' }}>
                <div><p className="text-xs mb-1" style={{ color: '#5a7a90' }}>上报时间</p><p className="text-sm font-medium" style={{ color: '#c0d8e8' }}>{selected.alertTime}</p></div>
                <div><p className="text-xs mb-1" style={{ color: '#5a7a90' }}>当前状态</p><p className="text-sm font-medium" style={{ color: st(selected.status).color }}>{st(selected.status).label}</p></div>
                <div><p className="text-xs mb-1" style={{ color: '#5a7a90' }}>处理人员</p><p className="text-sm font-medium" style={{ color: '#c0d8e8' }}>{selected.assignee || '—'}</p></div>
              </div>

              <div>
                <h4 className="text-sm font-medium mb-4" style={{ color: '#c0d8e8' }}>处理流程</h4>
                <div className="space-y-3">
                  {stepsFor(selected).map((step, idx, arr) => {
                    const isCompleted = step.status === 'completed';
                    const isCurrent = step.status === 'current';
                    const circleCls = 'w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold transition-all ' + (
                      isCompleted ? 'bg-green-500/20 text-green-400 border-2 border-green-500'
                        : isCurrent ? 'bg-blue-500/20 text-blue-400 border-2 border-blue-500'
                          : 'bg-gray-500/10 text-gray-500 border-2 border-gray-600');
                    return (
                      <div key={step.id} className="flex gap-3">
                        <div className="flex flex-col items-center">
                          <div className={circleCls}>
                            {isCompleted ? <CheckCircle2 className="w-4 h-4" /> : idx + 1}
                          </div>
                          {idx < arr.length - 1 && <div className={`w-0.5 h-8 ${isCompleted ? 'bg-green-500/30' : 'bg-gray-600/30'}`} />}
                        </div>
                        <div className="flex-1 pb-3">
                          <p className={`text-sm font-medium mb-1 ${isCurrent ? 'text-blue-400' : isCompleted ? 'text-white' : 'text-gray-500'}`}>{step.name}</p>
                          {(step.time || step.operator) && (
                            <p className="text-xs flex items-center gap-2" style={{ color: '#5a7a90' }}>
                              <Calendar className="inline w-3 h-3 mr-1" />{step.time}
                              {step.operator && <><User className="inline w-3 h-3 mx-1" />{step.operator}</>}
                            </p>
                          )}
                          {step.remark && <p className="text-xs mt-1 px-2 py-1 rounded" style={{ background: 'rgba(255,255,255,0.03)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.06)' }}>{step.remark}</p>}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            </motion.div>
          ) : (
            <div className="flex items-center justify-center h-64 rounded-xl" style={{ border: '1px dashed rgba(0,195,255,0.2)' }}>
              <div className="text-center"><Info className="w-10 h-10 mx-auto mb-2" style={{ color: '#2a4a60' }} /><p style={{ color: '#3a6e9a' }}>请从左侧选择一条记录查看详情</p></div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Main Component ─────────────────────────────────────────────────────────────
export function AlertCenterPage() {
  const [activeTab, setActiveTab] = useState<'alerts' | 'progress'>('alerts');

  const tabs = [
    { id: 'alerts', label: '设备预警记录', icon: <AlertTriangle className="w-4 h-4" /> },
    { id: 'progress', label: '处理进度', icon: <ClipboardCheck className="w-4 h-4" /> },
  ];

  return (
    <div className="p-6 space-y-5 min-h-full" style={{ background: 'linear-gradient(180deg, rgba(2,14,37,0) 0%, rgba(1,11,29,0) 100%)' }}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl flex items-center justify-center" style={{ background: 'rgba(239, 68, 68, 0.15)', border: '1px solid rgba(239, 68, 68, 0.3)' }}>
            <AlertTriangle className="w-5 h-5" style={{ color: '#ef4444' }} />
          </div>
          <div>
            <h1 className="text-xl font-bold" style={{ color: '#c0d8e8' }}>告警中心</h1>
            <p className="text-sm" style={{ color: '#3a6e9a' }}>设备真实告警监控与处理进度跟踪（按租户隔离）</p>
          </div>
        </div>
      </div>

      <div className="flex items-center gap-2 p-1 rounded-lg" style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0,195,255,0.15)' }}>
        {tabs.map(tab => (
          <button key={tab.id} onClick={() => setActiveTab(tab.id as typeof activeTab)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg transition-all flex-1"
            style={activeTab === tab.id
              ? { background: 'linear-gradient(90deg, rgba(0,195,255,0.25), rgba(0,195,255,0.15))', color: '#00c3ff', border: '1px solid rgba(0,195,255,0.4)' }
              : { color: '#5a7a90' }}>
            {tab.icon}<span className="text-sm font-medium">{tab.label}</span>
          </button>
        ))}
      </div>

      <AnimatePresence mode="wait">
        <motion.div key={activeTab} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -10 }} transition={{ duration: 0.2 }}>
          {activeTab === 'alerts' ? <AlertRecordsView /> : <ProcessProgressView />}
        </motion.div>
      </AnimatePresence>
    </div>
  );
}
