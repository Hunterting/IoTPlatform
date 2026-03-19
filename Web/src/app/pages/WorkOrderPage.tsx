import { useState, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Plus, Search, Eye, CheckCircle, Clock, XCircle,
  User, Calendar, ChevronRight,
  Wrench, Zap, Thermometer, Cpu, X, Send,
  ClipboardList, RefreshCw, MapPin, Building2, FileText,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import {
  SHARED_WORK_ORDERS,
  WorkOrder,
  WorkOrderStatus,
  WorkOrderPriority,
  WorkOrderType,
  WorkOrderLog,
} from '@/app/config/sharedMockData';

// ── Mock data ─────────────────────────────────────────────────────────────────
const STAFF_LIST = ['张工', '李工', '王工', '赵工', '刘工', '陈工'];

const initialOrders: WorkOrder[] = SHARED_WORK_ORDERS;

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Convert a 6-digit hex color + 0-1 alpha to rgba() so Motion can animate it */
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

const PRIORITY_CONFIG: Record<WorkOrderPriority, { label: string; color: string; dot: string }> = {
  low:    { label: '低', color: '#9ca3af', dot: 'bg-gray-400' },
  normal: { label: '普通', color: '#60a5fa', dot: 'bg-blue-400' },
  high:   { label: '高', color: '#fb923c', dot: 'bg-orange-400' },
  urgent: { label: '紧急', color: '#f87171', dot: 'bg-red-400' },
};

const TYPE_CONFIG: Record<WorkOrderType, { label: string; icon: React.ReactNode; color: string }> = {
  fault:        { label: '故障维修', icon: <Wrench className="w-3.5 h-3.5" />, color: '#f87171' },
  maintenance:  { label: '设备维保', icon: <Zap className="w-3.5 h-3.5" />, color: '#fb923c' },
  inspection:   { label: '定期巡检', icon: <ClipboardList className="w-3.5 h-3.5" />, color: '#60a5fa' },
  installation: { label: '安装调试', icon: <Cpu className="w-3.5 h-3.5" />, color: '#a78bfa' },
  other:        { label: '其他', icon: <Thermometer className="w-3.5 h-3.5" />, color: '#9ca3af' },
};

function StatusBadge({ status }: { status: WorkOrderStatus }) {
  const cfg = STATUS_CONFIG[status];
  return (
    <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium"
      style={{ color: cfg.color, background: cfg.bg, border: `1px solid ${cfg.border}` }}>
      {cfg.icon}{cfg.label}
    </span>
  );
}

function PriorityDot({ priority }: { priority: WorkOrderPriority }) {
  const cfg = PRIORITY_CONFIG[priority];
  return (
    <span className="inline-flex items-center gap-1.5 text-xs" style={{ color: cfg.color }}>
      <span className={`w-2 h-2 rounded-full flex-shrink-0 ${cfg.dot} ${priority === 'urgent' ? 'animate-pulse' : ''}`} />
      {cfg.label}
    </span>
  );
}

function TypeBadge({ type }: { type: WorkOrderType }) {
  const cfg = TYPE_CONFIG[type];
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
  const [orders, setOrders] = useState<WorkOrder[]>(initialOrders);
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<WorkOrderStatus | 'all'>('all');
  const [typeFilter, setTypeFilter] = useState<WorkOrderType | 'all'>('all');
  const [priorityFilter, setPriorityFilter] = useState<WorkOrderPriority | 'all'>('all');
  const [selectedOrder, setSelectedOrder] = useState<WorkOrder | null>(null);
  const [newComment, setNewComment] = useState('');
  const [showCreateModal, setShowCreateModal] = useState(false);

  // Filter
  const filteredOrders = useMemo(() => {
    return orders.filter(o => {
      const matchSearch = searchTerm === '' || 
        o.orderNo.toLowerCase().includes(searchTerm.toLowerCase()) ||
        o.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
        o.deviceName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        o.area.toLowerCase().includes(searchTerm.toLowerCase());
      const matchStatus = statusFilter === 'all' || o.status === statusFilter;
      const matchType = typeFilter === 'all' || o.type === typeFilter;
      const matchPriority = priorityFilter === 'all' || o.priority === priorityFilter;
      return matchSearch && matchStatus && matchType && matchPriority;
    });
  }, [orders, searchTerm, statusFilter, typeFilter, priorityFilter]);

  // Stats
  const stats = useMemo(() => {
    return {
      total: orders.length,
      pending: orders.filter(o => o.status === 'pending').length,
      in_progress: orders.filter(o => o.status === 'in_progress').length,
      resolved: orders.filter(o => o.status === 'resolved').length,
    };
  }, [orders]);

  // Actions
  const handleAssign = (orderId: string, assignee: string) => {
    setOrders(prev => prev.map(o => {
      if (o.id === orderId) {
        const newLog: WorkOrderLog = {
          id: `l${Date.now()}`,
          time: new Date().toISOString().slice(0, 16).replace('T', ' '),
          operator: user?.username || '系统',
          action: '分配工单',
          comment: `分配给${assignee}`,
        };
        return { ...o, assignee, status: 'assigned', logs: [...o.logs, newLog] };
      }
      return o;
    }));
    if (selectedOrder?.id === orderId) {
      setSelectedOrder(prev => prev ? { ...prev, assignee, status: 'assigned' } : null);
    }
  };

  const handleStatusChange = (orderId: string, newStatus: WorkOrderStatus) => {
    setOrders(prev => prev.map(o => {
      if (o.id === orderId) {
        const actionMap: Record<WorkOrderStatus, string> = {
          pending: '创建工单',
          assigned: '分配工单',
          in_progress: '开始处理',
          resolved: '解决工单',
          closed: '关闭工单',
          rejected: '驳回工单',
        };
        const newLog: WorkOrderLog = {
          id: `l${Date.now()}`,
          time: new Date().toISOString().slice(0, 16).replace('T', ' '),
          operator: user?.username || '系统',
          action: actionMap[newStatus],
        };
        return { ...o, status: newStatus, logs: [...o.logs, newLog] };
      }
      return o;
    }));
    if (selectedOrder?.id === orderId) {
      setSelectedOrder(prev => prev ? { ...prev, status: newStatus } : null);
    }
  };

  const handleAddComment = () => {
    if (!selectedOrder || !newComment.trim()) return;
    const newLog: WorkOrderLog = {
      id: `l${Date.now()}`,
      time: new Date().toISOString().slice(0, 16).replace('T', ' '),
      operator: user?.username || '系统',
      action: '添加备注',
      comment: newComment,
    };
    setOrders(prev => prev.map(o => 
      o.id === selectedOrder.id ? { ...o, logs: [...o.logs, newLog] } : o
    ));
    setSelectedOrder(prev => prev ? { ...prev, logs: [...prev.logs, newLog] } : null);
    setNewComment('');
  };

  const handleCreateOrder = (formData: any) => {
    const newOrder: WorkOrder = {
      id: `wo${Date.now()}`,
      orderNo: `WO${new Date().toISOString().slice(0, 10).replace(/-/g, '')}${String(orders.length + 1).padStart(3, '0')}`,
      title: formData.title,
      type: formData.type,
      priority: formData.priority,
      status: 'pending',
      customer: formData.customer,
      project: formData.project,
      deviceName: formData.deviceName,
      deviceCode: formData.deviceCode,
      area: formData.area,
      description: formData.description,
      reporter: user?.username || '系统',
      reportTime: new Date().toISOString().slice(0, 16).replace('T', ' '),
      logs: [{
        id: `l${Date.now()}`,
        time: new Date().toISOString().slice(0, 16).replace('T', ' '),
        operator: user?.username || '系统',
        action: '创建工单',
        comment: '工单已创建，待分配',
      }],
    };
    setOrders(prev => [newOrder, ...prev]);
    setShowCreateModal(false);
  };

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">工单管理</h1>
          <p className="text-gray-400">管理设备维护、故障处理等工单</p>
        </div>
        <button 
          onClick={() => setShowCreateModal(true)}
          className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all"
        >
          <Plus className="w-5 h-5" />
          <span>新建工单</span>
        </button>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-4 gap-4">
        <motion.div 
          initial={{ opacity: 0, y: 20 }} 
          animate={{ opacity: 1, y: 0 }}
          className="p-6 bg-gradient-to-br from-blue-500/20 to-cyan-500/10 border border-blue-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <ClipboardList className="w-8 h-8 text-blue-400" />
            <span className="text-3xl font-bold text-white">{stats.total}</span>
          </div>
          <p className="text-gray-300">工单总数</p>
        </motion.div>
        <motion.div 
          initial={{ opacity: 0, y: 20 }} 
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.05 }}
          className="p-6 bg-gradient-to-br from-amber-500/20 to-orange-500/10 border border-amber-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <Clock className="w-8 h-8 text-amber-400" />
            <span className="text-3xl font-bold text-white">{stats.pending}</span>
          </div>
          <p className="text-gray-300">待分配</p>
        </motion.div>
        <motion.div 
          initial={{ opacity: 0, y: 20 }} 
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1 }}
          className="p-6 bg-gradient-to-br from-purple-500/20 to-pink-500/10 border border-purple-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <RefreshCw className="w-8 h-8 text-purple-400" />
            <span className="text-3xl font-bold text-white">{stats.in_progress}</span>
          </div>
          <p className="text-gray-300">处理中</p>
        </motion.div>
        <motion.div 
          initial={{ opacity: 0, y: 20 }} 
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.15 }}
          className="p-6 bg-gradient-to-br from-green-500/20 to-emerald-500/10 border border-green-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <CheckCircle className="w-8 h-8 text-green-400" />
            <span className="text-3xl font-bold text-white">{stats.resolved}</span>
          </div>
          <p className="text-gray-300">已解决</p>
        </motion.div>
      </div>

      {/* Main Content */}
      <div className="flex gap-6 overflow-hidden min-h-0">
        {/* Left: List */}
        <div className="flex-1 flex flex-col bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl overflow-hidden">
          {/* Filters */}
          <div className="flex-shrink-0 p-4 border-b border-white/10 space-y-3">
            <div className="flex gap-3">
              <div className="flex-1 relative">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                <input
                  type="text"
                  placeholder="搜索工单编号、设备名称、区域..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  className="w-full pl-10 pr-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                />
              </div>
            </div>
            <div className="flex gap-2 flex-wrap">
              <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as any)}
                className="px-3 py-1.5 text-sm bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="all" className="bg-gray-800 text-white">全部状态</option>
                <option value="pending" className="bg-gray-800 text-white">待分配</option>
                <option value="assigned" className="bg-gray-800 text-white">已分配</option>
                <option value="in_progress" className="bg-gray-800 text-white">处理中</option>
                <option value="resolved" className="bg-gray-800 text-white">已解决</option>
                <option value="closed" className="bg-gray-800 text-white">已关闭</option>
                <option value="rejected" className="bg-gray-800 text-white">已驳回</option>
              </select>
              <select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value as any)}
                className="px-3 py-1.5 text-sm bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="all" className="bg-gray-800 text-white">全部类型</option>
                <option value="fault" className="bg-gray-800 text-white">故障维修</option>
                <option value="maintenance" className="bg-gray-800 text-white">设备维保</option>
                <option value="inspection" className="bg-gray-800 text-white">定期巡检</option>
                <option value="installation" className="bg-gray-800 text-white">安装调试</option>
                <option value="other" className="bg-gray-800 text-white">其他</option>
              </select>
              <select value={priorityFilter} onChange={(e) => setPriorityFilter(e.target.value as any)}
                className="px-3 py-1.5 text-sm bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500">
                <option value="all" className="bg-gray-800 text-white">全部优先级</option>
                <option value="low" className="bg-gray-800 text-white">低</option>
                <option value="normal" className="bg-gray-800 text-white">普通</option>
                <option value="high" className="bg-gray-800 text-white">高</option>
                <option value="urgent" className="bg-gray-800 text-white">紧急</option>
              </select>
            </div>
          </div>

          {/* List */}
          <div className="flex-1 overflow-y-auto">
            {filteredOrders.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-gray-400">
                <ClipboardList className="w-16 h-16 mb-4" />
                <p>暂无工单</p>
              </div>
            ) : (
              <div className="divide-y divide-white/5">
                {filteredOrders.map((order) => (
                  <motion.div
                    key={order.id}
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    className={`p-4 cursor-pointer hover:bg-white/5 transition-colors ${
                      selectedOrder?.id === order.id ? 'bg-blue-500/10 border-l-2 border-blue-500' : ''
                    }`}
                    onClick={() => setSelectedOrder(order)}
                  >
                    <div className="flex items-start justify-between mb-2">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-mono text-gray-400">{order.orderNo}</span>
                        <PriorityDot priority={order.priority} />
                      </div>
                      <StatusBadge status={order.status} />
                    </div>
                    <h3 className="font-semibold text-white mb-1">{order.title}</h3>
                    <div className="flex items-center gap-4 text-xs text-gray-400 mb-2">
                      <span className="flex items-center gap-1">
                        <Cpu className="w-3 h-3" />
                        {order.deviceName}
                      </span>
                      <span className="flex items-center gap-1">
                        <MapPin className="w-3 h-3" />
                        {order.area}
                      </span>
                      {order.assignee && (
                        <span className="flex items-center gap-1">
                          <User className="w-3 h-3" />
                          {order.assignee}
                        </span>
                      )}
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
          <motion.div
            initial={{ x: 300, opacity: 0 }}
            animate={{ x: 0, opacity: 1 }}
            className="w-[480px] flex-shrink-0 bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl overflow-hidden flex flex-col"
          >
            {/* Header */}
            <div className="flex-shrink-0 p-4 border-b border-white/10 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-white">工单详情</h2>
              <button onClick={() => setSelectedOrder(null)} className="p-1 hover:bg-white/10 rounded">
                <X className="w-5 h-5 text-gray-400" />
              </button>
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {/* Basic Info */}
              <div>
                <div className="flex items-center justify-between mb-3">
                  <span className="text-sm font-mono text-gray-400">{selectedOrder.orderNo}</span>
                  <StatusBadge status={selectedOrder.status} />
                </div>
                <h3 className="text-lg font-semibold text-white mb-3">{selectedOrder.title}</h3>
                
                <div className="space-y-2 text-sm">
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">工单类型</span>
                    <TypeBadge type={selectedOrder.type} />
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">优先级</span>
                    <PriorityDot priority={selectedOrder.priority} />
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">客户</span>
                    <span className="text-white">{selectedOrder.customer}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">项目</span>
                    <span className="text-white">{selectedOrder.project}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">设备</span>
                    <span className="text-white">{selectedOrder.deviceName}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">设备编号</span>
                    <span className="text-white font-mono text-xs">{selectedOrder.deviceCode}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">区域</span>
                    <span className="text-white">{selectedOrder.area}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">报修人</span>
                    <span className="text-white">{selectedOrder.reporter}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 w-20">报修时间</span>
                    <span className="text-white">{selectedOrder.reportTime}</span>
                  </div>
                  {selectedOrder.assignee && (
                    <div className="flex items-center gap-2">
                      <span className="text-gray-400 w-20">处理人</span>
                      <span className="text-white">{selectedOrder.assignee}</span>
                    </div>
                  )}
                  {selectedOrder.estimatedTime && (
                    <div className="flex items-center gap-2">
                      <span className="text-gray-400 w-20">预计完成</span>
                      <span className="text-white">{selectedOrder.estimatedTime}</span>
                    </div>
                  )}
                </div>
              </div>

              {/* Description */}
              <div className="pt-3 border-t border-white/10">
                <h4 className="text-sm font-semibold text-white mb-2">问题描述</h4>
                <p className="text-sm text-gray-300 leading-relaxed">{selectedOrder.description}</p>
              </div>

              {/* Actions */}
              {selectedOrder.status === 'pending' && (
                <div className="pt-3 border-t border-white/10">
                  <h4 className="text-sm font-semibold text-white mb-2">分配工单</h4>
                  <div className="flex gap-2 flex-wrap">
                    {STAFF_LIST.map(staff => (
                      <button key={staff} onClick={() => handleAssign(selectedOrder.id, staff)}
                        className="px-3 py-1.5 text-sm bg-blue-500/20 text-blue-400 border border-blue-500/30 rounded hover:bg-blue-500/30 transition-colors">
                        {staff}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {selectedOrder.status !== 'closed' && selectedOrder.status !== 'resolved' && (
                <div className="pt-3 border-t border-white/10">
                  <h4 className="text-sm font-semibold text-white mb-2">更新状态</h4>
                  <div className="flex gap-2 flex-wrap">
                    {(['in_progress', 'resolved', 'rejected'] as WorkOrderStatus[]).map(status => (
                      <button key={status} onClick={() => handleStatusChange(selectedOrder.id, status)}
                        className="px-3 py-1.5 text-xs rounded transition-colors"
                        style={{ 
                          background: STATUS_CONFIG[status].bg, 
                          color: STATUS_CONFIG[status].color,
                          border: `1px solid ${STATUS_CONFIG[status].border}`
                        }}>
                        {STATUS_CONFIG[status].label}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {/* Process Logs */}
              <div className="pt-3 border-t border-white/10">
                <h4 className="text-sm font-semibold text-white mb-3">处理记录</h4>
                <div className="space-y-3">
                  {selectedOrder.logs.map((log, i) => (
                    <div key={log.id} className="flex gap-3">
                      <div className="flex flex-col items-center">
                        <div className="w-2 h-2 rounded-full bg-blue-400 flex-shrink-0 mt-1.5" />
                        {i < selectedOrder.logs.length - 1 && (
                          <div className="w-px flex-1 bg-white/10 my-1" />
                        )}
                      </div>
                      <div className="flex-1 pb-3">
                        <div className="flex items-center gap-2 mb-1">
                          <span className="text-sm font-medium text-white">{log.action}</span>
                          <span className="text-xs text-gray-500">{log.time}</span>
                        </div>
                        <div className="text-xs text-gray-400">操作人：{log.operator}</div>
                        {log.comment && (
                          <div className="mt-1 text-sm text-gray-300 bg-white/5 px-2 py-1 rounded">
                            {log.comment}
                          </div>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* Add Comment */}
              <div className="pt-3 border-t border-white/10">
                <h4 className="text-sm font-semibold text-white mb-2">添加备注</h4>
                <div className="flex gap-2">
                  <input
                    type="text"
                    value={newComment}
                    onChange={(e) => setNewComment(e.target.value)}
                    onKeyDown={(e) => e.key === 'Enter' && handleAddComment()}
                    placeholder="输入备注内容..."
                    className="flex-1 px-3 py-2 text-sm bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
                  />
                  <button onClick={handleAddComment}
                    className="px-4 py-2 bg-blue-500 hover:bg-blue-600 text-white rounded-lg transition-colors flex items-center gap-1">
                    <Send className="w-4 h-4" />
                  </button>
                </div>
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
            onClose={() => setShowCreateModal(false)}
            onSave={handleCreateOrder}
          />
        )}
      </AnimatePresence>
    </div>
  );
}

// ── Create Work Order Modal ────────────────────────────────────────────────
function CreateWorkOrderModal({ 
  customers, 
  currentCustomer, 
  onClose, 
  onSave 
}: { 
  customers: any[]; 
  currentCustomer: any; 
  onClose: () => void; 
  onSave: (data: any) => void;
}) {
  const [form, setForm] = useState({
    title: '',
    type: 'fault' as WorkOrderType,
    priority: 'normal' as WorkOrderPriority,
    customer: currentCustomer?.name || '',
    project: '',
    deviceName: '',
    deviceCode: '',
    area: '',
    description: '',
  });

  const handleSubmit = () => {
    if (!form.title || !form.customer || !form.deviceName || !form.description) {
      alert('请填写必填项');
      return;
    }
    onSave(form);
  };

  // 获取选中客户的项目列表
  const selectedCustomer = customers.find(c => c.name === form.customer);
  const projects = selectedCustomer?.projects || [];

  return (
    <div 
      className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4" 
      onClick={onClose}
    >
      <motion.div 
        initial={{ opacity: 0, scale: 0.95 }}
        animate={{ opacity: 1, scale: 1 }}
        exit={{ opacity: 0, scale: 0.95 }}
        className="bg-gray-900 border border-white/10 rounded-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto p-6" 
        onClick={e => e.stopPropagation()}
      >
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-2xl font-bold text-white">新建工单</h2>
          <button onClick={onClose} className="p-1 hover:bg-white/10 rounded">
            <X className="w-5 h-5 text-gray-400" />
          </button>
        </div>

        <div className="space-y-4">
          {/* 工单标题 */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              工单标题 <span className="text-red-400">*</span>
            </label>
            <input
              type="text"
              value={form.title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
              placeholder="请输入工单标题"
              className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
            />
          </div>

          {/* 工单类型和优先级 */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">工单类型</label>
              <select
                value={form.type}
                onChange={(e) => setForm({ ...form, type: e.target.value as WorkOrderType })}
                className="w-full px-3 py-2 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500"
              >
                <option value="fault" className="bg-gray-800 text-white">故障维修</option>
                <option value="maintenance" className="bg-gray-800 text-white">设备维保</option>
                <option value="inspection" className="bg-gray-800 text-white">定期巡检</option>
                <option value="installation" className="bg-gray-800 text-white">安装调试</option>
                <option value="other" className="bg-gray-800 text-white">其他</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">优先级</label>
              <select
                value={form.priority}
                onChange={(e) => setForm({ ...form, priority: e.target.value as WorkOrderPriority })}
                className="w-full px-3 py-2 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500"
              >
                <option value="low" className="bg-gray-800 text-white">低</option>
                <option value="normal" className="bg-gray-800 text-white">普通</option>
                <option value="high" className="bg-gray-800 text-white">高</option>
                <option value="urgent" className="bg-gray-800 text-white">紧急</option>
              </select>
            </div>
          </div>

          {/* 客户和项目 */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">
                客户 <span className="text-red-400">*</span>
              </label>
              <select
                value={form.customer}
                onChange={(e) => setForm({ ...form, customer: e.target.value, project: '' })}
                className="w-full px-3 py-2 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500"
              >
                <option value="" className="bg-gray-800 text-white">请选择客户</option>
                {customers.map(c => (
                  <option key={c.id} value={c.name} className="bg-gray-800 text-white">{c.name}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">项目</label>
              <select
                value={form.project}
                onChange={(e) => setForm({ ...form, project: e.target.value })}
                className="w-full px-3 py-2 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 disabled:opacity-50"
                disabled={!form.customer}
              >
                <option value="" className="bg-gray-800 text-white">请选择项目</option>
                {projects.map((p: any) => (
                  <option key={p.id} value={p.name} className="bg-gray-800 text-white">{p.name}</option>
                ))}
              </select>
            </div>
          </div>

          {/* 设备信息 */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">
                设备名称 <span className="text-red-400">*</span>
              </label>
              <input
                type="text"
                value={form.deviceName}
                onChange={(e) => setForm({ ...form, deviceName: e.target.value })}
                placeholder="请输入设备名称"
                className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">设备编号</label>
              <input
                type="text"
                value={form.deviceCode}
                onChange={(e) => setForm({ ...form, deviceCode: e.target.value })}
                placeholder="请输入设备编号"
                className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
              />
            </div>
          </div>

          {/* 区域 */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">区域</label>
            <input
              type="text"
              value={form.area}
              onChange={(e) => setForm({ ...form, area: e.target.value })}
              placeholder="请输入区域"
              className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
            />
          </div>

          {/* 问题描述 */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              问题描述 <span className="text-red-400">*</span>
            </label>
            <textarea
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              placeholder="请详细描述问题..."
              rows={4}
              className="w-full px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 resize-none"
            />
          </div>

          {/* 按钮 */}
          <div className="flex gap-4 pt-4">
            <button
              onClick={handleSubmit}
              className="flex-1 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 text-white rounded-lg transition-all"
            >
              创建工单
            </button>
            <button
              onClick={onClose}
              className="flex-1 px-4 py-2 bg-white/10 hover:bg-white/20 text-white rounded-lg transition-colors"
            >
              取消
            </button>
          </div>
        </div>
      </motion.div>
    </div>
  );
}
