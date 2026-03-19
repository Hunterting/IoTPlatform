import { motion } from 'motion/react';
import { AlertTriangle, Info, CheckCircle, X } from 'lucide-react';

interface Alert {
  id: string;
  type: 'warning' | 'info' | 'success';
  title: string;
  message: string;
  time: string;
}

interface AlertsListProps {
  alerts: Alert[];
  onDismiss: (id: string) => void;
}

export function AlertsList({ alerts, onDismiss }: AlertsListProps) {
  const alertStyles = {
    warning: {
      bg: 'from-amber-500/20 to-orange-500/10 border-amber-500/30',
      icon: <AlertTriangle className="w-5 h-5 text-amber-400" />,
      iconBg: 'bg-amber-500/20',
    },
    info: {
      bg: 'from-blue-500/20 to-cyan-500/10 border-blue-500/30',
      icon: <Info className="w-5 h-5 text-blue-400" />,
      iconBg: 'bg-blue-500/20',
    },
    success: {
      bg: 'from-green-500/20 to-emerald-500/10 border-green-500/30',
      icon: <CheckCircle className="w-5 h-5 text-green-400" />,
      iconBg: 'bg-green-500/20',
    },
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      className="rounded-xl bg-gradient-to-br from-gray-800/50 to-gray-900/50 border border-gray-700/50 backdrop-blur-sm p-6"
    >
      <h3 className="text-xl font-semibold text-white mb-4">智能提醒</h3>
      <div className="space-y-3">
        {alerts.map((alert) => {
          const style = alertStyles[alert.type];
          return (
            <motion.div
              key={alert.id}
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: 20 }}
              className={`relative rounded-lg bg-gradient-to-br ${style.bg} border p-4 transition-all hover:shadow-lg`}
            >
              <button
                onClick={() => onDismiss(alert.id)}
                className="absolute top-3 right-3 p-1 hover:bg-white/10 rounded transition-colors"
              >
                <X className="w-4 h-4 text-gray-400" />
              </button>

              <div className="flex gap-3">
                <div className={`p-2 ${style.iconBg} rounded-lg h-fit`}>
                  {style.icon}
                </div>
                <div className="flex-1 pr-6">
                  <h4 className="font-medium text-white mb-1">{alert.title}</h4>
                  <p className="text-sm text-gray-300 mb-2">{alert.message}</p>
                  <span className="text-xs text-gray-400">{alert.time}</span>
                </div>
              </div>
            </motion.div>
          );
        })}
      </div>
    </motion.div>
  );
}
