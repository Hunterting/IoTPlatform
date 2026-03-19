import { motion } from 'motion/react';
import { AlertTriangle, CheckCircle, Server, Activity } from 'lucide-react';
import { DeviceItem } from '@/app/contexts/DeviceContext';
import { useEffect, useRef, useState } from 'react';

interface AreaMonitorCardProps {
  areaName: string;
  devices: DeviceItem[];
}

export function AreaMonitorCard({ areaName, devices }: AreaMonitorCardProps) {
  const total = devices.length;
  const normal = devices.filter((d) => d.status === 'online').length;
  const abnormalDevices = devices.filter((d) => d.status !== 'online');
  const abnormal = abnormalDevices.length;
  
  // Auto-scroll logic for the abnormal list
  const scrollRef = useRef<HTMLDivElement>(null);
  const [isHovered, setIsHovered] = useState(false);

  useEffect(() => {
    const scrollContainer = scrollRef.current;
    if (!scrollContainer || abnormalDevices.length <= 2 || isHovered) return;

    let animationFrameId: number;
    let scrollPos = scrollContainer.scrollTop;
    
    const scroll = () => {
      scrollPos += 0.5; // Speed
      // If we've scrolled past the content height (accounting for visible height), reset
      if (scrollPos >= scrollContainer.scrollHeight - scrollContainer.clientHeight) {
        scrollPos = 0;
      }
      scrollContainer.scrollTop = scrollPos;
      animationFrameId = requestAnimationFrame(scroll);
    };

    animationFrameId = requestAnimationFrame(scroll);

    return () => cancelAnimationFrame(animationFrameId);
  }, [abnormalDevices.length, isHovered]);

  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.95 }}
      animate={{ opacity: 1, scale: 1 }}
      className="bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl p-5 flex flex-col h-full"
    >
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-bold text-white truncate" title={areaName}>{areaName}</h3>
        <div className={`px-2 py-0.5 rounded text-xs font-medium ${abnormal > 0 ? 'bg-red-500/20 text-red-400' : 'bg-green-500/20 text-green-400'}`}>
          {abnormal > 0 ? '异常' : '正常'}
        </div>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-3 gap-2 mb-4">
        <div className="bg-white/5 rounded-lg p-2 text-center">
          <div className="text-xs text-gray-400 mb-1">总数</div>
          <div className="text-lg font-bold text-white">{total}</div>
        </div>
        <div className="bg-green-500/10 rounded-lg p-2 text-center">
          <div className="text-xs text-green-400 mb-1">正常</div>
          <div className="text-lg font-bold text-green-400">{normal}</div>
        </div>
        <div className="bg-red-500/10 rounded-lg p-2 text-center">
          <div className="text-xs text-red-400 mb-1">异常</div>
          <div className="text-lg font-bold text-red-400">{abnormal}</div>
        </div>
      </div>

      {/* Abnormal List */}
      <div className="flex-1 min-h-[100px] flex flex-col">
        <div className="text-xs font-medium text-gray-400 mb-2 flex items-center gap-1">
          <Activity className="w-3 h-3" />
          异常设备监控
        </div>
        
        <div 
            ref={scrollRef}
            className="flex-1 overflow-y-auto pr-1 custom-scrollbar relative bg-black/20 rounded-lg p-2"
            style={{ maxHeight: '120px' }}
            onMouseEnter={() => setIsHovered(true)}
            onMouseLeave={() => setIsHovered(false)}
        >
          {abnormalDevices.length > 0 ? (
            <div className="space-y-2">
              {abnormalDevices.map((device) => (
                <div key={device.id} className="flex items-start gap-2 p-2 bg-red-500/5 border border-red-500/10 rounded text-sm">
                    <AlertTriangle className="w-4 h-4 text-red-500 shrink-0 mt-0.5" />
                    <div>
                        <div className="text-red-200 font-medium text-xs">{device.name}</div>
                        <div className="text-red-400/70 text-[10px]">{device.status === 'offline' ? '设备离线' : '运行警告'}</div>
                    </div>
                </div>
              ))}
              {/* Duplicate for infinite scroll illusion if needed, but simple reset is safer for now */}
            </div>
          ) : (
            <div className="h-full flex flex-col items-center justify-center text-gray-500 gap-2">
              <CheckCircle className="w-8 h-8 opacity-20" />
              <span className="text-xs">所有设备运行正常</span>
            </div>
          )}
        </div>
      </div>
    </motion.div>
  );
}
