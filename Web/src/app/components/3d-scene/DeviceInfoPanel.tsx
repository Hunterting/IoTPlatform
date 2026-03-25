/**
 * 设备信息面板组件
 * 显示设备关键信息,支持设备操作(开关机等)
 */

import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { DeviceItem } from '@/app/contexts/DeviceContext';
import { Cpu, Power, Settings, MapPin, Calendar, AlertTriangle, CheckCircle } from 'lucide-react';

interface DeviceInfoPanelProps {
  device: DeviceItem;
  onClose: () => void;
  onTogglePower?: (deviceId: string, status: 'online' | 'offline') => void;
  className?: string;
}

export function DeviceInfoPanel({
  device,
  onClose,
  onTogglePower,
  className = '',
}: DeviceInfoPanelProps) {
  const [isOn, setIsOn] = useState(device.status === 'online');
  const [isProcessing, setIsProcessing] = useState(false);

  // 处理设备开关
  const handleTogglePower = async () => {
    if (isProcessing) return;
    
    setIsProcessing(true);
    
    try {
      const newStatus: 'online' | 'offline' = isOn ? 'offline' : 'online';
      setIsOn(!isOn);
      onTogglePower?.(device.id, newStatus);
    } catch (error) {
      console.error('设备操作失败:', error);
      // 回滚状态
      setIsOn(isOn);
    } finally {
      setIsProcessing(false);
    }
  };

  // 获取状态样式
  const getStatusStyle = () => {
    if (device.status === 'online') {
      return {
        color: 'text-green-400',
        bgColor: 'bg-green-500/20',
        borderColor: 'border-green-500/30',
        icon: CheckCircle,
        label: '在线',
      };
    } else if (device.status === 'warning') {
      return {
        color: 'text-yellow-400',
        bgColor: 'bg-yellow-500/20',
        borderColor: 'border-yellow-500/30',
        icon: AlertTriangle,
        label: '警告',
      };
    } else {
      return {
        color: 'text-red-400',
        bgColor: 'bg-red-500/20',
        borderColor: 'border-red-500/30',
        icon: AlertTriangle,
        label: '离线',
      };
    }
  };

  const statusStyle = getStatusStyle();
  const StatusIcon = statusStyle.icon;

  // 能耗类型标签
  const energyTypeLabels: Record<string, { label: string; color: string }> = {
    electric: { label: '电', color: 'text-yellow-400 bg-yellow-500/20' },
    gas: { label: '气', color: 'text-orange-400 bg-orange-500/20' },
    water: { label: '水', color: 'text-blue-400 bg-blue-500/20' },
  };

  return (
    <motion.div
      initial={{ x: 320, opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ x: 320, opacity: 0 }}
      transition={{ type: 'spring', damping: 25, stiffness: 200 }}
      className={`absolute right-4 top-4 bottom-4 w-80 bg-black/95 backdrop-blur-xl border ${statusStyle.borderColor} rounded-lg overflow-hidden flex flex-col ${className}`}
    >
      {/* 标题栏 */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-white/10">
        <h3 className="text-lg font-bold text-white">设备详情</h3>
        <button
          onClick={onClose}
          className="p-1.5 hover:bg-white/10 rounded-lg transition-colors group"
        >
          <Settings className="w-4 h-4 text-gray-400 group-hover:text-white transition-colors" />
        </button>
      </div>

      {/* 内容区域 - 可滚动 */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4 scrollbar-thin scrollbar-thumb-white/10 scrollbar-track-transparent">
        {/* 设备图标 */}
        <div className="flex items-center justify-center p-4 bg-gradient-to-br from-cyan-500/20 to-cyan-500/5 rounded-xl border border-cyan-500/20">
          <motion.div
            animate={{
              scale: [1, 1.05, 1],
              opacity: [1, 0.8, 1],
            }}
            transition={{
              duration: 2,
              repeat: Infinity,
              ease: 'easeInOut',
            }}
          >
            <Cpu className="w-16 h-16 text-cyan-400" />
          </motion.div>
        </div>

        {/* 设备名称 */}
        <div className="text-center">
          <h4 className="text-xl font-bold text-white mb-1">{device.name}</h4>
          <p className="text-sm text-gray-400">{device.category}</p>
        </div>

        {/* 设备状态 */}
        <div className={`flex items-center justify-center gap-2 px-4 py-3 rounded-lg ${statusStyle.bgColor} border ${statusStyle.borderColor}`}>
          <StatusIcon className={`w-5 h-5 ${statusStyle.color}`} />
          <span className={`font-medium ${statusStyle.color}`}>{statusStyle.label}</span>
        </div>

        {/* 设备详细信息 */}
        <div className="space-y-3 pt-2 border-t border-white/10">
          {/* 设备型号 */}
          <InfoItem label="设备型号" value={device.model} />

          {/* 序列号 */}
          <InfoItem label="序列号" value={device.serialNumber} mono />

          {/* 安装位置 */}
          <InfoItem
            label="安装位置"
            value={device.location}
            icon={MapPin}
          />

          {/* 所属区域 */}
          <InfoItem label="所属区域" value={device.area} />

          {/* 安装日期 */}
          <InfoItem
            label="安装日期"
            value={device.installDate}
            icon={Calendar}
          />

          {/* 最后维护 */}
          <InfoItem
            label="最后维护"
            value={device.lastMaintenance || '未记录'}
            icon={Calendar}
          />

          {/* 供应商 */}
          {device.supplier && (
            <InfoItem label="供应商" value={device.supplier} />
          )}

          {/* 功率 */}
          {device.power && (
            <InfoItem
              label="额定功率"
              value={`${device.power}W`}
            />
          )}

          {/* 电压 */}
          {device.voltage && (
            <InfoItem
              label="工作电压"
              value={device.voltage}
            />
          )}

          {/* 能耗类型 */}
          <div>
            <p className="text-xs text-gray-400 mb-2">能耗类型</p>
            <div className="flex flex-wrap gap-2">
              {device.energyType.map((type) => {
                const typeInfo = energyTypeLabels[type];
                return (
                  <span
                    key={type}
                    className={`px-3 py-1.5 text-xs font-medium rounded-lg ${typeInfo.color}`}
                  >
                    {typeInfo.label}
                  </span>
                );
              })}
            </div>
          </div>

          {/* 表计安装状态 */}
          <div>
            <p className="text-xs text-gray-400 mb-2">表计安装</p>
            <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg ${
              device.meterInstalled
                ? 'text-green-400 bg-green-500/20'
                : 'text-gray-400 bg-gray-500/20'
            }`}>
              {device.meterInstalled ? (
                <>
                  <CheckCircle className="w-3.5 h-3.5" />
                  已安装
                </>
              ) : (
                <>
                  <AlertTriangle className="w-3.5 h-3.5" />
                  未安装
                </>
              )}
            </span>
          </div>

          {/* 保修信息 */}
          {device.warrantyDate && (
            <InfoItem
              label="保修截止"
              value={device.warrantyDate}
              icon={Calendar}
            />
          )}
        </div>
      </div>

      {/* 操作按钮区域 */}
      <div className="p-4 border-t border-white/10 space-y-2">
        <motion.button
          whileHover={{ scale: 1.02 }}
          whileTap={{ scale: 0.98 }}
          onClick={handleTogglePower}
          disabled={isProcessing}
          className={`w-full flex items-center justify-center gap-2 px-4 py-3 rounded-lg font-medium transition-all ${
            isOn
              ? 'bg-red-500 hover:bg-red-600 text-white disabled:bg-red-500/50'
              : 'bg-green-500 hover:bg-green-600 text-white disabled:bg-green-500/50'
          } ${isProcessing ? 'cursor-not-allowed opacity-50' : ''}`}
        >
          <Power className="w-5 h-5" />
          {isProcessing ? '处理中...' : isOn ? '关闭设备' : '启动设备'}
        </motion.button>

        <motion.button
          whileHover={{ scale: 1.02 }}
          whileTap={{ scale: 0.98 }}
          className="w-full flex items-center justify-center gap-2 px-4 py-3 rounded-lg font-medium bg-cyan-500/20 hover:bg-cyan-500/30 text-cyan-400 transition-all"
        >
          <Settings className="w-5 h-5" />
          更多设置
        </motion.button>
      </div>
    </motion.div>
  );
}

// 信息项子组件
interface InfoItemProps {
  label: string;
  value: string;
  icon?: any;
  mono?: boolean;
}

function InfoItem({ label, value, icon: Icon, mono }: InfoItemProps) {
  return (
    <div>
      <p className="text-xs text-gray-400 mb-1.5 flex items-center gap-1.5">
        {Icon && <Icon className="w-3.5 h-3.5" />}
        {label}
      </p>
      <p className={`text-sm text-white truncate ${mono ? 'font-mono' : ''}`}>
        {value || '-'}
      </p>
    </div>
  );
}