import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  AlertTriangle, ClipboardCheck,
  Search, Filter, Download, RefreshCw, ChevronDown,
  CheckCircle, XCircle, Clock, Eye, User,
  Cpu, Thermometer, Zap, MapPin, Bell, AlertCircle,
  Info, Play, Pause, CheckCircle2, Calendar,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { SHARED_ALERT_RECORDS, AlertRecord as SharedAlertRecord } from '@/app/config/sharedMockData';

// ── Types ──────────────────────────────────────────────────────────────────────
type AlertRecord = SharedAlertRecord;

interface ProcessProgress {
  id: string;
  alertId: string;
  alertNo: string;
  deviceName: string;
  area: string;
  alertType: string;
  level: 'info' | 'warning' | 'critical';
  reportTime: string;
  currentStatus: 'pending' | 'processing' | 'resolved' | 'ignored';
  progress: number;
  steps: ProgressStep[];
  assignee?: string;
  estimatedResolve?: string;
}

interface ProgressStep {
  id: string;
  name: string;
  status: 'completed' | 'current' | 'pending';
  time?: string;
  operator?: string;
  remark?: string;
}

// ── Mock Data ──────────────────────────────────────────────────────────────────
const mockAlertRecords: AlertRecord[] = SHARED_ALERT_RECORDS;

const mockProcessProgress: ProcessProgress[] = [
  {
    id: 'pp1', alertId: 'a1', alertNo: 'ALT20260304001',
    deviceName: '冷藏柜 SR-F520BX', area: '中央厨房',
    alertType: '温度异常', level: 'critical',
    reportTime: '2026-03-04 06:23:11', currentStatus: 'processing',
    progress: 60, assignee: '张工', estimatedResolve: '2026-03-04 14:00',
    steps: [
      { id: 's1', name: '告警触发', status: 'completed', time: '06:23:11', operator: '系统', remark: '冷藏柜温度 8.5°C 超过阈值 5°C' },
      { id: 's2', name: '告警通知', status: 'completed', time: '06:23:15', operator: '系统', remark: '已发送短信通知给张管理员、李运营' },
      { id: 's3', name: '工单创建', status: 'completed', time: '06:35:00', operator: '张管理员', remark: '创建紧急工单 WO20260304001' },
      { id: 's4', name: '工单分配', status: 'completed', time: '06:40:00', operator: '张管理员', remark: '分配给张工处理，预计完成时间 14:00' },
      { id: 's5', name: '现场处理', status: 'current', time: '07:10:00', operator: '张工', remark: '已到现场检查，发现压缩机散热异常，正在更换散热风扇' },
      { id: 's6', name: '验证确认', status: 'pending' },
      { id: 's7', name: '工单关闭', status: 'pending' },
    ],
  },
  {
    id: 'pp2', alertId: 'a5', alertNo: 'ALT20260304005',
    deviceName: '冷冻库 FZ-1000', area: '冷冻库',
    alertType: '温度异常', level: 'critical',
    reportTime: '2026-03-04 09:30:00', currentStatus: 'processing',
    progress: 40, assignee: '王工', estimatedResolve: '2026-03-04 18:00',
    steps: [
      { id: 's1', name: '告警触发', status: 'completed', time: '09:30:00', operator: '系统', remark: '冷冻库温度 -8°C，阈值 -18°C，偏差 10°C' },
      { id: 's2', name: '告警通知', status: 'completed', time: '09:30:05', operator: '系统', remark: '紧急通知已发送' },
      { id: 's3', name: '工单创建', status: 'completed', time: '09:45:00', operator: '李运营', remark: '创建紧急工单' },
      { id: 's4', name: '工单分配', status: 'current', time: '10:00:00', operator: '张管理员', remark: '分配给王工，制冷系统故障中' },
      { id: 's5', name: '现场处理', status: 'pending' },
      { id: 's6', name: '验证确认', status: 'pending' },
      { id: 's7', name: '工单关闭', status: 'pending' },
    ],
  },
  {
    id: 'pp3', alertId: 'a2', alertNo: 'ALT20260304002',
    deviceName: '排烟风机 EXH-200', area: '烹饪区',
    alertType: '烟雾异常', level: 'warning',
    reportTime: '2026-03-04 07:45:00', currentStatus: 'resolved',
    progress: 100, assignee: '李工', estimatedResolve: '2026-03-04 09:12:00',
    steps: [
      { id: 's1', name: '告警触发', status: 'completed', time: '07:45:00', operator: '系统', remark: '烟雾传感器检测到异常' },
      { id: 's2', name: '告警通知', status: 'completed', time: '07:45:03', operator: '系统', remark: '通知相关人员' },
      { id: 's3', name: '工单创建', status: 'completed', time: '07:50:00', operator: '李运营', remark: '创建维修工单' },
      { id: 's4', name: '工单分配', status: 'completed', time: '07:55:00', operator: '张管理员', remark: '分配给李工' },
      { id: 's5', name: '现场处理', status: 'completed', time: '08:10:00', operator: '李工', remark: '检查后确认是厨师烹饪油烟较大触发告警，已清洁传感器' },
      { id: 's6', name: '验证确认', status: 'completed', time: '08:45:00', operator: '李工', remark: '持续监测30分钟无异常' },
      { id: 's7', name: '工单关闭', status: 'completed', time: '09:12:00', operator: '李运营', remark: '工单已完成并关闭' },
    ],
  },
  {
    id: 'pp4', alertId: 'a3', alertNo: 'ALT20260304003',
    deviceName: '洗碗机 DW-M9000', area: '清洗区',
    alertType: '用水异常', level: 'info',
    reportTime: '2026-03-04 08:15:00', currentStatus: 'resolved',
    progress: 100, assignee: '王工', estimatedResolve: '2026-03-04 10:30:00',
    steps: [
      { id: 's1', name: '告警触发', status: 'completed', time: '08:15:00', operator: '系统', remark: '用水量突增' },
      { id: 's2', name: '告警通知', status: 'completed', time: '08:15:02', operator: '系统' },
      { id: 's3', name: '工单创建', status: 'completed', time: '08:20:00', operator: '李运营' },
      { id: 's4', name: '工单分配', status: 'completed', time: '08:25:00', operator: '张管理员' },
      { id: 's5', name: '现场处理', status: 'completed', time: '08:40:00', operator: '王工', remark: '检查后发现是早高峰餐具量增加导致，属正常现象' },
      { id: 's6', name: '验证确认', status: 'completed', time: '09:00:00', operator: '王工' },
      { id: 's7', name: '工单关闭', status: 'completed', time: '10:30:00', operator: '李运营' },
    ],
  },
];

// ── Alert Records View ─────────────────────────────────────────────────────────
function AlertRecordsView() {
  const [search, setSearch] = useState('');
  const [levelFilter, setLevelFilter] = useState<'all' | 'info' | 'warning' | 'critical'>('all');
  const [statusFilter, setStatusFilter] = useState<'all' | 'pending' | 'processing' | 'resolved' | 'ignored'>('all');
  const [selectedRecord, setSelectedRecord] = useState<AlertRecord | null>(null);

  const levelInfo = {
    info: { label: '信息', color: '#3b82f6', bgColor: 'rgba(59, 130, 246, 0.15)', icon: <Info className="w-4 h-4" /> },
    warning: { label: '警告', color: '#f59e0b', bgColor: 'rgba(245, 158, 11, 0.15)', icon: <AlertCircle className="w-4 h-4" /> },
    critical: { label: '严重', color: '#ef4444', bgColor: 'rgba(239, 68, 68, 0.15)', icon: <AlertTriangle className="w-4 h-4" /> },
  };

  const statusInfo = {
    pending: { label: '待处理', color: '#f59e0b', icon: <Clock className="w-4 h-4" /> },
    processing: { label: '处理中', color: '#3b82f6', icon: <Play className="w-4 h-4" /> },
    resolved: { label: '已解决', color: '#10b981', icon: <CheckCircle className="w-4 h-4" /> },
    ignored: { label: '已忽略', color: '#6b7280', icon: <XCircle className="w-4 h-4" /> },
  };

  const typeIcons: Record<string, JSX.Element> = {
    '温度异常': <Thermometer className="w-4 h-4" />,
    '烟雾异常': <AlertTriangle className="w-4 h-4" />,
    '用水异常': <Zap className="w-4 h-4" />,
    '用气异常': <Zap className="w-4 h-4" />,
    '设备故障': <Cpu className="w-4 h-4" />,
  };

  const filteredRecords = mockAlertRecords.filter((r) => {
    const matchSearch = r.deviceName.toLowerCase().includes(search.toLowerCase()) || r.alertType.toLowerCase().includes(search.toLowerCase());
    const matchLevel = levelFilter === 'all' || r.level === levelFilter;
    const matchStatus = statusFilter === 'all' || r.status === statusFilter;
    return matchSearch && matchLevel && matchStatus;
  });

  const stats = {
    total: mockAlertRecords.length,
    pending: mockAlertRecords.filter((r) => r.status === 'pending').length,
    processing: mockAlertRecords.filter((r) => r.status === 'processing').length,
    resolved: mockAlertRecords.filter((r) => r.status === 'resolved').length,
  };

  return (
    <div className="space-y-4">
      {/* Stats */}
      <div className="grid grid-cols-4 gap-3">
        {[
          { label: '总告警数', value: stats.total, color: '#00c3ff', icon: <Bell className="w-5 h-5" /> },
          { label: '待处理', value: stats.pending, color: '#f59e0b', icon: <Clock className="w-5 h-5" /> },
          { label: '处理中', value: stats.processing, color: '#3b82f6', icon: <Play className="w-5 h-5" /> },
          { label: '已解决', value: stats.resolved, color: '#10b981', icon: <CheckCircle className="w-5 h-5" /> },
        ].map((stat, i) => (
          <motion.div
            key={i}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: i * 0.05 }}
            className="p-4 rounded-lg"
            style={{ background: 'rgba(255,255,255,0.02)', border: `1px solid ${stat.color}30` }}
          >
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm" style={{ color: '#5a7a90' }}>{stat.label}</span>
              <span style={{ color: stat.color }}>{stat.icon}</span>
            </div>
            <p className="text-2xl font-bold" style={{ color: stat.color }}>{stat.value}</p>
          </motion.div>
        ))}
      </div>

      {/* Filters */}
      <div className="flex items-center gap-3">
        <div className="flex-1 relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: '#3a6e9a' }} />
          <input
            type="text"
            placeholder="搜索设备或告警类型..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-10 pr-3 py-2 rounded-lg text-sm"
            style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0,195,255,0.2)', color: '#c0d8e8' }}
          />
        </div>
        <select
          value={levelFilter}
          onChange={(e) => setLevelFilter(e.target.value as typeof levelFilter)}
          className="px-3 py-2 rounded-lg text-sm [&>option]:bg-gray-800"
          style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0,195,255,0.2)', color: '#c0d8e8' }}
        >
          <option value="all">所有级别</option>
          <option value="info">信息</option>
          <option value="warning">警告</option>
          <option value="critical">严重</option>
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}
          className="px-3 py-2 rounded-lg text-sm [&>option]:bg-gray-800"
          style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0,195,255,0.2)', color: '#c0d8e8' }}
        >
          <option value="all">所有状态</option>
          <option value="pending">待处理</option>
          <option value="processing">处理中</option>
          <option value="resolved">已解决</option>
          <option value="ignored">已忽略</option>
        </select>
        <button className="px-4 py-2 rounded-lg flex items-center gap-2 text-sm hover:brightness-110 transition-all"
          style={{ background: 'linear-gradient(90deg, rgba(0,195,255,0.15), rgba(0,195,255,0.08))', border: '1px solid rgba(0,195,255,0.3)', color: '#00c3ff' }}>
          <Download className="w-4 h-4" />
          导出
        </button>
      </div>

      {/* Table */}
      <div className="rounded-xl overflow-hidden" style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(0,195,255,0.15)' }}>
        <table className="w-full">
          <thead>
            <tr style={{ background: 'rgba(0,195,255,0.08)', borderBottom: '1px solid rgba(0,195,255,0.2)' }}>
              {['告警编号', '设备名称', '区域', '告警类型', '级别', '状态', '触发时间', '操作'].map((h) => (
                <th key={h} className="px-4 py-3 text-left text-xs font-medium" style={{ color: '#5a7a90' }}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filteredRecords.map((rec, idx) => (
              <motion.tr
                key={rec.id}
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                transition={{ delay: idx * 0.03 }}
                className="hover:brightness-110 transition-all cursor-pointer"
                style={{ borderBottom: '1px solid rgba(255,255,255,0.05)' }}
                onClick={() => setSelectedRecord(rec)}
              >
                <td className="px-4 py-3">
                  <span className="text-xs font-mono" style={{ color: '#00c3ff' }}>{rec.alertNo}</span>
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-2">
                    <Cpu className="w-4 h-4" style={{ color: '#3a6e9a' }} />
                    <span className="text-sm" style={{ color: '#c0d8e8' }}>{rec.deviceName}</span>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-1">
                    <MapPin className="w-3 h-3" style={{ color: '#3a6e9a' }} />
                    <span className="text-xs" style={{ color: '#5a7a90' }}>{rec.area}</span>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-2">
                    {typeIcons[rec.alertType] || <Bell className="w-4 h-4" />}
                    <span className="text-sm" style={{ color: '#c0d8e8' }}>{rec.alertType}</span>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <div className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium"
                    style={{ background: levelInfo[rec.level].bgColor, color: levelInfo[rec.level].color }}>
                    {levelInfo[rec.level].icon}
                    {levelInfo[rec.level].label}
                  </div>
                </td>
                <td className="px-4 py-3">
                  <div className="inline-flex items-center gap-1 text-xs" style={{ color: statusInfo[rec.status].color }}>
                    {statusInfo[rec.status].icon}
                    {statusInfo[rec.status].label}
                  </div>
                </td>
                <td className="px-4 py-3 text-xs" style={{ color: '#5a7a90' }}>{rec.triggerTime}</td>
                <td className="px-4 py-3">
                  <button className="p-1 hover:brightness-125 transition-all" style={{ color: '#00c3ff' }}>
                    <Eye className="w-4 h-4" />
                  </button>
                </td>
              </motion.tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Detail Modal */}
      <AnimatePresence>
        {selectedRecord && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setSelectedRecord(null)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-2xl w-full p-6"
            >
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-xl font-bold text-white">告警详情</h3>
                <button onClick={() => setSelectedRecord(null)} className="p-1 hover:bg-white/10 rounded">
                  <XCircle className="w-5 h-5 text-gray-400" />
                </button>
              </div>
              <div className="space-y-4 text-sm">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <p className="text-gray-400 mb-1">告警编号</p>
                    <p className="text-white font-mono">{selectedRecord.alertNo}</p>
                  </div>
                  <div>
                    <p className="text-gray-400 mb-1">触发时间</p>
                    <p className="text-white">{selectedRecord.triggerTime}</p>
                  </div>
                  <div>
                    <p className="text-gray-400 mb-1">设备名称</p>
                    <p className="text-white">{selectedRecord.deviceName}</p>
                  </div>
                  <div>
                    <p className="text-gray-400 mb-1">所属区域</p>
                    <p className="text-white">{selectedRecord.area}</p>
                  </div>
                  <div>
                    <p className="text-gray-400 mb-1">告警类型</p>
                    <p className="text-white">{selectedRecord.alertType}</p>
                  </div>
                  <div>
                    <p className="text-gray-400 mb-1">告警级别</p>
                    <span className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium"
                      style={{ background: levelInfo[selectedRecord.level].bgColor, color: levelInfo[selectedRecord.level].color }}>
                      {levelInfo[selectedRecord.level].icon}
                      {levelInfo[selectedRecord.level].label}
                    </span>
                  </div>
                </div>
                <div>
                  <p className="text-gray-400 mb-1">告警描述</p>
                  <p className="text-white">{selectedRecord.description}</p>
                </div>
                {selectedRecord.value && (
                  <div>
                    <p className="text-gray-400 mb-1">当前数值</p>
                    <p className="text-white">{selectedRecord.value}</p>
                  </div>
                )}
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ── Process Progress View ──────────────────────────────────────────────────────
function ProcessProgressView() {
  const [selectedProgress, setSelectedProgress] = useState<ProcessProgress | null>(mockProcessProgress[0]);
  const [statusFilter, setStatusFilter] = useState<'all' | 'pending' | 'processing' | 'resolved'>('all');

  const levelInfo = {
    info: { label: '信息', color: '#3b82f6' },
    warning: { label: '警告', color: '#f59e0b' },
    critical: { label: '严重', color: '#ef4444' },
  };

  const statusInfo = {
    pending: { label: '待处理', color: '#f59e0b' },
    processing: { label: '处理中', color: '#3b82f6' },
    resolved: { label: '已解决', color: '#10b981' },
    ignored: { label: '已忽略', color: '#6b7280' },
  };

  const filteredProgress = mockProcessProgress.filter((p) => statusFilter === 'all' || p.currentStatus === statusFilter);

  return (
    <div className="space-y-4">
      {/* Filters */}
      <div className="flex items-center gap-3">
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}
          className="px-3 py-2 rounded-lg text-sm [&>option]:bg-gray-800"
          style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0,195,255,0.2)', color: '#c0d8e8' }}
        >
          <option value="all">所有状态</option>
          <option value="pending">待处理</option>
          <option value="processing">处理中</option>
          <option value="resolved">已解决</option>
        </select>
        <div className="flex-1"></div>
        <button className="p-2 hover:brightness-110 transition-all rounded-lg" style={{ color: '#00c3ff' }}>
          <RefreshCw className="w-4 h-4" />
        </button>
      </div>

      {/* Content */}
      <div className="grid grid-cols-3 gap-4">
        {/* List */}
        <div className="col-span-1 space-y-2 max-h-[600px] overflow-y-auto pr-2">
          {filteredProgress.map((p) => (
            <motion.div
              key={p.id}
              whileHover={{ scale: 1.02 }}
              onClick={() => setSelectedProgress(p)}
              className={`p-4 rounded-lg cursor-pointer transition-all ${selectedProgress?.id === p.id ? 'brightness-125' : ''}`}
              style={{
                background: selectedProgress?.id === p.id ? 'rgba(0,195,255,0.15)' : 'rgba(255,255,255,0.02)',
                border: `1px solid ${selectedProgress?.id === p.id ? 'rgba(0,195,255,0.4)' : 'rgba(255,255,255,0.05)'}`,
              }}
            >
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs font-mono" style={{ color: '#00c3ff' }}>{p.alertNo}</span>
                <div className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs"
                  style={{ background: `${levelInfo[p.level].color}20`, color: levelInfo[p.level].color }}>
                  {levelInfo[p.level].label}
                </div>
              </div>
              <p className="text-sm font-medium mb-1" style={{ color: '#c0d8e8' }}>{p.deviceName}</p>
              <div className="flex items-center gap-2 text-xs mb-2" style={{ color: '#5a7a90' }}>
                <MapPin className="w-3 h-3" />
                <span>{p.area}</span>
              </div>
              <div className="flex items-center gap-2">
                <div className="flex-1 h-1.5 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.1)' }}>
                  <div className="h-full rounded-full transition-all" style={{ width: `${p.progress}%`, background: statusInfo[p.currentStatus].color }}></div>
                </div>
                <span className="text-xs font-medium" style={{ color: statusInfo[p.currentStatus].color }}>{p.progress}%</span>
              </div>
            </motion.div>
          ))}
        </div>

        {/* Detail */}
        <div className="col-span-2">
          {selectedProgress ? (
            <motion.div
              key={selectedProgress.id}
              initial={{ opacity: 0, x: 10 }}
              animate={{ opacity: 1, x: 0 }}
              className="p-6 rounded-xl"
              style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(0,195,255,0.15)' }}
            >
              {/* Header */}
              <div className="flex items-start justify-between mb-6">
                <div>
                  <div className="flex items-center gap-3 mb-2">
                    <h3 className="text-xl font-bold" style={{ color: '#c0d8e8' }}>{selectedProgress.deviceName}</h3>
                    <div className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium"
                      style={{ background: `${levelInfo[selectedProgress.level].color}20`, color: levelInfo[selectedProgress.level].color }}>
                      {levelInfo[selectedProgress.level].label}
                    </div>
                  </div>
                  <div className="flex items-center gap-4 text-sm" style={{ color: '#5a7a90' }}>
                    <span className="flex items-center gap-1">
                      <MapPin className="w-3 h-3" />
                      {selectedProgress.area}
                    </span>
                    <span>{selectedProgress.alertType}</span>
                  </div>
                </div>
                <div className="text-right">
                  <p className="text-xs mb-1" style={{ color: '#5a7a90' }}>告警编号</p>
                  <p className="text-sm font-mono" style={{ color: '#00c3ff' }}>{selectedProgress.alertNo}</p>
                </div>
              </div>

              {/* Progress Bar */}
              <div className="mb-6">
                <div className="flex items-center justify-between mb-2">
                  <span className="text-sm font-medium" style={{ color: '#c0d8e8' }}>处理进度</span>
                  <span className="text-sm font-bold" style={{ color: statusInfo[selectedProgress.currentStatus].color }}>{selectedProgress.progress}%</span>
                </div>
                <div className="h-2 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.1)' }}>
                  <div className="h-full rounded-full transition-all" style={{ width: `${selectedProgress.progress}%`, background: statusInfo[selectedProgress.currentStatus].color }}></div>
                </div>
              </div>

              {/* Info Grid */}
              <div className="grid grid-cols-3 gap-4 mb-6 p-4 rounded-lg" style={{ background: 'rgba(255,255,255,0.03)' }}>
                <div>
                  <p className="text-xs mb-1" style={{ color: '#5a7a90' }}>上报时间</p>
                  <p className="text-sm font-medium" style={{ color: '#c0d8e8' }}>{selectedProgress.reportTime}</p>
                </div>
                <div>
                  <p className="text-xs mb-1" style={{ color: '#5a7a90' }}>当前状态</p>
                  <p className="text-sm font-medium" style={{ color: statusInfo[selectedProgress.currentStatus].color }}>{statusInfo[selectedProgress.currentStatus].label}</p>
                </div>
                <div>
                  <p className="text-xs mb-1" style={{ color: '#5a7a90' }}>处理人员</p>
                  <p className="text-sm font-medium" style={{ color: '#c0d8e8' }}>{selectedProgress.assignee || '-'}</p>
                </div>
              </div>

              {/* Steps */}
              <div>
                <h4 className="text-sm font-medium mb-4" style={{ color: '#c0d8e8' }}>处理流程</h4>
                <div className="space-y-3">
                  {selectedProgress.steps.map((step, idx) => {
                    const isCompleted = step.status === 'completed';
                    const isCurrent = step.status === 'current';
                    const isPending = step.status === 'pending';
                    return (
                      <div key={step.id} className="flex gap-3">
                        <div className="flex flex-col items-center">
                          <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold transition-all ${
                            isCompleted ? 'bg-green-500/20 text-green-400 border-2 border-green-500' :
                            isCurrent ? 'bg-blue-500/20 text-blue-400 border-2 border-blue-500' :
                            'bg-gray-500/10 text-gray-500 border-2 border-gray-600'
                          }`}>
                            {isCompleted ? <CheckCircle2 className="w-4 h-4" /> : idx + 1}
                          </div>
                          {idx < selectedProgress.steps.length - 1 && (
                            <div className={`w-0.5 h-8 ${isCompleted ? 'bg-green-500/30' : 'bg-gray-600/30'}`} />
                          )}
                        </div>
                        <div className="flex-1 pb-3">
                          <p className={`text-sm font-medium mb-1 ${isCurrent ? 'text-blue-400' : isCompleted ? 'text-white' : 'text-gray-500'}`}>
                            {step.name}
                          </p>
                          {(step.time || step.operator) && (
                            <p className="text-xs flex items-center gap-2" style={{ color: '#5a7a90' }}>
                              <Calendar className="inline w-3 h-3 mr-1" />{step.time}
                              {step.operator && <> · <User className="inline w-3 h-3 mx-1" />{step.operator}</>}
                            </p>
                          )}
                          {step.remark && (
                            <p className="text-xs mt-1 px-2 py-1 rounded" style={{ background: 'rgba(255,255,255,0.03)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.06)' }}>
                              {step.remark}
                            </p>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            </motion.div>
          ) : (
            <div className="flex items-center justify-center h-64 rounded-xl" style={{ border: '1px dashed rgba(0,195,255,0.2)' }}>
              <div className="text-center">
                <Info className="w-10 h-10 mx-auto mb-2" style={{ color: '#2a4a60' }} />
                <p style={{ color: '#3a6e9a' }}>请从左侧选择一条记录查看详情</p>
              </div>
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
      {/* Page Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl flex items-center justify-center" style={{ background: 'rgba(239, 68, 68, 0.15)', border: '1px solid rgba(239, 68, 68, 0.3)' }}>
            <AlertTriangle className="w-5 h-5" style={{ color: '#ef4444' }} />
          </div>
          <div>
            <h1 className="text-xl font-bold" style={{ color: '#c0d8e8' }}>告警中心</h1>
            <p className="text-sm" style={{ color: '#3a6e9a' }}>设备告警信息监控与处理进度跟踪</p>
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-2 p-1 rounded-lg" style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0,195,255,0.15)' }}>
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id as typeof activeTab)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg transition-all flex-1"
            style={
              activeTab === tab.id
                ? { background: 'linear-gradient(90deg, rgba(0,195,255,0.25), rgba(0,195,255,0.15))', color: '#00c3ff', border: '1px solid rgba(0,195,255,0.4)' }
                : { color: '#5a7a90' }
            }
          >
            {tab.icon}
            <span className="text-sm font-medium">{tab.label}</span>
          </button>
        ))}
      </div>

      {/* Content */}
      <AnimatePresence mode="wait">
        <motion.div
          key={activeTab}
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -10 }}
          transition={{ duration: 0.2 }}
        >
          {activeTab === 'alerts' ? <AlertRecordsView /> : <ProcessProgressView />}
        </motion.div>
      </AnimatePresence>
    </div>
  );
}
