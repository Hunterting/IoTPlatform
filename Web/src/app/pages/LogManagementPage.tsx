import { useState, useMemo, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  LogIn, Activity, AlertTriangle, ClipboardCheck,
  Search, Filter, Download, RefreshCw, ChevronDown,
  CheckCircle, XCircle, Clock, Eye, User, Monitor,
  Smartphone, Globe, Shield, Cpu, Thermometer, Zap,
  TrendingUp, TrendingDown, BarChart3, Calendar,
  ChevronLeft, ChevronRight, MapPin, Bell, AlertCircle,
  ArrowRight, Info, Play, Pause, CheckCircle2, Loader2,
} from 'lucide-react';
import { PageType } from '@/app/components/Sidebar';
import { useAuth } from '@/app/contexts/AuthContext';
import { SHARED_ALERT_RECORDS, AlertRecord as SharedAlertRecord } from '@/app/config/sharedMockData';
import { logApi } from '@/app/services/api';
import { 
  adaptOperationLogFromBackend, 
  adaptLoginLogFromBackend,
  formatLogTime,
  getLoginStatusInfo,
  getOperationStatusInfo,
  parseUserAgent,
  parseIpLocation,
  calculateLoginStats,
  calculateOperationStats
} from '@/app/services/adapters/logAdapter';
import { OperationLogDto, LoginLogDto, MODULE_COLORS, LOG_STATUS } from '@/app/services/api/types/log.types';

interface LogManagementPageProps {
  activePage: PageType;
}

// ── Types ──────────────────────────────────────────────────────────────────────
// LoginLog 和 OperationLog 类型现在从 log.types.ts 导入

// AlertRecord is imported from sharedMockData as SharedAlertRecord
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
      { id: 's5', name: '现场处理', status: 'completed', time: '08:30:00', operator: '李工', remark: '清洁过滤网，检查风机叶片' },
      { id: 's6', name: '验证确认', status: 'completed', time: '09:10:00', operator: '李工', remark: '设备恢复正常，传感器数值正常' },
      { id: 's7', name: '工单关闭', status: 'completed', time: '09:12:00', operator: '张管理员', remark: '确认完成，工单关闭' },
    ],
  },
  {
    id: 'pp4', alertId: 'a3', alertNo: 'ALT20260304003',
    deviceName: '智能烤箱 CVS-800', area: '烘焙间',
    alertType: '温度异常', level: 'warning',
    reportTime: '2026-03-04 08:10:22', currentStatus: 'pending',
    progress: 15, estimatedResolve: undefined,
    steps: [
      { id: 's1', name: '告警触发', status: 'completed', time: '08:10:22', operator: '系统', remark: '烤箱温度超过设定阈值 5°C' },
      { id: 's2', name: '告警通知', status: 'completed', time: '08:10:25', operator: '系统', remark: '通知已发送' },
      { id: 's3', name: '工单创建', status: 'current', remark: '待创建工单' },
      { id: 's4', name: '工单分配', status: 'pending' },
      { id: 's5', name: '现场处理', status: 'pending' },
      { id: 's6', name: '验证确认', status: 'pending' },
      { id: 's7', name: '工单关闭', status: 'pending' },
    ],
  },
];

// ── Sub Components ─────────────────────────────────────────────────────────────
const Badge = ({ children, color }: { children: React.ReactNode; color: string }) => (
  <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium" style={{ background: `${color}22`, color, border: `1px solid ${color}44` }}>
    {children}
  </span>
);

const statCard = (icon: React.ReactNode, label: string, value: string, color: string, sub?: string) => (
  <div className="rounded-xl p-4 flex items-center gap-4" style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.08)' }}>
    <div className="w-12 h-12 rounded-xl flex items-center justify-center flex-shrink-0" style={{ background: `${color}18`, border: `1px solid ${color}30` }}>
      <span style={{ color }}>{icon}</span>
    </div>
    <div>
      <p className="text-xs" style={{ color: '#5a7a90' }}>{label}</p>
      <p className="text-2xl font-bold mt-0.5" style={{ color: '#e0f0ff' }}>{value}</p>
      {sub && <p className="text-xs mt-0.5" style={{ color: '#3a6e9a' }}>{sub}</p>}
    </div>
  </div>
);

// ── Login Logs View ────────────────────────────────────────────────────────────
function LoginLogsView() {
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'success' | 'failed' | 'locked'>('all');
  const [currentPage, setCurrentPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const pageSize = 8;
  
  const [loginLogs, setLoginLogs] = useState<LoginLogDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);

  // 加载登录日志
  const loadLoginLogs = useCallback(async (page: number, keyword?: string) => {
    setLoading(true);
    try {
      const filters: any = {};
      if (keyword) {
        filters.keyword = keyword;
      }
      if (statusFilter !== 'all') {
        filters.status = statusFilter;
      }
      
      const response = await logApi.getLoginLogs(page, pageSize, filters);
      
      if (response.data?.code === 200 && response.data?.data) {
        const adaptedLogs = response.data.data.items.map((item: any) => adaptLoginLogFromBackend(item));
        setLoginLogs(adaptedLogs);
        setTotalCount(response.data.data.totalCount || 0);
      }
    } catch (error) {
      console.error('加载登录日志失败:', error);
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => {
    loadLoginLogs(currentPage, search);
  }, [loadLoginLogs, currentPage]);

  const filtered = useMemo(() => {
    if (!search && statusFilter === 'all') {
      return loginLogs;
    }
    return loginLogs.filter(l => {
      const matchSearch = !search || l.userName.includes(search) || l.ip.includes(search);
      const matchStatus = statusFilter === 'all' || l.status === statusFilter;
      return matchSearch && matchStatus;
    });
  }, [loginLogs, search, statusFilter]);

  const totalPages = Math.ceil(totalCount / pageSize);

  // 计算统计数据
  const stats = calculateLoginStats(loginLogs);

  const handleSearch = (value: string) => {
    setSearch(value);
    setCurrentPage(1);
  };

  const handleStatusFilter = (status: 'all' | 'success' | 'failed' | 'locked') => {
    setStatusFilter(status);
    setCurrentPage(1);
  };

  return (
    <div className="space-y-5">
      {/* Stats */}
      <div className="grid grid-cols-4 gap-4">
        {statCard(<LogIn className="w-5 h-5" />, '登录总次数', String(stats.total), '#00c3ff', '今日')}
        {statCard(<CheckCircle className="w-5 h-5" />, '登录成功', String(stats.success), '#10b981', `成功率 ${stats.total > 0 ? Math.round(stats.success / stats.total * 100) : 0}%`)}
        {statCard(<XCircle className="w-5 h-5" />, '登录失败', String(stats.failed), '#f59e0b', '密码错误')}
        {statCard(<Shield className="w-5 h-5" />, '账号锁定', String(stats.locked), '#ef4444', '安全事件')}
      </div>

      {/* Filters */}
      <div className="flex items-center gap-3">
        <div className="flex-1 relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: '#3a6e9a' }} />
          <input
            className="w-full pl-9 pr-4 py-2 rounded-lg text-sm outline-none"
            style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(0,195,255,0.15)', color: '#c0d8e8' }}
            placeholder="搜索用户名 / IP..."
            value={search}
            onChange={e => handleSearch(e.target.value)}
          />
        </div>
        {(['all', 'success', 'failed', 'locked'] as const).map(s => (
          <button
            key={s}
            onClick={() => handleStatusFilter(s)}
            className="px-4 py-2 rounded-lg text-sm transition-all"
            style={statusFilter === s ? { background: 'rgba(0,195,255,0.15)', color: '#00c3ff', border: '1px solid rgba(0,195,255,0.3)' } : { background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}
          >
            {s === 'all' ? '全部' : s === 'success' ? '成功' : s === 'failed' ? '失败' : '锁定'}
          </button>
        ))}
        <button 
          className="flex items-center gap-2 px-4 py-2 rounded-lg text-sm transition-all" 
          style={{ background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}
          onClick={() => loadLoginLogs(currentPage, search)}
        >
          <RefreshCw className="w-4 h-4" />刷新
        </button>
      </div>

      {/* Table */}
      <div className="rounded-xl overflow-hidden" style={{ border: '1px solid rgba(0,195,255,0.12)' }}>
        <table className="w-full text-sm">
          <thead>
            <tr style={{ background: 'rgba(0,195,255,0.06)', borderBottom: '1px solid rgba(0,195,255,0.12)' }}>
              {['用户名', '角色', '登录时间', 'IP地址', '设备/浏览器', '状态'].map(h => (
                <th key={h} className="text-left px-4 py-3 font-medium" style={{ color: '#3a6e9a' }}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={6} className="px-4 py-12 text-center">
                  <Loader2 className="w-8 h-8 animate-spin mx-auto text-blue-400" />
                  <p className="mt-2" style={{ color: '#5a7a90' }}>加载中...</p>
                </td>
              </tr>
            ) : filtered.length > 0 ? filtered.map((log, i) => {
              const si = getLoginStatusInfo(log.status);
              const { device, browser } = parseUserAgent(log.userAgent);
              const location = parseIpLocation(log.ip);
              return (
                <motion.tr
                  key={log.id}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ delay: i * 0.03 }}
                  className="border-b transition-colors hover:bg-white/5"
                  style={{ borderColor: 'rgba(0,195,255,0.06)' }}
                >
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-2">
                      <div className="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold" style={{ background: 'rgba(0,195,255,0.15)', color: '#00c3ff' }}>
                        {log.userName[0]}
                      </div>
                      <span style={{ color: '#c0d8e8' }}>{log.userName}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3" style={{ color: '#6a8ca8' }}>{log.role}</td>
                  <td className="px-4 py-3" style={{ color: '#6a8ca8' }}>{formatLogTime(log.loginTime)}</td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1.5">
                      <Globe className="w-3.5 h-3.5" style={{ color: '#3a6e9a' }} />
                      <span style={{ color: '#6a8ca8' }}>{log.ip}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <div style={{ color: '#5a7a90' }}>
                      <div className="flex items-center gap-1.5">
                        <Monitor className="w-3 h-3" />
                        <span className="text-xs">{device}</span>
                      </div>
                      <div className="flex items-center gap-1.5 mt-0.5">
                        <Globe className="w-3 h-3" />
                        <span className="text-xs">{browser}</span>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <div>
                      <Badge color={si.color}>
                        {si.label === '成功' ? <CheckCircle className="w-3 h-3" /> : si.label === '失败' ? <XCircle className="w-3 h-3" /> : <Shield className="w-3 h-3" />}
                        {si.label}
                      </Badge>
                      {log.failReason && (
                        <p className="text-xs mt-1" style={{ color: '#ef4444' }}>{log.failReason}</p>
                      )}
                    </div>
                  </td>
                </motion.tr>
              );
            }) : (
              <tr>
                <td colSpan={6} className="px-4 py-12 text-center" style={{ color: '#5a7a90' }}>
                  暂无登录日志记录
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div className="flex items-center justify-between text-sm" style={{ color: '#5a7a90' }}>
        <span>共 {totalCount} 条记录</span>
        <div className="flex items-center gap-2">
          <button onClick={() => setCurrentPage(p => Math.max(1, p - 1))} disabled={currentPage === 1 || loading} className="p-1.5 rounded transition-colors hover:bg-white/5 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
          <span style={{ color: '#c0d8e8' }}>{currentPage} / {totalPages || 1}</span>
          <button onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))} disabled={currentPage >= totalPages || loading} className="p-1.5 rounded transition-colors hover:bg-white/5 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
        </div>
      </div>
    </div>
  );
}

// ── Operation Logs View ────────────────────────────────────────────────────────
function OperationLogsView() {
  const [search, setSearch] = useState('');
  const [moduleFilter, setModuleFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'success' | 'failed'>('all');
  const [currentPage, setCurrentPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const pageSize = 8;
  
  const [operationLogs, setOperationLogs] = useState<OperationLogDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [modules, setModules] = useState<string[]>([]);

  // 加载操作日志
  const loadOperationLogs = useCallback(async (page: number, keyword?: string) => {
    setLoading(true);
    try {
      const filters: any = {};
      if (keyword) {
        filters.keyword = keyword;
      }
      if (moduleFilter) {
        filters.module = moduleFilter;
      }
      
      const response = await logApi.getOperationLogs(page, pageSize, filters);
      
      if (response.data?.code === 200 && response.data?.data) {
        const adaptedLogs = response.data.data.items.map((item: any) => adaptOperationLogFromBackend(item));
        setOperationLogs(adaptedLogs);
        setTotalCount(response.data.data.totalCount || 0);
        
        // 提取所有模块用于筛选
        const allModules = [...new Set(adaptedLogs.map(l => l.module).filter(Boolean))];
        setModules(allModules);
      }
    } catch (error) {
      console.error('加载操作日志失败:', error);
    } finally {
      setLoading(false);
    }
  }, [moduleFilter]);

  useEffect(() => {
    loadOperationLogs(currentPage, search);
  }, [loadOperationLogs, currentPage]);

  const filtered = useMemo(() => {
    return operationLogs.filter(l => {
      const matchSearch = !search || l.userName.includes(search) || l.action.includes(search) || l.target.includes(search);
      const matchStatus = statusFilter === 'all' || l.status === statusFilter;
      return matchSearch && matchStatus;
    });
  }, [operationLogs, search, statusFilter]);

  const totalPages = Math.ceil(totalCount / pageSize);

  // 计算统计数据
  const stats = calculateOperationStats(operationLogs);

  const handleSearch = (value: string) => {
    setSearch(value);
    setCurrentPage(1);
  };

  const handleModuleFilter = (value: string) => {
    setModuleFilter(value);
    setCurrentPage(1);
  };

  const handleStatusFilter = (status: 'all' | 'success' | 'failed') => {
    setStatusFilter(status);
    setCurrentPage(1);
  };

  return (
    <div className="space-y-5">
      {/* Stats */}
      <div className="grid grid-cols-4 gap-4">
        {statCard(<Activity className="w-5 h-5" />, '操作总次数', String(stats.total), '#00c3ff', '今日')}
        {statCard(<CheckCircle className="w-5 h-5" />, '成功操作', String(stats.success), '#10b981')}
        {statCard(<XCircle className="w-5 h-5" />, '失败操作', String(stats.failed), '#ef4444')}
        {statCard(<User className="w-5 h-5" />, '活跃用户', String(stats.uniqueUsers), '#a855f7', '今日')}
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-1 min-w-48 relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: '#3a6e9a' }} />
          <input
            className="w-full pl-9 pr-4 py-2 rounded-lg text-sm outline-none"
            style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(0,195,255,0.15)', color: '#c0d8e8' }}
            placeholder="搜索用户名 / 操作 / 对象..."
            value={search}
            onChange={e => handleSearch(e.target.value)}
          />
        </div>
        <select
          className="px-3 py-2 rounded-lg text-sm outline-none"
          style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(0,195,255,0.15)', color: '#c0d8e8' }}
          value={moduleFilter}
          onChange={e => handleModuleFilter(e.target.value)}
        >
          <option value="" style={{ background: '#020e25' }}>全部模块</option>
          {modules.map(m => <option key={m} value={m} style={{ background: '#020e25' }}>{m}</option>)}
        </select>
        {(['all', 'success', 'failed'] as const).map(s => (
          <button
            key={s}
            onClick={() => handleStatusFilter(s)}
            className="px-4 py-2 rounded-lg text-sm transition-all"
            style={statusFilter === s ? { background: 'rgba(0,195,255,0.15)', color: '#00c3ff', border: '1px solid rgba(0,195,255,0.3)' } : { background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}
          >
            {s === 'all' ? '全部' : s === 'success' ? '成功' : '失败'}
          </button>
        ))}
        <button 
          className="flex items-center gap-2 px-4 py-2 rounded-lg text-sm transition-all" 
          style={{ background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}
          onClick={() => loadOperationLogs(currentPage, search)}
        >
          <RefreshCw className="w-4 h-4" />刷新
        </button>
      </div>

      {/* Table */}
      <div className="rounded-xl overflow-hidden" style={{ border: '1px solid rgba(0,195,255,0.12)' }}>
        <table className="w-full text-sm">
          <thead>
            <tr style={{ background: 'rgba(0,195,255,0.06)', borderBottom: '1px solid rgba(0,195,255,0.12)' }}>
              {['操作时间', '操作人', '所属模块', '操作类型', '操作对象', '操作详情', 'IP地址', '耗时', '状态'].map(h => (
                <th key={h} className="text-left px-4 py-3 font-medium" style={{ color: '#3a6e9a' }}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={9} className="px-4 py-12 text-center">
                  <Loader2 className="w-8 h-8 animate-spin mx-auto text-blue-400" />
                  <p className="mt-2" style={{ color: '#5a7a90' }}>加载中...</p>
                </td>
              </tr>
            ) : filtered.length > 0 ? filtered.map((log, i) => (
              <motion.tr
                key={log.id}
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                transition={{ delay: i * 0.03 }}
                className="border-b transition-colors hover:bg-white/5"
                style={{ borderColor: 'rgba(0,195,255,0.06)' }}
              >
                <td className="px-4 py-3" style={{ color: '#6a8ca8' }}>{formatLogTime(log.time)}</td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-2">
                    <div className="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold" style={{ background: 'rgba(0,195,255,0.15)', color: '#00c3ff' }}>{log.userName[0]}</div>
                    <div>
                      <p style={{ color: '#c0d8e8' }}>{log.userName}</p>
                      <p className="text-xs" style={{ color: '#3a6e9a' }}>{log.role}</p>
                    </div>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <Badge color={MODULE_COLORS[log.module] || '#6a8ca8'}>{log.module}</Badge>
                </td>
                <td className="px-4 py-3" style={{ color: '#c0d8e8' }}>{log.action}</td>
                <td className="px-4 py-3" style={{ color: '#6a8ca8' }}>{log.target}</td>
                <td className="px-4 py-3 max-w-48">
                  <p className="text-xs truncate" style={{ color: '#5a7a90' }} title={log.detail}>{log.detail}</p>
                </td>
                <td className="px-4 py-3" style={{ color: '#5a7a90' }}>{log.ip}</td>
                <td className="px-4 py-3" style={{ color: '#5a7a90' }}>{log.duration}ms</td>
                <td className="px-4 py-3">
                  <Badge color={log.status === 'success' ? '#10b981' : '#ef4444'}>
                    {log.status === 'success' ? <CheckCircle className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                    {log.status === 'success' ? '成功' : '失败'}
                  </Badge>
                </td>
              </motion.tr>
            )) : (
              <tr>
                <td colSpan={9} className="px-4 py-12 text-center" style={{ color: '#5a7a90' }}>
                  暂无操作日志记录
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div className="flex items-center justify-between text-sm" style={{ color: '#5a7a90' }}>
        <span>共 {totalCount} 条记录</span>
        <div className="flex items-center gap-2">
          <button onClick={() => setCurrentPage(p => Math.max(1, p - 1))} disabled={currentPage === 1 || loading} className="p-1.5 rounded hover:bg-white/5 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
          <span style={{ color: '#c0d8e8' }}>{currentPage} / {totalPages || 1}</span>
          <button onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))} disabled={currentPage >= totalPages || loading} className="p-1.5 rounded hover:bg-white/5 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
        </div>
      </div>
    </div>
  );
}

// ── Alert Records View ─────────────────────────────────────────────────────────
function AlertRecordsView() {
  const [search, setSearch] = useState('');
  const [levelFilter, setLevelFilter] = useState<'all' | 'info' | 'warning' | 'critical'>('all');
  const [statusFilter, setStatusFilter] = useState<'all' | 'pending' | 'processing' | 'resolved' | 'ignored'>('all');
  const [selectedRecord, setSelectedRecord] = useState<AlertRecord | null>(null);

  const levelInfo = {
    info: { label: '信息', color: '#3b82f6' },
    warning: { label: '警告', color: '#f59e0b' },
    critical: { label: '严重', color: '#ef4444' },
  };
  const statusInfo = {
    pending: { label: '待处理', color: '#f59e0b' },
    processing: { label: '处理中', color: '#00c3ff' },
    resolved: { label: '已解决', color: '#10b981' },
    ignored: { label: '已忽略', color: '#6a8ca8' },
  };
  const alertTypeInfo = {
    temperature: { label: '温度异常', icon: <Thermometer className="w-4 h-4" />, color: '#ef4444' },
    pressure: { label: '压力异常', icon: <Zap className="w-4 h-4" />, color: '#f59e0b' },
    offline: { label: '设备离线', icon: <Cpu className="w-4 h-4" />, color: '#6a8ca8' },
    power: { label: '功率异常', icon: <Zap className="w-4 h-4" />, color: '#a855f7' },
    smoke: { label: '烟雾告警', icon: <AlertTriangle className="w-4 h-4" />, color: '#f97316' },
    door: { label: '门体告警', icon: <AlertCircle className="w-4 h-4" />, color: '#3b82f6' },
  };

  const filtered = useMemo(() => {
    return mockAlertRecords.filter(r => {
      const matchSearch = !search || r.deviceName.includes(search) || r.alertNo.includes(search) || r.area.includes(search);
      const matchLevel = levelFilter === 'all' || r.level === levelFilter;
      const matchStatus = statusFilter === 'all' || r.status === statusFilter;
      return matchSearch && matchLevel && matchStatus;
    });
  }, [search, levelFilter, statusFilter]);

  const total = mockAlertRecords.length;
  const pending = mockAlertRecords.filter(r => r.status === 'pending').length;
  const processing = mockAlertRecords.filter(r => r.status === 'processing').length;
  const critical = mockAlertRecords.filter(r => r.level === 'critical').length;

  return (
    <div className="space-y-5">
      {/* Stats */}
      <div className="grid grid-cols-4 gap-4">
        {statCard(<Bell className="w-5 h-5" />, '预警总计', String(total), '#00c3ff')}
        {statCard(<AlertTriangle className="w-5 h-5" />, '严重告警', String(critical), '#ef4444', '需优先处理')}
        {statCard(<Clock className="w-5 h-5" />, '待处理', String(pending), '#f59e0b')}
        {statCard(<Activity className="w-5 h-5" />, '处理中', String(processing), '#00c3ff')}
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-1 min-w-48 relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: '#3a6e9a' }} />
          <input
            className="w-full pl-9 pr-4 py-2 rounded-lg text-sm outline-none"
            style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(0,195,255,0.15)', color: '#c0d8e8' }}
            placeholder="搜索设备名称 / 预警编号 / 区域..."
            value={search}
            onChange={e => setSearch(e.target.value)}
          />
        </div>
        <div className="flex items-center gap-1">
          {(['all', 'critical', 'warning', 'info'] as const).map(l => (
            <button key={l} onClick={() => setLevelFilter(l)} className="px-3 py-2 rounded-lg text-xs transition-all"
              style={levelFilter === l ? { background: 'rgba(0,195,255,0.15)', color: '#00c3ff', border: '1px solid rgba(0,195,255,0.3)' } : { background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}>
              {l === 'all' ? '全部级别' : levelInfo[l].label}
            </button>
          ))}
        </div>
        <div className="flex items-center gap-1">
          {(['all', 'pending', 'processing', 'resolved', 'ignored'] as const).map(s => (
            <button key={s} onClick={() => setStatusFilter(s)} className="px-3 py-2 rounded-lg text-xs transition-all"
              style={statusFilter === s ? { background: 'rgba(0,195,255,0.15)', color: '#00c3ff', border: '1px solid rgba(0,195,255,0.3)' } : { background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}>
              {s === 'all' ? '全部状态' : statusInfo[s].label}
            </button>
          ))}
        </div>
      </div>

      {/* Cards Grid */}
      <div className="grid grid-cols-1 gap-3">
        {filtered.map((record, i) => {
          const ti = alertTypeInfo[record.alertType];
          const li = levelInfo[record.level];
          const si = statusInfo[record.status];
          return (
            <motion.div
              key={record.id}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: i * 0.04 }}
              className="rounded-xl p-4 cursor-pointer transition-all hover:border-cyan-500/30"
              style={{ background: 'rgba(255,255,255,0.02)', border: `1px solid rgba(255,255,255,0.08)` }}
              onClick={() => setSelectedRecord(selectedRecord?.id === record.id ? null : record)}
            >
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0" style={{ background: `${ti.color}15`, border: `1px solid ${ti.color}30` }}>
                    <span style={{ color: ti.color }}>{ti.icon}</span>
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium" style={{ color: '#c0d8e8' }}>{record.deviceName}</span>
                      <Badge color={li.color}>{li.label}</Badge>
                      <Badge color={si.color}>{si.label}</Badge>
                    </div>
                    <div className="flex items-center gap-4 mt-1 text-xs" style={{ color: '#5a7a90' }}>
                      <span>{record.alertNo}</span>
                      <span className="flex items-center gap-1"><MapPin className="w-3 h-3" />{record.area}</span>
                      <span className="flex items-center gap-1"><Calendar className="w-3 h-3" />{record.alertTime}</span>
                    </div>
                  </div>
                </div>
                <div className="text-right">
                  <div className="flex items-center gap-4">
                    <div>
                      <p className="text-xs" style={{ color: '#5a7a90' }}>当前值</p>
                      <p className="text-sm font-bold" style={{ color: li.color }}>{record.value}</p>
                    </div>
                    <div>
                      <p className="text-xs" style={{ color: '#5a7a90' }}>阈值</p>
                      <p className="text-sm" style={{ color: '#6a8ca8' }}>{record.threshold}</p>
                    </div>
                    {record.assignee && (
                      <div>
                        <p className="text-xs" style={{ color: '#5a7a90' }}>处理人</p>
                        <p className="text-sm" style={{ color: '#c0d8e8' }}>{record.assignee}</p>
                      </div>
                    )}
                    <ChevronDown className="w-4 h-4 transition-transform" style={{ color: '#3a6e9a', transform: selectedRecord?.id === record.id ? 'rotate(180deg)' : 'rotate(0deg)' }} />
                  </div>
                </div>
              </div>

              {/* Expanded detail */}
              <AnimatePresence>
                {selectedRecord?.id === record.id && (
                  <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
                    <div className="mt-4 pt-4 grid grid-cols-3 gap-4" style={{ borderTop: '1px solid rgba(0,195,255,0.1)' }}>
                      <div>
                        <p className="text-xs mb-1" style={{ color: '#3a6e9a' }}>设备编号</p>
                        <p className="text-sm" style={{ color: '#c0d8e8' }}>{record.deviceCode}</p>
                      </div>
                      <div>
                        <p className="text-xs mb-1" style={{ color: '#3a6e9a' }}>告警类型</p>
                        <p className="text-sm" style={{ color: '#c0d8e8' }}>{ti.label}</p>
                      </div>
                      {record.resolveTime && (
                        <div>
                          <p className="text-xs mb-1" style={{ color: '#3a6e9a' }}>解决时间</p>
                          <p className="text-sm" style={{ color: '#10b981' }}>{record.resolveTime}</p>
                        </div>
                      )}
                      {record.remark && (
                        <div className="col-span-3">
                          <p className="text-xs mb-1" style={{ color: '#3a6e9a' }}>备注说明</p>
                          <p className="text-sm" style={{ color: '#6a8ca8' }}>{record.remark}</p>
                        </div>
                      )}
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </motion.div>
          );
        })}
      </div>
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
    processing: { label: '处理中', color: '#00c3ff' },
    resolved: { label: '已解决', color: '#10b981' },
    ignored: { label: '已忽略', color: '#6a8ca8' },
  };

  const filtered = mockProcessProgress.filter(p => statusFilter === 'all' || p.currentStatus === statusFilter);

  const getStepIcon = (status: string) => {
    if (status === 'completed') return <CheckCircle2 className="w-5 h-5" style={{ color: '#10b981' }} />;
    if (status === 'current') return <Play className="w-5 h-5" style={{ color: '#00c3ff' }} />;
    return <Pause className="w-5 h-5" style={{ color: '#3a6e9a' }} />;
  };

  return (
    <div className="space-y-5">
      {/* Stats */}
      <div className="grid grid-cols-4 gap-4">
        {statCard(<ClipboardCheck className="w-5 h-5" />, '处理总计', String(mockProcessProgress.length), '#00c3ff')}
        {statCard(<Clock className="w-5 h-5" />, '待处理', String(mockProcessProgress.filter(p => p.currentStatus === 'pending').length), '#f59e0b')}
        {statCard(<Activity className="w-5 h-5" />, '处理中', String(mockProcessProgress.filter(p => p.currentStatus === 'processing').length), '#00c3ff')}
        {statCard(<CheckCircle className="w-5 h-5" />, '已解决', String(mockProcessProgress.filter(p => p.currentStatus === 'resolved').length), '#10b981', `解决率 ${Math.round(mockProcessProgress.filter(p => p.currentStatus === 'resolved').length / mockProcessProgress.length * 100)}%`)}
      </div>

      <div className="flex gap-5">
        {/* Left: List */}
        <div className="w-96 flex-shrink-0 space-y-3">
          {/* Filter */}
          <div className="flex items-center gap-1">
            {(['all', 'pending', 'processing', 'resolved'] as const).map(s => (
              <button key={s} onClick={() => setStatusFilter(s)} className="px-3 py-1.5 rounded-lg text-xs transition-all"
                style={statusFilter === s ? { background: 'rgba(0,195,255,0.15)', color: '#00c3ff', border: '1px solid rgba(0,195,255,0.3)' } : { background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}>
                {s === 'all' ? '全部' : statusInfo[s as keyof typeof statusInfo]?.label || s}
              </button>
            ))}
          </div>

          {filtered.map(progress => {
            const li = levelInfo[progress.level];
            const si = statusInfo[progress.currentStatus];
            const isSelected = selectedProgress?.id === progress.id;
            return (
              <motion.div
                key={progress.id}
                whileHover={{ scale: 1.01 }}
                onClick={() => setSelectedProgress(progress)}
                className="rounded-xl p-4 cursor-pointer transition-all"
                style={{
                  background: isSelected ? 'rgba(0,195,255,0.08)' : 'rgba(255,255,255,0.02)',
                  border: `1px solid ${isSelected ? 'rgba(0,195,255,0.3)' : 'rgba(255,255,255,0.08)'}`,
                }}
              >
                <div className="flex items-center justify-between mb-3">
                  <div className="flex items-center gap-2">
                    <Badge color={li.color}>{li.label}</Badge>
                    <Badge color={si.color}>{si.label}</Badge>
                  </div>
                  <span className="text-xs" style={{ color: '#3a6e9a' }}>{progress.alertNo}</span>
                </div>
                <p className="text-sm font-medium mb-1" style={{ color: '#c0d8e8' }}>{progress.deviceName}</p>
                <p className="text-xs mb-3" style={{ color: '#5a7a90' }}>
                  <MapPin className="inline w-3 h-3 mr-1" />{progress.area} · {progress.alertType}
                </p>
                {/* Progress bar */}
                <div className="flex items-center gap-2">
                  <div className="flex-1 h-1.5 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.08)' }}>
                    <motion.div
                      initial={{ width: 0 }}
                      animate={{ width: `${progress.progress}%` }}
                      transition={{ duration: 0.8, ease: 'easeOut' }}
                      className="h-full rounded-full"
                      style={{ background: progress.progress === 100 ? '#10b981' : progress.progress > 50 ? '#00c3ff' : '#f59e0b' }}
                    />
                  </div>
                  <span className="text-xs" style={{ color: '#5a7a90' }}>{progress.progress}%</span>
                </div>
                {progress.assignee && (
                  <p className="text-xs mt-2" style={{ color: '#3a6e9a' }}>
                    <User className="inline w-3 h-3 mr-1" />处理人：{progress.assignee}
                  </p>
                )}
              </motion.div>
            );
          })}
        </div>

        {/* Right: Detail */}
        <div className="flex-1">
          {selectedProgress ? (
            <motion.div
              key={selectedProgress.id}
              initial={{ opacity: 0, x: 10 }}
              animate={{ opacity: 1, x: 0 }}
              className="rounded-xl p-5"
              style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(0,195,255,0.12)' }}
            >
              {/* Header */}
              <div className="flex items-start justify-between mb-5">
                <div>
                  <div className="flex items-center gap-2 mb-1">
                    <Badge color={levelInfo[selectedProgress.level].color}>{levelInfo[selectedProgress.level].label}</Badge>
                    <Badge color={statusInfo[selectedProgress.currentStatus].color}>{statusInfo[selectedProgress.currentStatus].label}</Badge>
                    <span className="text-xs" style={{ color: '#3a6e9a' }}>{selectedProgress.alertNo}</span>
                  </div>
                  <h3 className="text-lg font-bold" style={{ color: '#c0d8e8' }}>{selectedProgress.deviceName}</h3>
                  <p className="text-sm mt-0.5" style={{ color: '#5a7a90' }}>
                    <MapPin className="inline w-3.5 h-3.5 mr-1" />{selectedProgress.area} · {selectedProgress.alertType}
                  </p>
                </div>
                <div className="text-right text-sm">
                  <p style={{ color: '#5a7a90' }}>上报时间</p>
                  <p style={{ color: '#c0d8e8' }}>{selectedProgress.reportTime}</p>
                  {selectedProgress.estimatedResolve && (
                    <>
                      <p className="mt-2" style={{ color: '#5a7a90' }}>预计完成</p>
                      <p style={{ color: '#f59e0b' }}>{selectedProgress.estimatedResolve}</p>
                    </>
                  )}
                </div>
              </div>

              {/* Progress bar */}
              <div className="mb-6">
                <div className="flex items-center justify-between mb-2 text-sm">
                  <span style={{ color: '#5a7a90' }}>处理进度</span>
                  <span className="font-bold" style={{ color: selectedProgress.progress === 100 ? '#10b981' : '#00c3ff' }}>{selectedProgress.progress}%</span>
                </div>
                <div className="h-2 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.08)' }}>
                  <motion.div
                    initial={{ width: 0 }}
                    animate={{ width: `${selectedProgress.progress}%` }}
                    transition={{ duration: 1, ease: 'easeOut' }}
                    className="h-full rounded-full"
                    style={{ background: selectedProgress.progress === 100 ? 'linear-gradient(90deg, #10b981, #34d399)' : 'linear-gradient(90deg, #0057b8, #00c3ff)' }}
                  />
                </div>
              </div>

              {/* Steps timeline */}
              <div className="space-y-0">
                {selectedProgress.steps.map((step, i) => {
                  const isLast = i === selectedProgress.steps.length - 1;
                  return (
                    <div key={step.id} className="flex gap-3">
                      {/* Icon & line */}
                      <div className="flex flex-col items-center">
                        <div className={`w-8 h-8 rounded-full flex items-center justify-center flex-shrink-0 ${step.status === 'completed' ? 'bg-emerald-500/10 border border-emerald-500/30' : step.status === 'current' ? 'bg-cyan-500/10 border border-cyan-500/40' : 'bg-white/5 border border-white/10'}`}>
                          {getStepIcon(step.status)}
                        </div>
                        {!isLast && (
                          <div className="w-0.5 flex-1 my-1" style={{ background: step.status === 'completed' ? 'rgba(16,185,129,0.3)' : 'rgba(255,255,255,0.06)' }} />
                        )}
                      </div>
                      {/* Content */}
                      <div className={`pb-4 ${isLast ? '' : ''}`}>
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className={`text-sm font-medium ${step.status === 'completed' ? 'text-emerald-400' : step.status === 'current' ? 'text-cyan-400' : ''}`} style={step.status === 'pending' ? { color: '#3a6e9a' } : {}}>
                            {step.name}
                          </span>
                          {step.status === 'current' && (
                            <span className="text-xs px-1.5 py-0.5 rounded" style={{ background: 'rgba(0,195,255,0.15)', color: '#00c3ff' }}>进行中</span>
                          )}
                        </div>
                        {step.time && (
                          <p className="text-xs" style={{ color: '#3a6e9a' }}>
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
export function LogManagementPage({ activePage }: LogManagementPageProps) {
  const pageConfig = {
    'logs-login': { title: '登录日志', desc: '记录所有用户登录与退出行为', icon: <LogIn className="w-5 h-5" />, color: '#00c3ff' },
    'logs-operation': { title: '操作日志', desc: '记录用户在系统中的操作行为', icon: <Activity className="w-5 h-5" />, color: '#a855f7' },
    'logs-alerts': { title: '设备预警记录', desc: '汇总设备告警信息与处理状态', icon: <AlertTriangle className="w-5 h-5" />, color: '#f59e0b' },
    'logs-progress': { title: '处理进度', desc: '跟踪预警工单的处理全流程', icon: <ClipboardCheck className="w-5 h-5" />, color: '#10b981' },
  };

  const config = pageConfig[activePage as keyof typeof pageConfig] || pageConfig['logs-login'];

  const renderContent = () => {
    switch (activePage) {
      case 'logs-login': return <LoginLogsView />;
      case 'logs-operation': return <OperationLogsView />;
      case 'logs-alerts': return <AlertRecordsView />;
      case 'logs-progress': return <ProcessProgressView />;
      default: return <LoginLogsView />;
    }
  };

  return (
    <div className="p-6 space-y-5 min-h-full" style={{ background: 'linear-gradient(180deg, rgba(2,14,37,0) 0%, rgba(1,11,29,0) 100%)' }}>
      {/* Page Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl flex items-center justify-center" style={{ background: `${config.color}18`, border: `1px solid ${config.color}30` }}>
            <span style={{ color: config.color }}>{config.icon}</span>
          </div>
          <div>
            <h1 className="text-xl font-bold" style={{ color: '#c0d8e8' }}>{config.title}</h1>
            <p className="text-sm" style={{ color: '#3a6e9a' }}>{config.desc}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button className="flex items-center gap-2 px-4 py-2 rounded-lg text-sm transition-all hover:bg-white/5" style={{ background: 'rgba(255,255,255,0.04)', color: '#5a7a90', border: '1px solid rgba(255,255,255,0.08)' }}>
            <RefreshCw className="w-4 h-4" />刷新
          </button>
          <div className="flex items-center gap-2 px-3 py-2 rounded-lg text-sm" style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(0,195,255,0.15)', color: '#6a8ca8' }}>
            <Calendar className="w-4 h-4" />
            <span>2026-03-04</span>
          </div>
        </div>
      </div>

      {/* Page Content */}
      <AnimatePresence mode="wait">
        <motion.div
          key={activePage}
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -8 }}
          transition={{ duration: 0.2 }}
        >
          {renderContent()}
        </motion.div>
      </AnimatePresence>
    </div>
  );
}
