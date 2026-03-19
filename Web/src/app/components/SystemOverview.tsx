import { motion } from 'motion/react';
import { Cpu, AlertTriangle, CheckCircle, Activity } from 'lucide-react';

export interface SystemStats {
  devicesOnline: number;
  totalDevices: number;
  abnormalCount: number;
  normalCount: number;
  uptime: string;
}

interface SystemOverviewProps {
  stats: SystemStats;
}

export function SystemOverview({ stats }: SystemOverviewProps) {
  return (
    <div className="grid grid-cols-4 gap-4">
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.1 }}
        className="rounded-xl bg-gradient-to-br from-blue-500/20 to-cyan-500/10 border border-blue-500/30 backdrop-blur-sm p-6"
      >
        <div className="flex items-center justify-between mb-4">
          <div className="p-3 bg-blue-500/20 rounded-lg">
            <Cpu className="w-6 h-6 text-blue-400" />
          </div>
          <div className="text-right">
            <p className="text-3xl font-bold text-white">{stats.totalDevices}</p>
            <p className="text-sm text-gray-400">在线: {stats.devicesOnline}</p>
          </div>
        </div>
        <p className="text-sm text-gray-300">设备总数</p>
      </motion.div>

      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.2 }}
        className="rounded-xl bg-gradient-to-br from-red-500/20 to-orange-500/10 border border-red-500/30 backdrop-blur-sm p-6"
      >
        <div className="flex items-center justify-between mb-4">
          <div className="p-3 bg-red-500/20 rounded-lg">
            <AlertTriangle className="w-6 h-6 text-red-400" />
          </div>
          <div className="text-right">
            <p className="text-3xl font-bold text-red-400">{stats.abnormalCount}</p>
          </div>
        </div>
        <p className="text-sm text-gray-300">异常设备</p>
      </motion.div>

      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.3 }}
        className="rounded-xl bg-gradient-to-br from-green-500/20 to-emerald-500/10 border border-green-500/30 backdrop-blur-sm p-6"
      >
        <div className="flex items-center justify-between mb-4">
          <div className="p-3 bg-green-500/20 rounded-lg">
            <CheckCircle className="w-6 h-6 text-green-400" />
          </div>
          <div className="text-right">
            <p className="text-3xl font-bold text-green-400">{stats.normalCount}</p>
          </div>
        </div>
        <p className="text-sm text-gray-300">正常设备</p>
      </motion.div>

      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.4 }}
        className="rounded-xl bg-gradient-to-br from-amber-500/20 to-orange-500/10 border border-amber-500/30 backdrop-blur-sm p-6"
      >
        <div className="flex items-center justify-between mb-4">
          <div className="p-3 bg-amber-500/20 rounded-lg">
            <Activity className="w-6 h-6 text-amber-400" />
          </div>
          <div className="text-right">
            <p className="text-2xl font-bold text-white">{stats.uptime}</p>
          </div>
        </div>
        <p className="text-sm text-gray-300">系统运行时间</p>
      </motion.div>
    </div>
  );
}
