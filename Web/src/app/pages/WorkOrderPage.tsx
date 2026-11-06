import { useState, useEffect, useCallback, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Plus, Search, Eye, CheckCircle, Clock, XCircle,
  User, Calendar, ChevronRight,
  Wrench, Zap, Thermometer, Cpu, X, Send,
  ClipboardList, RefreshCw, MapPin, Building2, FileText,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import {
  workOrderApi,
  type WorkOrder,
  type WorkOrderLog,
  type CreateWorkOrderRequest,
} from '@/app/services/api/workOrderApi';

// ── 类型 ──────────────────────────────────────────────────────────────────────
type WorkOrderStatus = 'pending' | 'assigned' | 'in_progress' | 'resolved' | 'closed' | 'rejected';
type WorkOrderPriority = 'low' | 'normal' | 'high' | 'urgent';
type WorkOrderType = 'fault' | 'maintenance' | 'inspection' | 'installation' | 'other';

const STAFF_LIST = ['张工', '李工', '王工', '赵工', '刘工', '陈工'];

function rgba(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgba(${r},${g},${b},${alpha})`;
}

const STATUS_CONFIG: Record<WorkOrderStatus, { label: string; color: string; bg: string; border: string; icon: React.ReactNode }> = {
  pending:     { label: '待分配', color: '#f59e0b', bg: 'rgba(245,158,11,0.15)', border: 'rgba(245,158,11,0.35)', icon: <Clock className="w-3 h-3" /> },
  assigned:    { label: '已分配', color: '#60a5fa', bg: 'rgba(96,165,250,0.15)', border: 'rgba(96,165,250,0.35)', icon: <User className="w-3 h-3" /> },
  in_progress: { label: '处理中', color: '#a78bfa', bg: 'rgba(167,139,250,0.15)', border: 'rgba(167,139,250,0.35)', icon: <RefreshCw className="w-3 h-3" /> },
  resolved:    { label: '已解决', color: '#34d399', bg: 'rgba(52,211,153,0.15)', border: 'rgba(52,211,153,0.35)', icon: <CheckCircle className="w-3 h-3" /> },
  closed:      { label: '已关闭', color: '#9ca3af', bg: 'rgba(156,163,175,0.15)', border: 'rgba(156,163,175,0.35)', icon: <XCircle className="w-3 h-3" /> },
  rejected:    { label: '已驳回', color: '#f87171', bg: 'rgba(248,113,113,0.15)', border: 'rgba(248,113,113,0.35)', icon: <XCircle className="w-3 h-3" /> },
};
function st(k: string) { return STATUS_CONFIG[k as WorkOrderStatus] ?? STATUS_CONFIG.pending; }

const PRIORITY_CONFIG: Record<WorkOrderPriority, { label: string; color: string; dot: string }> = {
  low:    { label: '低', color: '#9ca3af', dot: 'bg-gray-400' },
  normal: { label: '普通', color: '#60a5fa', dot: 'bg-blue-400' },
  high:   { label: '高', color: '#fb923c', dot: 'bg-orange-400' },
  urgent: { label: '紧急', color: '#f87171', dot: 'bg-red-400' },
};
function pr(k: string) { return PRIORITY_CONFIG[k as WorkOrderPriority] ?? PRIORITY_CONFIG.normal; }

const TYPE_CONFIG: Record<WorkOrderType, { label: string; icon: React.ReactNode; color: string }> = {
  fault:        { label: '故障维修', icon: <Wrench className="w-3.5 h-3.5" />, color: '#f87171' },
  maintenance:  { label: '设备维保', icon: <Zap className="w-3.5 h-3.5" />, color: '#fb923c' },
  inspection:   { label: '定期巡检', icon: <ClipboardList className="w-3.5 h-3.5" />, color: '#60a5fa' },
  installation: { label: '安装调试', icon: <Cpu className="w-3.5 h-3.5" />, color: '#a78bfa' },
  other:        { label: '其他', icon: <Thermometer className="w-3.5 h-3.5" />, color: '#9ca3af' },
};
function ty(k: string) { return TYPE_CONFIG[k as WorkOrderType] ?? TYPE_CONFIG.other; }

function StatusBadge({ status }: { status: string }) {
  const cfg = st(status);
  return (
    <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium"
      style={{ color: cfg.color, background: cfg.bg, border: `1px solid ${cfg.border}` }}>
      {cfg.icon}{cfg.label}
    </span>
  );
}
function PriorityDot({ priority }: { priority: string }) {
  const cfg = pr(priority);
  return (
    <span className="inline-flex items-center gap-1.5 text-xs" style={{ color: cfg.color }}>
      <span className={`w-2 h-2 rounded-full flex-shrink-0 ${cfg.dot} ${priority === 'urgent' ? 'animate-pulse' : ''}`} />
      {cfg.label}
    </span>
  );
}
function TypeBadge({ type }: { type: string }) {
  const cfg = ty(type);
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs"
      style={{ color: cfg.color, background: rgba(cfg.color, 0.1), border: `1px solid ${rgba(cfg.color, 0.25)}` }}>
      {cfg.icon}{cfg.label}
    </span>
  );
}

// ── Main Component ────────────────────────────────────────────────────────────
export function WorkOrderPage() {
  const { user, customers, currentCustomer } = useAuth();
  const isSuperAdmin = user?.role === 'super_admin';

  const [orders, setOrders] = useState<WorkOrder[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMsg, setActionMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [priorityFilter, setPriorityFilter] = useState<string>('all');
  const [selectedOrder, setSelectedOrder] = useState<WorkOrder | null>(null);
  const [orderLogs, setOrderLogs] = useState<WorkOrderLog[]>([]);
  const [logsLoading, setLogsLoading] = useState(false);
  const [showCreateModal, setShowCreateModal] = useState(false);

  const load = useCallback(async () => {
    try {
      const r = await workOrderApi.getWorkOrders({ page: 1, pageSize: 100 });
      if (r.code === 200 && r.data) { setOrders(r.data.items || []); setError(null); }
      else setError(r.message || '工单列表加载失败');
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

  // 详情：切换选中工单时拉处理记录
  useEffect(() => {
    if (!selectedOrder) { setOrderLogs([]); return; }
    let cancelled = false;
    (async () => {
      setLogsLoading(true);
      try {
        const r = await workOrderApi.getWorkOrderLogs(selectedOrder.id);
        if (!cancelled) setOrderLogs(r.code === 200 && r.data ? r.data : []);
      } catch { if (!cancelled) setOrderLogs([]); }
      finally { if (!cancelled) setLogsLoading(false); }
    })();
    return () => { cancelled = true; };
  }, [selectedOrder?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const filteredOrders = useMemo(() => {
    return orders.filter(o => {
      const matchSearch = searchTerm === '' ||
        (o.orderNo || '').toLowerCase().includes(searchTerm.toLowerCase()) ||
        (o.title || '').toLowerCase().includes(searchTerm.toLowerCase()) ||
        (o.deviceName || '').toLowerCase().includes(searchTerm.toLowerCase()) ||
        (o.areaName || '').toLowerCase().includes(searchTerm.toLowerCase());
      return matchSearch &&
        (statusFilter === 'all' || o.status === statusFilter) &&
        (typeFilter === 'all' || o.type === typeFilter) &&
        (priorityFilter === 'all' || o.priority === priorityFilter);
    });
  }, [orders, searchTerm, statusFilter, typeFilter, priorityFilter]);

  const stats = useMemo(() => ({
    total: orders.length,
    pending: orders.filter(o => o.status === 'pending').length,
    in_progress: orders.filter(o => o.status === 'in_progress').length,
    resolved: orders.filter(o => o.status === 'resolved').length,
  }), [orders]);

  const refreshSelected = async (id: number) => {
    const r = await workOrderApi.getWorkOrder(id);
    if (r.code === 200 && r.data) setSelectedOrder(r.data);
    await load();
  };

  const doAction = async (action: 'assign' | 'start' | 'resolve' | 'close' | 'reject', payload?: string) => {
    if (!selectedOrder) return;
    setBusy(true); setActionMsg(null);
    try {
      let r;
      switch (action) {
        case 'assign': r = await workOrderApi.assignWorkOrder(selectedOrder.id, payload || user?.username || '系统'); break;
        case 'start': r = await workOrderApi.startWorkOrder(selectedOrder.id); break;
        case 'resolve': r = await workOrderApi.resolveWorkOrder(selectedOrder.id, payload || '已处理完成'); break;
        case 'close': r = await workOrderApi.closeWorkOrder(selectedOrder.id); break;
        case 'reject': r = await workOrderApi.rejectWorkOrder(selectedOrder.id, payload || '不满足处理条件'); break;
      }
      if (r && r.code === 200) {
        setActionMsg(r.message || '操作成功');
        await refreshSelected(selectedOrder.id);
      } else setActionMsg(r?.message || '操作失败');
    } catch { setActionMsg('操作请求失败'); }
    setBusy(false);
  };

  const handleCreate = async (formData: CreateWorkOrderRequest) => {
    setBusy(true); setActionMsg(null);
    try {
      const r = await workOrderApi.createWorkOrder(formData);
      if (r.code === 200) {
        setActionMsg(r.message || '工单创建成功');
        setShowCreateModal(false);
        await load();
      } else setActionMsg(r.message || '创建失败');
    } catch { setActionMsg('创建请求失败'); }
    setBusy(false);
  };

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">工单管理</h1>
          <p className="text-gray-400">管理设备维护、故障处理等工单（按租户隔离）</p>
        </div>
        <button onClick={() => setShowCreateModal(true)}
          className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all">
          <Plus className="w-5 h-5" /><span>新建工单</span>
        </button>
      </div>

      {/* 租户范围提示 */}
      <div className="flex items-center gap-2 px-3 py-2 rounded-lg text-xs"
        style={{ background: 'rgba(0,195,255,0.06)', border: '1px solid rgba(0,195,255,0.2)', color: '#8ecfff' }}>
        <Building2 className="w-3.5 h-3.5" />
        {isSuperAdmin
          ? '超级管理员视图：展示全部租户工单（列表含「租户」列）'
          : `当前租户视图：仅展示本租户工单（${orders[0]?.appCode || '按登录账号自动隔离'}）`}
      </div>

      {/* action msg */}
      <AnimatePresence>
        {actionMsg && (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            onClick={() => setActionMsg(null)}
            className="px-3 py-2 rounded-lg text-xs text-white cursor-pointer"
            style={{ background: 'rgba(0,20,50,0.9)', border: '1px solid rgba(0,195,255,0.4)' }}>
            {actionMsg}
          </motion.div>
        )}
      </AnimatePresence>

      {/* Stats */}
      <div className="grid grid-cols-4 gap-4">
        {[
          { label: '工单总数', value: stats.total, color: '#00c3ff', icon: <ClipboardList className="w-8 h-8" /> },
          { label: '待分配', value: stats.pending, color: '#f59e0b', icon: <Clock className="w-8 h-8" /> },
          { label: '处理中', value: stats.in_progress, color: '#a78bfa', icon: <RefreshCw className="w-8 h-8" /> },
          { label: '已解决', value: stats.resolved, color: '#34d399', icon: <CheckCircle className="w-8 h-8" /> },
        ].map((s, i) => (
          <motion.div key={s.label} initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.05 }}
            className="p-6 rounded-xl"
            style={{ background: `linear-gradient(135deg, ${s.color}22, ${s.color}0a)`, border: `1px solid ${s.color}55` }}>
            <div className="flex items-center justify-between mb-2">
              <span style={{ color: s.color }}>{s.icon}</span>
              <span className="text-3xl font-bold text-white">{s.value}</span>
            </div>
            <p className="text-gray-300">{s.label}</p>
          </motion.div>
        ))}
      </div>

      {error && <div className="text-xs py-2" style={{ color: '#ffaa00' }}>{error}</div>}

      {/* Main Content */}
      <div className="flex gap-6 overflow-hidden min-h-0">
        {/* Left: List */}
        <div className="flex-1 flex flex-col bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl overflow-hidden">
          <div className="flex-shrink-0 p-4 border-b border-white/10 space-y-3">
            <div className="flex gap-3">
              <div className="flex-1 relative">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                <input type="text" placeholder="搜索工单编号、设备名称、区域..."
                  value={searchTerm} onChange={e => setSearchTerm(e.target.value)}
                  className="w-full pl-10 pr-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors" />
              </div>
              <button onClick={load} disabled={loading} className="px-3 py-2 rounded-lg hover:brightness-110 disabled:opacity-40"
                style={{ color: '#00c3ff', border: '1px solid rgba(0,195,255,0.2)' }}>
                <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
              </button>
            </div>
            <div className="flex gap-2 flex-wrap">
              <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}
                className="px-3 py-1.5 text-sm bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="all" className="bg-gray-800 text-white">全部状态</option>
                <option value="pending" className="bg-gray-800 text-white">待分配</option>
                <option value="assigned" className="bg-gray-800 text-white">已分配</option>
                <option value="in_progress" className="bg-gray-800 text-white">处理中</option>
                <option value="resolved" className="bg-gray-800 text-white">已解决</option>
                <option value="closed" className="bg-gray-800 text-white">已关闭</option>
                <option value="rejected" className="bg-gray-800 text-white">已驳回</option>
              </select>
              <select value={typeFilter} onChange={e => setTypeFilter(e.target.value)}
                className="px-3 py-1.5 text-sm bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="all" className="bg-gray-800 text-white">全部类型</option>
                <option value="fault" className="bg-gray-800 text-white">故障维修</option>
                <option value="maintenance" className="bg-gray-800 text-white">设备维保</option>
                <option value="inspection" className="bg-gray-800 text-white">定期巡检</option>
                <option value="installation" className="bg-gray-800 text-white">安装调试</option>
                <option value="other" className="bg-gray-800 text-white">其他</option>
              </select>
              <select value={priorityFilter} onChange={e => setPriorityFilter(e.target.value)}
                className="px-3 py-1.5 text-sm bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="all" className="bg-gray-800 text-white">全部优先级</option>
                <option value="low" className="bg-gray-800 text-white">低</option>
                <option value="normal" className="bg-gray-800 text-white">普通</option>
                <option value="high" className="bg-gray-800 text-white">高</option>
                <option value="urgent" className="bg-gray-800 text-white">紧急</option>
              </select>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto">
            {loading ? (
              <div className="flex flex-col items-center justify-center h-full text-gray-400 py-10">
                <ClipboardList className="w-16 h-16 mb-4" />
                <p className="text-sm">加载工单…</p>
              </div>
            ) : filteredOrders.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-gray-400 py-10">
                <ClipboardList className="w-16 h-16 mb-4" />
                <p className="text-sm">{orders.length === 0 ? '当前租户暂无工单' : '没有匹配的工单'}</p>
              </div>
            ) : (
              <div className="divide-y divide-white/5">
                {filteredOrders.map(order => (
                  <motion.div key={order.id} initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                    className={`p-4 cursor-pointer hover:bg-white/5 transition-colors ${selectedOrder?.id === order.id ? 'bg-blue-500/10 border-l-2 border-blue-500' : ''}`}
                    onClick={() => setSelectedOrder(order)}>
                    <div className="flex items-start justify-between mb-2">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-mono text-gray-400">{order.orderNo}</span>
                        <PriorityDot priority={order.priority} />
                        {isSuperAdmin && order.appCode && (
                          <span className="text-[10px] font-mono px-1.5 py-0.5 rounded"
                            style={{ color: '#8ecfff', background: 'rgba(0,195,255,0.1)', border: '1px solid rgba(0,195,255,0.25)' }}>
                            {order.appCode}
                          </span>
                        )}
                      </div>
                      <StatusBadge status={order.status} />
                    </div>
                    <h3 className="font-semibold text-white mb-1">{order.title}</h3>
                    <div className="flex items-center gap-4 text-xs text-gray-400 mb-2 flex-wrap">
                      <span className="flex items-center gap-1"><Cpu className="w-3 h-3" />{order.deviceName || '—'}</span>
                      <span className="flex items-center gap-1"><MapPin className="w-3 h-3" />{order.areaName || '—'}</span>
                      {order.assignee && <span className="flex items-center gap-1"><User className="w-3 h-3" />{order.assignee}</span>}
                    </div>
                    <div className="flex items-center gap-2">
                      <TypeBadge type={order.type} />
                      <span className="text-xs text-gray-500">{order.reportTime}</span>
                    </div>
                  </motion.div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Right: Detail */}
        {selectedOrder && (
          <motion.div initial={{ x: 300, opacity: 0 }} animate={{ x: 0, opacity: 1 }}
            className="w-[480px] flex-shrink-0 bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl overflow-hidden flex flex-col">
            <div className="flex-shrink-0 p-4 border-b border-white/10 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-white">工单详情</h2>
              <button onClick={() => setSelectedOrder(null)} className="p-1 hover:bg-white/10 rounded">
                <X className="w-5 h-5 text-gray-400" />
              </button>
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              <div>
                <div className="flex items-center justify-between mb-3">
                  <span className="text-sm font-mono text-gray-400">{selectedOrder.orderNo}</span>
                  <StatusBadge status={selectedOrder.status} />
                </div>
                <h3 className="text-lg font-semibold text-white mb-3">{selectedOrder.title}</h3>
                <div className="space-y-2 text-sm">
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">工单类型</span><TypeBadge type={selectedOrder.type} /></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">优先级</span><PriorityDot priority={selectedOrder.priority} /></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">客户</span><span className="text-white">{selectedOrder.customerName || '—'}</span></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">项目</span><span className="text-white">{selectedOrder.projectName || '—'}</span></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">设备</span><span className="text-white">{selectedOrder.deviceName || '—'}</span></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">设备编号</span><span className="text-white font-mono text-xs">{selectedOrder.deviceCode || '—'}</span></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">区域</span><span className="text-white">{selectedOrder.areaName || '—'}</span></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">报修人</span><span className="text-white">{selectedOrder.reporter || '—'}</span></div>
                  <div className="flex items-center gap-2"><span className="text-gray-400 w-20">报修时间</span><span className="text-white">{selectedOrder.reportTime}</span></div>
                  {selectedOrder.assignee && <div className="flex items-center gap-2"><span className="text-gray-400 w-20">处理人</span><span className="text-white">{selectedOrder.assignee}</span></div>}
                  {selectedOrder.appCode && <div className="flex items-center gap-2"><span className="text-gray-400 w-20">所属租户</span><span className="text-white font-mono text-xs">{selectedOrder.appCode}</span></div>}
                  {selectedOrder.estimatedTime && <div className="flex items-center gap-2"><span className="text-gray-400 w-20">预计完成</span><span className="text-white">{selectedOrder.estimatedTime}</span></div>}
                </div>
              </div>

              <div className="pt-3 border-t border-white/10">
                <h4 className="text-sm font-semibold text-white mb-2">问题描述</h4>
                <p className="text-sm text-gray-300 leading-relaxed">{selectedOrder.description || '—'}</p>
              </div>

              {/* 状态机动作（真实端点） */}
              <div className="pt-3 border-t border-white/10">
                <h4 className="text-sm font-semibold text-white mb-2">工单操作</h4>
                <div className="flex gap-2 flex-wrap">
                  {(selectedOrder.status === 'pending' || selectedOrder.status === 'assigned') && (
                    <select onChange={e => { if (e.target.value) doAction('assign', e.target.value); e.target.value = ''; }}
                      disabled={busy}
                      className="px-3 py-1.5 text-xs bg-blue-500/20 text-blue-400 border border-blue-500/30 rounded hover:bg-blue-500/30 disabled:opacity-50">
                      <option value="" className="bg-gray-800 text-white">分配给…</option>
                      {STAFF_LIST.map(s => <option key={s} value={s} className="bg-gray-800 text-white">{s}</option>)}
                    </select>
                  )}
                  {(selectedOrder.status === 'pending' || selectedOrder.status === 'assigned') && (
                    <button onClick={() => doAction('start')} disabled={busy}
                      className="px-3 py-1.5 text-xs rounded disabled:opacity-50"
                      style={{ background: STATUS_CONFIG.in_progress.bg, color: STATUS_CONFIG.in_progress.color, border: `1px solid ${STATUS_CONFIG.in_progress.border}` }}>
                      开始处理
                    </button>
                  )}
                  {(selectedOrder.status === 'in_progress' || selectedOrder.status === 'assigned') && (
                    <button onClick={() => doAction('resolve', '已处理完成')} disabled={busy}
                      className="px-3 py-1.5 text-xs rounded disabled:opacity-50"
                      style={{ background: STATUS_CONFIG.resolved.bg, color: STATUS_CONFIG.resolved.color, border: `1px solid ${STATUS_CONFIG.resolved.border}` }}>
                      标记已解决
                    </button>
                  )}
                  {selectedOrder.status === 'resolved' && (
                    <button onClick={() => doAction('close')} disabled={busy}
                      className="px-3 py-1.5 text-xs rounded disabled:opacity-50"
                      style={{ background: STATUS_CONFIG.closed.bg, color: STATUS_CONFIG.closed.color, border: `1px solid ${STATUS_CONFIG.closed.border}` }}>
                      关闭工单
                    </button>
                  )}
                  {(selectedOrder.status === 'pending' || selectedOrder.status === 'assigned' || selectedOrder.status === 'in_progress') && (
                    <button onClick={() => doAction('reject', '不满足处理条件')} disabled={busy}
                      className="px-3 py-1.5 text-xs rounded disabled:opacity-50"
                      style={{ background: STATUS_CONFIG.rejected.bg, color: STATUS_CONFIG.rejected.color, border: `1px solid ${STATUS_CONFIG.rejected.border}` }}>
                      驳回工单
                    </button>
                  )}
                  {selectedOrder.status === 'closed' && <span className="text-xs text-gray-500">工单已关闭，无可用操作</span>}
                </div>
              </div>

              {/* 处理记录（真实 /logs 端点） */}
              <div className="pt-3 border-t border-white/10">
                <div className="flex items-center justify-between mb-3">
                  <h4 className="text-sm font-semibold text-white">处理记录</h4>
                  <button onClick={() => selectedOrder && workOrderApi.getWorkOrderLogs(selectedOrder.id).then(r => setOrderLogs(r.code === 200 && r.data ? r.data : [])).catch(() => {})}
                    className="p-1 hover:brightness-125" style={{ color: '#00c3ff' }}><RefreshCw className="w-3.5 h-3.5" /></button>
                </div>
                {logsLoading ? <p className="text-xs text-gray-500">加载处理记录…</p>
                  : orderLogs.length === 0 ? <p className="text-xs text-gray-500">暂无处理记录</p>
                    : (
                      <div className="space-y-3">
                        {orderLogs.map((log, i) => (
                          <div key={log.id} className="flex gap-3">
                            <div className="flex flex-col items-center">
                              <div className="w-2 h-2 rounded-full bg-blue-400 flex-shrink-0 mt-1.5" />
                              {i < orderLogs.length - 1 && <div className="w-px flex-1 bg-white/10 my-1" />}
                            </div>
                            <div className="flex-1 pb-3">
                              <div className="flex items-center gap-2 mb-1">
                                <span className="text-sm font-medium text-white">{log.action}</span>
                                <span className="text-xs text-gray-500">{log.createdAt}</span>
                              </div>
                              <div className="text-xs text-gray-400">操作人：{log.operator || '系统'}</div>
                              {log.comment && <div className="mt-1 text-sm text-gray-300 bg-white/5 px-2 py-1 rounded">{log.comment}</div>}
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
              </div>
            </div>
          </motion.div>
        )}
      </div>

      {/* Create Work Order Modal */}
      <AnimatePresence>
        {showCreateModal && (
          <CreateWorkOrderModal
            customers={customers}
            currentCustomer={currentCustomer}
            isSuperAdmin={isSuperAdmin}
            onClose={() => setShowCreateModal(false)}
            onSave={handleCreate}
          />
        )}
      </AnimatePresence>
    </div>
  );
}

// ── Create Work Order Modal ────────────────────────────────────────────────────
function CreateWorkOrderModal({
  customers, currentCustomer, isSuperAdmin, onClose, onSave
}: {
  customers: any[];
  currentCustomer: any;
  isSuperAdmin: boolean;
  onClose: () => void;
  onSave: (data: CreateWorkOrderRequest) => void;
}) {
  const [form, setForm] = useState({
    title: '',
    type: 'fault' as WorkOrderType,
    priority: 'normal' as WorkOrderPriority,
    customerId: '' as string,
    projectName: '',
    deviceName: '',
    deviceCode: '',
    areaName: '',
    description: '',
    appCode: '',
  });

  const handleSubmit = () => {
    if (!form.title.trim() || !form.description.trim()) {
      alert('请填写工单标题与问题描述');
      return;
    }
    onSave({
      title: form.title.trim(),
      type: form.type,
      priority: form.priority,
      customerId: form.customerId ? Number(form.customerId) : null,
      deviceName: form.deviceName || null,
      deviceCode: form.deviceCode || null,
      areaName: form.areaName || null,
      projectName: form.projectName || null,
      description: form.description.trim(),
      reporter: currentCustomer?.name || '系统',
      appCode: isSuperAdmin && form.appCode ? form.appCode : null,
    });
  };

  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4" onClick={onClose}>
      <motion.div initial={{ opacity: 0, scale: 0.95 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0, scale: 0.95 }}
        className="bg-gray-900 border border-white/10 rounded-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto p-6"
        onClick={e => e.stopPropagation()}>
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-2xl font-bold text-white">新建工单</h2>
          <button onClick={onClose} className="p-1 hover:bg-white/10 rounded"><X className="w-5 h-5 text-gray-400" /></button>
        </div>

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">工单标题 <span className="text-red-400">*</span></label>
            <input type="text" value={form.title} onChange={e => setForm({ ...form, title: e.target.value })}
              placeholder="请输入工单标题"
              className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500" />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">工单类型</label>
              <select value={form.type} onChange={e => setForm({ ...form, type: e.target.value as WorkOrderType })}
                className="w-full px-3 py-2 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="fault" className="bg-gray-800 text-white">故障维修</option>
                <option value="maintenance" className="bg-gray-800 text-white">设备维保</option>
                <option value="inspection" className="bg-gray-800 text-white">定期巡检</option>
                <option value="installation" className="bg-gray-800 text-white">安装调试</option>
                <option value="other" className="bg-gray-800 text-white">其他</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">优先级</label>
              <select value={form.priority} onChange={e => setForm({ ...form, priority: e.target.value as WorkOrderPriority })}
                className="w-full px-3 py-2 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="low" className="bg-gray-800 text-white">低</option>
                <option value="normal" className="bg-gray-800 text-white">普通</option>
                <option value="high" className="bg-gray-800 text-white">高</option>
                <option value="urgent" className="bg-gray-800 text-white">紧急</option>
              </select>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">客户</label>
              <select value={form.customerId} onChange={e => setForm({ ...form, customerId: e.target.value })}
                className="w-full px-3 py-2 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="" className="bg-gray-800 text-white">请选择客户（可选）</option>
                {customers.map(c => <option key={c.id} value={c.id} className="bg-gray-800 text-white">{c.name}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">项目</label>
              <input type="text" value={form.projectName} onChange={e => setForm({ ...form, projectName: e.target.value })}
                placeholder="请输入项目名称"
                className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500" />
            </div>
          </div>

          {isSuperAdmin && (
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">目标租户 AppCode（超管代建；留空则归属当前租户）</label>
              <input type="text" value={form.appCode} onChange={e => setForm({ ...form, appCode: e.target.value })}
                placeholder="如 customer_001"
                className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white font-mono text-sm placeholder-gray-500 focus:outline-none focus:border-blue-500" />
            </div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">设备名称</label>
              <input type="text" value={form.deviceName} onChange={e => setForm({ ...form, deviceName: e.target.value })}
                placeholder="请输入设备名称"
                className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">设备编号</label>
              <input type="text" value={form.deviceCode} onChange={e => setForm({ ...form, deviceCode: e.target.value })}
                placeholder="请输入设备编号"
                className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500" />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">区域</label>
            <input type="text" value={form.areaName} onChange={e => setForm({ ...form, areaName: e.target.value })}
              placeholder="请输入区域"
              className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500" />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">问题描述 <span className="text-red-400">*</span></label>
            <textarea value={form.description} onChange={e => setForm({ ...form, description: e.target.value })}
              placeholder="请详细描述问题..." rows={4}
              className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 resize-none" />
          </div>

          <div className="flex gap-4 pt-4">
            <button onClick={handleSubmit}
              className="flex-1 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 text-white rounded-lg transition-all">
              创建工单
            </button>
            <button onClick={onClose}
              className="flex-1 px-4 py-2 bg-white/10 hover:bg-white/20 text-white rounded-lg transition-colors">
              取消
            </button>
          </div>
        </div>
      </motion.div>
    </div>
  );
}
