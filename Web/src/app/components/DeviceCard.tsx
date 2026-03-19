import { Power, Thermometer, Clock, Zap, TrendingUp, TrendingDown } from 'lucide-react';
import { motion } from 'motion/react';

export interface Device {
  id: string;
  name: string;
  type: string;
  icon: React.ReactNode;
  status: 'online' | 'offline' | 'warning';
  isOn: boolean;
  temperature?: number;
  targetTemp?: number;
  humidity?: number;
  power: number;
  runtime: string;
  efficiency: number;
}

interface DeviceCardProps {
  device: Device;
  onToggle: (id: string) => void;
  onTempChange?: (id: string, temp: number) => void;
}

export function DeviceCard({ device, onToggle, onTempChange }: DeviceCardProps) {
  const statusColors = {
    online: 'from-green-500/20 to-green-600/10 border-green-500/30',
    offline: 'from-gray-500/20 to-gray-600/10 border-gray-500/30',
    warning: 'from-amber-500/20 to-amber-600/10 border-amber-500/30',
  };

  const statusDots = {
    online: 'bg-green-500',
    offline: 'bg-gray-500',
    warning: 'bg-amber-500',
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      className={`relative rounded-xl bg-gradient-to-br ${statusColors[device.status]} border backdrop-blur-sm p-6 transition-all hover:shadow-lg hover:scale-[1.02]`}
    >
      {/* Status Indicator */}
      <div className="absolute top-4 right-4 flex items-center gap-2">
        <span className={`w-2 h-2 rounded-full ${statusDots[device.status]} animate-pulse`} />
        <span className="text-xs text-gray-400 uppercase">{device.status}</span>
      </div>

      {/* Device Icon and Name */}
      <div className="flex items-start gap-4 mb-4">
        <div className="p-3 bg-white/5 rounded-lg">
          {device.icon}
        </div>
        <div>
          <h3 className="font-semibold text-lg text-white">{device.name}</h3>
          <p className="text-sm text-gray-400">{device.type}</p>
        </div>
      </div>

      {/* Temperature Control */}
      {device.temperature !== undefined && (
        <div className="mb-4 p-3 bg-white/5 rounded-lg">
          <div className="flex items-center justify-between mb-2">
            <div className="flex items-center gap-2">
              <Thermometer className="w-4 h-4 text-blue-400" />
              <span className="text-sm text-gray-300">当前温度</span>
            </div>
            <span className="text-xl font-semibold text-white">{device.temperature}°C</span>
          </div>
          {device.targetTemp && (
            <div className="flex items-center justify-between text-sm">
              <span className="text-gray-400">目标温度</span>
              <div className="flex items-center gap-2">
                <input
                  type="range"
                  min="0"
                  max="300"
                  value={device.targetTemp}
                  onChange={(e) => onTempChange?.(device.id, Number(e.target.value))}
                  className="w-20 h-1 bg-gray-600 rounded-lg appearance-none cursor-pointer"
                  disabled={!device.isOn}
                />
                <span className="text-gray-300 w-12">{device.targetTemp}°C</span>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Stats Grid */}
      <div className="grid grid-cols-3 gap-3 mb-4">
        <div className="p-2 bg-white/5 rounded-lg">
          <div className="flex items-center gap-1 mb-1">
            <Zap className="w-3 h-3 text-yellow-400" />
            <span className="text-xs text-gray-400">功率</span>
          </div>
          <p className="text-sm font-semibold text-white">{device.power}W</p>
        </div>
        <div className="p-2 bg-white/5 rounded-lg">
          <div className="flex items-center gap-1 mb-1">
            <Clock className="w-3 h-3 text-purple-400" />
            <span className="text-xs text-gray-400">运行</span>
          </div>
          <p className="text-sm font-semibold text-white">{device.runtime}</p>
        </div>
        <div className="p-2 bg-white/5 rounded-lg">
          <div className="flex items-center gap-1 mb-1">
            {device.efficiency >= 80 ? (
              <TrendingUp className="w-3 h-3 text-green-400" />
            ) : (
              <TrendingDown className="w-3 h-3 text-red-400" />
            )}
            <span className="text-xs text-gray-400">效率</span>
          </div>
          <p className="text-sm font-semibold text-white">{device.efficiency}%</p>
        </div>
      </div>

      {/* Control Button */}
      <button
        onClick={() => onToggle(device.id)}
        className={`w-full py-3 rounded-lg flex items-center justify-center gap-2 transition-all ${
          device.isOn
            ? 'bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700'
            : 'bg-gray-700 hover:bg-gray-600'
        }`}
      >
        <Power className="w-4 h-4" />
        <span className="font-medium">{device.isOn ? '关闭设备' : '开启设备'}</span>
      </button>
    </motion.div>
  );
}
