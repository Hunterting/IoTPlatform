import { useState, useEffect, useMemo } from 'react';
import { useAuth } from '@/app/contexts/AuthContext';
import { useDevices } from '@/app/contexts/DeviceContext';
import { useArea, Area } from '@/app/contexts/AreaContext';
import {
  LineChart, Line, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, PieChart, Pie, Cell, Legend, AreaChart, Area as RechartsArea
} from 'recharts';
import { Cloud, ChevronDown, User, LogOut } from 'lucide-react';
import { motion, AnimatePresence } from 'motion/react';
import {
  dashboardApi,
  type AlertDto,
  type AlertSummaryDto,
  type MonitoringDataDto,
} from '@/app/services/api/dashboardApi';

// ─── Real backend data helpers ───────────────────────────────────────────────
// 解析设备原始上报里被序列化成 JSON 字符串的字段（tasks / EMdata / slots）。
function tryParseJson(s: unknown): unknown | null {
  if (typeof s !== 'string') return null;
  const t = s.trim();
  if (!(t.startsWith('[') || t.startsWith('{'))) return null;
  try { return JSON.parse(t); } catch { return null; }
}
function firstArrayItem(s: unknown): Record<string, unknown> | null {
  const parsed = tryParseJson(s);
  if (Array.isArray(parsed) && parsed.length > 0 && parsed[0] && typeof parsed[0] === 'object') {
    return parsed[0] as Record<string, unknown>;
  }
  if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
    return parsed as Record<string, unknown>;
  }
  return null;
}
function toNum(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) return v;
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}
function fmtHHmm(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return '--:--';
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getHours())}:${p(d.getMinutes())}`;
}

interface AlertEventView {
  time: string;
  content: string;
  status: string;
  color: string;
}

const ALERT_TYPE_LABEL: Record<string, string> = {
  temperature: '温度异常',
  pressure: '压力异常',
  offline: '设备离线',
  power: '功率异常',
  smoke: '烟雾异常',
  door: '门体异常',
};

function toAlertEventView(alert: AlertDto): AlertEventView {
  let status = '待处理';
  if (alert.status === 'processing') status = '处理中';
  else if (alert.status === 'resolved') status = '已处理';
  else if (alert.status === 'ignored') status = '已忽略';

  let color = '#00c3ff';
  if (alert.level === 'critical') color = '#ff4444';
  else if (alert.level === 'warning' || status === '处理中') color = '#ffaa00';

  return {
    time: fmtHHmm(alert.alertTime),
    content: `${alert.deviceName} ${ALERT_TYPE_LABEL[alert.alertType] || alert.alertType || '异常'}`,
    status,
    color,
  };
}

/** 把真实监控记录按时间升序排列，构造三条曲线的数据点 */
interface ChartPoint {
  t: string;
  temperature: number | null;
  signal: number | null;
  power: number | null;
  current: number | null;
  emPower: number | null;
}
function buildChartPoints(records: MonitoringDataDto[], limit = 12): ChartPoint[] {
  const sorted = [...records].sort(
    (a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()
  );
  return sorted.slice(-limit).map((r) => {
    const sv = (r.sensorValues || {}) as Record<string, unknown>;
    const task = firstArrayItem(sv.tasks);
    const em = firstArrayItem(sv.EMdata);
    return {
      t: fmtHHmm(r.timestamp),
      temperature: toNum(sv.temperature),
      signal: toNum(sv.signal),
      power: task ? toNum(task.power) : null,
      current: task ? toNum(task.current) : null,
      emPower: em ? toNum(em.p) : null,
    };
  });
}

// ─── Real-time clock ─────────────────────────────────────────────────────────
function useClock() {
  const [now, setNow] = useState(new Date());
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(id);
  }, []);
  return now;
}

const WEEK_DAYS = ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六'];
const pad = (n: number) => String(n).padStart(2, '0');

// ─── Panel wrapper ────────────────────────────────────────────────────────────
function Panel({ title, extra, children, className = '', style }: {
  title: string; extra?: React.ReactNode; children: React.ReactNode; className?: string; style?: React.CSSProperties;
}) {
  return (
    <div className={`relative flex flex-col rounded-sm ${className}`}
      style={{ background: 'rgba(3,18,45,0.82)', border: '1px solid rgba(0,195,255,0.25)', ...style }}>
      {/* Top border glow */}
      <div className="absolute top-0 left-0 right-0 h-px"
        style={{ background: 'linear-gradient(90deg, transparent, rgba(0,195,255,0.6), transparent)' }} />
      {/* Header */}
      <div className="flex items-center justify-between px-3 py-2 border-b"
        style={{ borderColor: 'rgba(0,195,255,0.15)' }}>
        <div className="flex items-center gap-2">
          <div className="w-1 h-4 rounded-full" style={{ background: 'linear-gradient(180deg, #00c3ff, #0057b8)' }} />
          <span className="text-xs font-semibold tracking-wide" style={{ color: '#8ecfff' }}>{title}</span>
        </div>
        {extra}
      </div>
      <div className="flex-1 p-3">{children}</div>
    </div>
  );
}

// ─── Zone color palette ───────────────────────────────────────────────────────
const ZONE_PALETTE = [
  { fill: 'rgba(0,195,255,0.55)',  stroke: '#00d4ff', text: '#00e8ff', glow: '#00c3ff' },
  { fill: 'rgba(0,255,159,0.55)',  stroke: '#00ff9f', text: '#00ffaa', glow: '#00ff9f' },
  { fill: 'rgba(255,107,0,0.55)',  stroke: '#ff8c00', text: '#ffaa33', glow: '#ff6b00' },
  { fill: 'rgba(123,47,255,0.55)', stroke: '#9b5fff', text: '#c088ff', glow: '#7b2fff' },
  { fill: 'rgba(255,68,136,0.55)', stroke: '#ff66aa', text: '#ff88cc', glow: '#ff4488' },
  { fill: 'rgba(255,220,0,0.55)',  stroke: '#ffee33', text: '#ffe44d', glow: '#ffdd00' },
  { fill: 'rgba(0,221,255,0.55)',  stroke: '#33eeff', text: '#66f2ff', glow: '#00ddff' },
  { fill: 'rgba(255,136,68,0.55)', stroke: '#ffaa66', text: '#ffcc88', glow: '#ff8844' },
];

// ─── Isometric zone tile computation ─────────────────────────────────────────
// The isometric top face: A(40,120) B(240,30) C(440,120) D(240,210)
// Basis vectors: e1 = B-A = (200,-90) [north], e2 = D-A = (200,90) [east]
function computeZoneTiles(count: number) {
  let rows = 1, cols = 1;
  if (count <= 1)      { rows = 1; cols = 1; }
  else if (count === 2){ rows = 1; cols = 2; }
  else if (count <= 4) { rows = 2; cols = 2; }
  else if (count <= 6) { rows = 2; cols = 3; }
  else if (count <= 8) { rows = 2; cols = 4; }
  else                 { rows = 3; cols = 3; }

  const Ax = 40, Ay = 120;
  const e1x = 200 / rows, e1y = -90 / rows;
  const e2x = 200 / cols, e2y =  90 / cols;

  const tiles: Array<{ points: string; cx: number; cy: number }> = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const p0x = Ax + r * e1x + c * e2x;
      const p0y = Ay + r * e1y + c * e2y;
      const p1x = p0x + e2x, p1y = p0y + e2y;
      const p2x = p0x + e1x + e2x, p2y = p0y + e1y + e2y;
      const p3x = p0x + e1x, p3y = p0y + e1y;
      tiles.push({
        points: `${p0x},${p0y} ${p1x},${p1y} ${p2x},${p2y} ${p3x},${p3y}`,
        cx: (p0x + p1x + p2x + p3x) / 4,
        cy: (p0y + p1y + p2y + p3y) / 4,
      });
    }
  }
  return tiles;
}

// ─── 3D Floor Plan (SVG Isometric, dynamic zones) ────────────────────────────
function FloorPlan3D({
  devices,
  areas,
  onZoneClick,
}: {
  devices: any[];
  areas: Area[];
  onZoneClick?: (area: Area) => void;
}) {
  const onlineCount  = devices.filter(d => d.status === 'online' || d.status === 'warning').length;
  const offlineCount = devices.filter(d => d.status === 'offline').length;
  const warningCount = devices.filter(d => d.status === 'warning').length;
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  // Track hover position for level3 popup
  const [hoverPos, setHoverPos] = useState<{ x: number; y: number } | null>(null);

  // Flatten to level-2 children (or top level if no children)
  const zoneAreas = useMemo(() => {
    if (areas.length === 0) return [] as Area[];
    const lvl2: Area[] = [];
    areas.forEach(a => {
      if (a.children && a.children.length > 0) {
        lvl2.push(...a.children);
      } else {
        lvl2.push(a);
      }
    });
    return lvl2.slice(0, 8);
  }, [areas]);

  // 无真实区域数据时展示空态（不再伪造演示区域）
  const displayAreas: Area[] = zoneAreas.length > 0 ? zoneAreas : [];

  const tiles = computeZoneTiles(displayAreas.length);

  // Hovered level3 children
  const hoveredArea = hoveredId ? displayAreas.find(a => a.id === hoveredId) : null;
  const level3Children: Area[] = (hoveredArea?.children ?? []).filter(c => c.type === 'level3');

  return (
    <div className="relative w-full h-full flex items-center justify-center" style={{ minHeight: 240 }}>
      {/* Grid background */}
      <svg className="absolute inset-0 w-full h-full" style={{ opacity: 0.15 }}>
        <defs>
          <pattern id="dashGrid" width="30" height="30" patternUnits="userSpaceOnUse">
            <path d="M 30 0 L 0 0 0 30" fill="none" stroke="#00c3ff" strokeWidth="0.5" />
          </pattern>
        </defs>
        <rect width="100%" height="100%" fill="url(#dashGrid)" />
      </svg>

      <svg viewBox="0 0 480 300" className="w-full h-full" style={{ maxHeight: 280 }}>
        <defs>
          <filter id="glow3d">
            <feGaussianBlur stdDeviation="2" result="blur" />
            <feComposite in="SourceGraphic" in2="blur" operator="over" />
          </filter>
          <filter id="glowStrong3d">
            <feGaussianBlur stdDeviation="4" result="blur" />
            <feComposite in="SourceGraphic" in2="blur" operator="over" />
          </filter>
        </defs>

        {/* Bottom plate */}
        <polygon points="240,285 440,195 440,200 240,290 40,200 40,195"
          fill="#001a35" stroke="#00c3ff" strokeWidth="0.5" strokeOpacity="0.4" />

        {/* Left wall */}
        <polygon points="40,120 40,195 240,285 240,210"
          fill="#001830" stroke="#00c3ff" strokeWidth="0.8" strokeOpacity="0.5" />
        {/* Right wall */}
        <polygon points="440,120 440,195 240,285 240,210"
          fill="#001020" stroke="#00c3ff" strokeWidth="0.8" strokeOpacity="0.5" />
        {/* Top face base */}
        <polygon points="40,120 240,30 440,120 240,210"
          fill="#002845" stroke="#00c3ff" strokeWidth="1" strokeOpacity="0.3" />

        {/* Dynamic zone tiles */}
        {displayAreas.map((area, i) => {
          const tile = tiles[i];
          if (!tile) return null;
          const pal = ZONE_PALETTE[i % ZONE_PALETTE.length];
          const hovered = hoveredId === area.id;
          const isClickable = !!onZoneClick && zoneAreas.length > 0;
          const hasLevel3 = (area.children ?? []).some(c => c.type === 'level3');

          return (
            <g key={area.id}
              style={{ cursor: isClickable ? 'pointer' : 'default' }}
              onClick={() => isClickable && onZoneClick && onZoneClick(area)}
              onMouseEnter={(e) => {
                if (isClickable) {
                  setHoveredId(area.id);
                  const rect = (e.currentTarget.ownerSVGElement as SVGSVGElement)?.getBoundingClientRect();
                  const svgRect = rect ?? { left: 0, top: 0, width: 1, height: 1 };
                  // Map SVG viewBox coords to screen coords
                  const scaleX = svgRect.width / 480;
                  const scaleY = svgRect.height / 300;
                  setHoverPos({
                    x: tile.cx * scaleX,
                    y: tile.cy * scaleY,
                  });
                }
              }}
              onMouseLeave={() => { setHoveredId(null); setHoverPos(null); }}
            >
              <polygon
                points={tile.points}
                fill={hovered ? pal.fill.replace('0.55', '0.85') : pal.fill}
                stroke={pal.stroke}
                strokeWidth={hovered ? 2 : 1}
                filter={hovered ? 'url(#glowStrong3d)' : 'url(#glow3d)'}
              />
              {/* Area name */}
              <text x={tile.cx} y={tile.cy - 4}
                fill={pal.text} fontSize="8" fontWeight="bold" textAnchor="middle"
                style={{ filter: `drop-shadow(0 0 3px ${pal.glow})`, pointerEvents: 'none' }}>
                {area.name.length > 5 ? area.name.slice(0, 5) + '…' : area.name}
              </text>
              {/* Device count */}
              <text x={tile.cx} y={tile.cy + 7}
                fill={pal.text} fontSize="7" textAnchor="middle"
                style={{ pointerEvents: 'none', opacity: 0.85 }}>
                {(area as any).deviceCount ?? 0}台设备
              </text>
              {/* Level3 expand hint */}
              {hasLevel3 && (
                <text x={tile.cx} y={tile.cy + 17}
                  fill={pal.text} fontSize="6" textAnchor="middle"
                  style={{ pointerEvents: 'none', opacity: 0.7 }}>
                  ▼ {area.children!.filter(c => c.type === 'level3').length}个子区
                </text>
              )}
              {/* Pulse dot (moved down if has level3) */}
              <circle cx={tile.cx} cy={tile.cy + (hasLevel3 ? 24 : 17)} r="2.5" fill={pal.stroke} opacity="0.9">
                <animate attributeName="opacity" values="0.9;0.3;0.9" dur={`${1.5 + i * 0.3}s`} repeatCount="indefinite" />
              </circle>
              <circle cx={tile.cx} cy={tile.cy + (hasLevel3 ? 24 : 17)} r="5" fill="none" stroke={pal.stroke} strokeWidth="0.8" opacity="0.5">
                <animate attributeName="r" values="3;8;3" dur={`${1.5 + i * 0.3}s`} repeatCount="indefinite" />
                <animate attributeName="opacity" values="0.5;0;0.5" dur={`${1.5 + i * 0.3}s`} repeatCount="indefinite" />
              </circle>
            </g>
          );
        })}

        {/* Warning indicator */}
        {warningCount > 0 && (
          <g>
            <circle cx="448" cy="38" r="6" fill="#ff4444">
              <animate attributeName="opacity" values="1;0.2;1" dur="0.8s" repeatCount="indefinite" />
            </circle>
            <text x="448" y="26" fill="#ff4444" fontSize="8" textAnchor="middle">告警</text>
          </g>
        )}

        {/* Scan line */}
        <line x1="40" y1="120" x2="440" y2="120" stroke="#00c3ff" strokeWidth="0.5" opacity="0.3">
          <animate attributeName="y1" values="120;210;120" dur="4s" repeatCount="indefinite" />
          <animate attributeName="y2" values="120;210;120" dur="4s" repeatCount="indefinite" />
          <animate attributeName="opacity" values="0.3;0;0.3" dur="4s" repeatCount="indefinite" />
        </line>

        {/* Click hint */}
        {onZoneClick && zoneAreas.length > 0 && (
          <text x="240" y="295" fill="rgba(0,195,255,0.4)" fontSize="7.5" textAnchor="middle">
            点击区域 → 跳转设备列表
          </text>
        )}
      </svg>

      {/* 无真实区域数据时的空态 */}
      {displayAreas.length === 0 && (
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <span className="text-xs px-3 py-1.5 rounded"
            style={{ color: '#5a8ab0', background: 'rgba(0,12,36,0.75)', border: '1px solid rgba(0,195,255,0.2)' }}>
            暂无区域数据
          </span>
        </div>
      )}

      {/* Level3 expansion popup on hover */}
      {hoveredId && level3Children.length > 0 && hoverPos && (
        <div
          className="absolute pointer-events-none z-20"
          style={{
            left: Math.min(hoverPos.x + 10, 220),
            top: Math.max(hoverPos.y - 90, 4),
            minWidth: 140,
          }}
        >
          <div className="rounded px-2.5 py-2"
            style={{
              background: 'rgba(0,12,36,0.96)',
              border: '1px solid rgba(0,195,255,0.5)',
              boxShadow: '0 0 12px rgba(0,195,255,0.2)',
            }}>
            <div className="text-xs font-semibold mb-1.5 pb-1"
              style={{ color: '#00c3ff', borderBottom: '1px solid rgba(0,195,255,0.2)' }}>
              {hoveredArea?.name} · 子区域
            </div>
            {level3Children.map((child, ci) => (
              <div key={child.id} className="flex items-center justify-between gap-3 py-0.5">
                <div className="flex items-center gap-1.5">
                  <div className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                    style={{ background: ZONE_PALETTE[ci % ZONE_PALETTE.length].stroke }} />
                  <span className="text-xs" style={{ color: '#a8d4f0' }}>{child.name}</span>
                </div>
                <span className="text-xs flex-shrink-0" style={{ color: '#5a9abf' }}>
                  {child.deviceCount ?? 0}台
                </span>
              </div>
            ))}
            <div className="mt-1.5 pt-1 text-xs" style={{ color: 'rgba(0,195,255,0.45)', borderTop: '1px solid rgba(0,195,255,0.15)' }}>
              点击进入设备列表
            </div>
          </div>
        </div>
      )}

      {/* Hover tooltip (when no level3 or no children) */}
      {hoveredId && level3Children.length === 0 && (
        <div className="absolute top-2 right-2 px-2.5 py-1.5 rounded text-xs pointer-events-none"
          style={{ background: 'rgba(0,20,50,0.92)', border: '1px solid rgba(0,195,255,0.45)', color: '#8ecfff' }}>
          {displayAreas.find(a => a.id === hoveredId)?.name} — 点击查看设备
        </div>
      )}

      {/* Status badges */}
      <div className="absolute bottom-2 left-2 flex gap-3">
        <div className="flex items-center gap-1">
          <div className="w-2 h-2 rounded-full bg-green-400" style={{ boxShadow: '0 0 4px #00ff9f' }} />
          <span className="text-xs text-green-400">在线 {onlineCount}</span>
        </div>
        <div className="flex items-center gap-1">
          <div className="w-2 h-2 rounded-full bg-red-400" style={{ boxShadow: '0 0 4px #ff4444' }} />
          <span className="text-xs text-red-400">离线 {offlineCount}</span>
        </div>
        {warningCount > 0 && (
          <div className="flex items-center gap-1">
            <div className="w-2 h-2 rounded-full bg-yellow-400 animate-pulse" />
            <span className="text-xs text-yellow-400">告警 {warningCount}</span>
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Circular Stat ────────────────────────────────────────────────────────────
function CircleStat({ value, label, color, max = 100 }: { value: number; label: string; color: string; max?: number }) {
  const r = 26;
  const circ = 2 * Math.PI * r;
  const pct = Math.min(value / max, 1);
  const dash = pct * circ;

  return (
    <div className="flex flex-col items-center gap-1">
      <div className="relative w-16 h-16">
        <svg className="w-full h-full -rotate-90" viewBox="0 0 64 64">
          <circle cx="32" cy="32" r={r} fill="none" stroke="rgba(255,255,255,0.08)" strokeWidth="5" />
          <circle cx="32" cy="32" r={r} fill="none" stroke={color} strokeWidth="5"
            strokeDasharray={`${dash} ${circ}`} strokeLinecap="round"
            style={{ filter: `drop-shadow(0 0 4px ${color})` }} />
        </svg>
        <div className="absolute inset-0 flex items-center justify-center">
          <span className="text-base font-bold" style={{ color }}>{value}</span>
        </div>
      </div>
      <span className="text-xs text-gray-400">{label}</span>
    </div>
  );
}

// ─── Donut chart for devices ──────────────────────────────────────────────────
function DeviceDonut({ online, offline }: { online: number; offline: number }) {
  const total = online + offline;
  const data = [
    { name: '在线设备', value: online, color: '#00c3ff' },
    { name: '离线设备', value: offline, color: '#1a3a5c' },
  ];
  return (
    <div className="flex items-center gap-3">
      <div className="relative w-[100px] h-[100px] flex-shrink-0">
        <PieChart width={100} height={100}>
          <Pie key="device-pie" data={data} cx={50} cy={50} innerRadius={30} outerRadius={46}
            dataKey="value" startAngle={90} endAngle={-270} strokeWidth={0}>
            {data.map((entry, index) => (
              <Cell key={`device-cell-${entry.name}-${index}`} fill={entry.color}
                style={index === 0 ? { filter: 'drop-shadow(0 0 6px #00c3ff)' } : {}} />
            ))}
          </Pie>
        </PieChart>
        <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
          <span className="text-lg font-bold" style={{ color: '#ffffff' }}>{total}</span>
          <span className="text-xs text-gray-400">总设备</span>
        </div>
      </div>
      <div className="space-y-2 flex-1">
        <div>
          <div className="flex items-center justify-between mb-1">
            <div className="flex items-center gap-1">
              <div className="w-2 h-2 rounded-full" style={{ background: '#00c3ff' }} />
              <span className="text-xs text-gray-300">在线设备</span>
            </div>
            <span className="text-xs font-bold" style={{ color: '#00c3ff' }}>{online}台</span>
          </div>
          <div className="h-1 rounded-full" style={{ background: 'rgba(255,255,255,0.08)' }}>
            <div className="h-full rounded-full" style={{ width: `${total > 0 ? (online / total) * 100 : 0}%`, background: '#00c3ff', boxShadow: '0 0 4px #00c3ff' }} />
          </div>
        </div>
        <div>
          <div className="flex items-center justify-between mb-1">
            <div className="flex items-center gap-1">
              <div className="w-2 h-2 rounded-full bg-gray-500" />
              <span className="text-xs text-gray-300">离线设备</span>
            </div>
            <span className="text-xs font-bold text-gray-400">{offline}台</span>
          </div>
          <div className="h-1 rounded-full" style={{ background: 'rgba(255,255,255,0.08)' }}>
            <div className="h-full rounded-full bg-gray-600" style={{ width: `${total > 0 ? (offline / total) * 100 : 0}%` }} />
          </div>
        </div>
      </div>
    </div>
  );
}

// ─── Main Dashboard ───────────────────────────────────────────────────────────
export function DashboardPage({ onNavigateToArea }: { onNavigateToArea?: (areaName: string) => void }) {
  const { currentCustomer, customers, user, logout, switchCustomer } = useAuth();
  const { devices } = useDevices();
  const { areas } = useArea();
  const now = useClock();
  
  const [showUserMenu, setShowUserMenu] = useState(false);
  const [showCustomerMenu, setShowCustomerMenu] = useState(false);

  // ── 真实后端数据（每个来源独立容错，单接口 403/失败不影响整页） ──
  const [alertSummary, setAlertSummary] = useState<AlertSummaryDto | null>(null);
  const [alertEvents, setAlertEvents] = useState<AlertEventView[]>([]);
  const [workOrderCount, setWorkOrderCount] = useState(0);
  const [chartPoints, setChartPoints] = useState<ChartPoint[]>([]);
  const [panelError, setPanelError] = useState<Record<string, string>>({});

  useEffect(() => {
    let cancelled = false;
    const markError = (key: string, msg: string) => {
      if (!cancelled) setPanelError((prev) => ({ ...prev, [key]: msg }));
    };
    const clearError = (key: string) => {
      if (!cancelled) setPanelError((prev) => {
        const next = { ...prev };
        delete next[key];
        return next;
      });
    };

    const load = async () => {
      // 运营情况 / 预警事件：告警汇总
      dashboardApi.getAlertSummary()
        .then((res) => {
          if (cancelled) return;
          if (res.code === 200 && res.data) { setAlertSummary(res.data); clearError('summary'); }
          else markError('summary', res.message || '告警数据不可用');
        })
        .catch(() => markError('summary', '无权限或网络错误'));

      // 预警事件列表（优先待处理，空则取最近）
      dashboardApi.getAlerts({ page: 1, pageSize: 6, status: 'pending' })
        .then(async (res) => {
          if (cancelled) return;
          let items = res.code === 200 && res.data ? (res.data.items || []) : [];
          if (items.length === 0) {
            try {
              const fallback = await dashboardApi.getAlerts({ page: 1, pageSize: 6 });
              items = fallback.code === 200 && fallback.data ? (fallback.data.items || []) : [];
            } catch { /* 保持空 */ }
          }
          if (!cancelled) { setAlertEvents(items.map(toAlertEventView)); clearError('alerts'); }
        })
        .catch(() => markError('alerts', '无权限或网络错误'));

      // 工单数
      dashboardApi.getWorkOrderCount()
        .then((res) => {
          if (cancelled) return;
          if (res.code === 200) { setWorkOrderCount(res.data ?? 0); clearError('workorders'); }
          else markError('workorders', res.message || '工单数据不可用');
        })
        .catch(() => markError('workorders', '无权限或网络错误'));

      // 环境/变频排风/能耗曲线：真实监控记录
      dashboardApi.getRecentMonitoringData(50)
        .then((res) => {
          if (cancelled) return;
          if (res.code === 200) { setChartPoints(buildChartPoints(res.data || [])); clearError('monitor'); }
          else markError('monitor', res.message || '监控数据不可用');
        })
        .catch(() => markError('monitor', '无权限或网络错误'));
    };

    load();
    const timer = setInterval(load, 30000); // 与设备上行频率一致
    return () => { cancelled = true; clearInterval(timer); };
  }, []);

  // Stats（真实设备列表，无任何假兜底）
  const totalDevices   = devices.length;
  const onlineDevices  = devices.filter(d => d.status === 'online' || d.status === 'warning').length;
  const offlineDevices = devices.filter(d => d.status === 'offline').length;
  const warningDevices = devices.filter(d => d.status === 'warning').length;

  // 超级管理员合计所有客户项目数；租户账号只合计本客户项目数
  const isSuperAdmin = user?.role === 'super_admin';

  // 可见客户列表
  const visibleCustomers = isSuperAdmin
    ? customers
    // F4：c.id / customerId 运行时是 number 而 TS 声明为 string，统一 String() 归一避免静默失配
    : customers.filter(c => String(c.id) === String(user?.customerId ?? currentCustomer?.id));

  const totalCustomers = visibleCustomers.length;

  // 项目数 = 直接统计各客户 projects 数组长度之和（真实数据源）
  let totalProjects = 0;
  for (const c of visibleCustomers) {
    totalProjects += Array.isArray(c.projects) ? c.projects.length : 0;
  }

  // Computed zone areas for legend
  const zoneAreas = useMemo(() => {
    if (areas.length === 0) return [] as Area[];
    const lvl2: Area[] = [];
    areas.forEach(a => {
      if (a.children && a.children.length > 0) {
        lvl2.push(...a.children);
      } else {
        lvl2.push(a);
      }
    });
    return lvl2.slice(0, 8);
  }, [areas]);

  // ── 真实曲线数据（全部来自 /monitoring/data 的设备上报，绝不编造数值） ──
  // 环境监测：温度 / 信号强度（当前设备上报的真实指标）
  const envData = chartPoints.map((p) => ({
    t: p.t,
    temperature: p.temperature,
    signal: p.signal,
  }));
  const hasEnvData = envData.some(d => d.temperature !== null || d.signal !== null);

  // 变频排风：tasks[].power（功率）/ tasks[].current（电流）
  const fanData = chartPoints.map((p) => ({ t: p.t, power: p.power, current: p.current }));
  const hasFanData = fanData.some(d => d.power !== null || d.current !== null);

  // 能耗统计：EMdata[].p（瞬时功率），按采样时刻展示
  const energyData = chartPoints
    .filter((p) => p.emPower !== null)
    .map((p) => ({ d: p.t, v: p.emPower as number }));
  const hasEnergyData = energyData.length > 0;

  // 运营情况三圈（真实告警汇总，空即为 0）
  const alarmCount = alertSummary?.pendingAlerts ?? 0;
  const faultCount = alertSummary?.criticalAlerts ?? 0;
  const workOrderStat = workOrderCount;

  const [envTab, setEnvTab] = useState<'today' | '7days' | 'month'>('today');
  const [projectFilter] = useState('项目筛选');

  const dateStr = `${now.getFullYear()}年${pad(now.getMonth() + 1)}月${pad(now.getDate())}日`;
  const timeStr = `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
  const weekStr = WEEK_DAYS[now.getDay()];

  const chartTooltipStyle = {
    backgroundColor: '#021428',
    border: '1px solid rgba(0,195,255,0.3)',
    borderRadius: '4px',
    color: '#8ecfff',
    fontSize: '11px',
  };

  return (
    <div className="flex flex-col h-full overflow-hidden" style={{ background: '#030d1f', color: '#c8e6f8' }}>

      {/* ── Top bar ── */}
      <div className="flex items-center justify-between px-4 py-2 flex-shrink-0"
        style={{ background: 'rgba(0,18,50,0.95)', borderBottom: '1px solid rgba(0,195,255,0.2)' }}>
        {/* Left - Weather & Customer Selector */}
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <Cloud className="w-5 h-5 text-gray-300" />
            <span className="text-sm text-gray-400">天气服务未接入</span>
          </div>
          
          {/* Customer Selector (for super admin) */}
          {user?.role === 'super_admin' && currentCustomer && (
            <div className="relative">
              <button
                onClick={() => setShowCustomerMenu(!showCustomerMenu)}
                className="flex items-center gap-2 px-3 py-1.5 rounded"
                style={{ background: 'rgba(0,195,255,0.1)', border: '1px solid rgba(0,195,255,0.3)' }}
              >
                <span className="text-sm font-medium" style={{ color: '#00c3ff' }}>{currentCustomer.name}</span>
                <ChevronDown className="w-4 h-4" style={{ color: '#00c3ff' }} />
              </button>

              <AnimatePresence>
                {showCustomerMenu && (
                  <motion.div
                    initial={{ opacity: 0, y: -10 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -10 }}
                    className="absolute top-full left-0 mt-2 w-64 rounded-lg shadow-xl z-50 overflow-hidden"
                    style={{ background: 'rgba(0,18,50,0.98)', border: '1px solid rgba(0,195,255,0.3)' }}
                  >
                    <div className="p-2 max-h-64 overflow-y-auto">
                      {customers.map((customer) => (
                        <button
                          key={customer.id}
                          onClick={() => {
                            switchCustomer(customer.id);
                            setShowCustomerMenu(false);
                          }}
                          className={`w-full text-left px-3 py-2 rounded transition-colors ${
                            currentCustomer.id === customer.id ? 'text-blue-400' : 'text-gray-300 hover:bg-white/5'
                          }`}
                          style={currentCustomer.id === customer.id ? { background: 'rgba(0,195,255,0.15)' } : {}}
                        >
                          <div className="font-medium">{customer.name}</div>
                          <div className="text-xs text-gray-400">{customer.code}</div>
                        </button>
                      ))}
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </div>
          )}
        </div>

        {/* Center - Title */}
        <div className="flex flex-col items-center">
          <h1 className="text-xl font-bold tracking-widest"
            style={{ background: 'linear-gradient(90deg, #00c3ff, #ffffff, #00c3ff)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>
            智厨物联平台
          </h1>
          <div className="h-px w-40 mt-0.5" style={{ background: 'linear-gradient(90deg, transparent, #00c3ff, transparent)' }} />
        </div>

        {/* Right - DateTime & User Menu */}
        <div className="flex items-center gap-4">
          <div className="text-right">
            <div className="text-sm font-mono" style={{ color: '#00c3ff' }}>{dateStr} {timeStr} {weekStr}</div>
          </div>
          
          {/* User Menu */}
          <div className="relative">
            <button
              onClick={() => setShowUserMenu(!showUserMenu)}
              className="flex items-center gap-2 px-3 py-1.5 rounded transition-colors hover:bg-white/5"
            >
              <div className="w-7 h-7 rounded-full flex items-center justify-center"
                style={{ background: 'linear-gradient(135deg, #00c3ff, #0057b8)' }}>
                <User className="w-4 h-4 text-white" />
              </div>
              <div className="text-left">
                <div className="text-sm font-medium text-white">{user?.name}</div>
              </div>
              <ChevronDown className="w-4 h-4 text-gray-400" />
            </button>

            <AnimatePresence>
              {showUserMenu && (
                <motion.div
                  initial={{ opacity: 0, y: -10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -10 }}
                  className="absolute top-full right-0 mt-2 w-48 rounded-lg shadow-xl z-50 overflow-hidden"
                  style={{ background: 'rgba(0,18,50,0.98)', border: '1px solid rgba(0,195,255,0.3)' }}
                >
                  <div className="p-2">
                    <button
                      onClick={() => {
                        logout();
                        setShowUserMenu(false);
                      }}
                      className="w-full flex items-center gap-2 px-3 py-2 rounded transition-colors"
                      style={{ color: '#ff6b6b' }}
                      onMouseEnter={(e) => e.currentTarget.style.background = 'rgba(255,107,107,0.1)'}
                      onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
                    >
                      <LogOut className="w-4 h-4" />
                      <span>退出登录</span>
                    </button>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </div>
      </div>

      {/* ── Main 3-column grid ── */}
      <div className="flex-1 grid grid-cols-[280px_1fr_280px] gap-2 p-2 overflow-hidden min-h-0">

        {/* ══ LEFT COLUMN ══ */}
        <div className="flex flex-col gap-2 min-h-0 overflow-hidden">

          {/* 客户信息 */}
          <Panel title="客户信息">
            <div className="grid grid-cols-2 gap-2">
              {[
                { label: '客户数', value: totalCustomers, unit: '家' },
                { label: '项目数', value: totalProjects, unit: '个' },
              ].map((item) => (
                <div key={item.label} className="flex flex-col items-center">
                  <span className="text-2xl font-bold" style={{ color: '#00c3ff', textShadow: '0 0 8px #00c3ff' }}>
                    {item.value}<span className="text-sm ml-0.5">{item.unit}</span>
                  </span>
                  <span className="text-xs text-gray-400 mt-1">{item.label}</span>
                </div>
              ))}
            </div>
          </Panel>

          {/* 运营情况（真实：告警汇总 + 工单数；无权限时显示提示） */}
          <Panel title="运营情况">
            <div className="flex justify-around items-center py-1">
              <CircleStat value={alarmCount}     max={Math.max(alarmCount, 10)}     label="待处理报警" color="#ff4444" />
              <CircleStat value={faultCount}     max={Math.max(faultCount, 10)}     label="严重告警"   color="#ffaa00" />
              <CircleStat value={workOrderStat}  max={Math.max(workOrderStat, 10)}  label="待处理工单" color="#00c3ff" />
            </div>
            {(panelError.summary || panelError.workorders) && (
              <div className="text-center text-xs mt-1" style={{ color: '#ffaa00' }}>
                {panelError.summary || panelError.workorders}
              </div>
            )}
          </Panel>

          {/* 预警事件 */}
          <Panel title="预警事件" className="flex-1">
            <div className="space-y-1">
              <div className="grid grid-cols-3 gap-1 mb-1 px-1">
                <span className="text-xs text-gray-500">时间</span>
                <span className="text-xs text-gray-500">内容</span>
                <span className="text-xs text-gray-500">处理进度</span>
              </div>
              {alertEvents.length > 0 ? (
                alertEvents.map((ev, i) => (
                  <div key={i} className="grid grid-cols-3 gap-1 px-1 py-0.5 rounded"
                    style={{ background: 'rgba(0,195,255,0.05)', borderLeft: `2px solid ${ev.color}` }}>
                    <span className="text-xs text-gray-400">{ev.time}</span>
                    <span className="text-xs text-gray-300 truncate">{ev.content}</span>
                    <span className="text-xs" style={{ color: ev.color }}>{ev.status}</span>
                  </div>
                ))
              ) : (
                <div className="text-center py-4 text-xs" style={{ color: panelError.alerts ? '#ffaa00' : '#556' }}>
                  {panelError.alerts || '暂无待处理预警'}
                </div>
              )}
            </div>
          </Panel>

          {/* 变频排风 */}
          <Panel title="变频排风"
            extra={
              <button className="flex items-center gap-1 text-xs px-2 py-0.5 rounded"
                style={{ color: '#8ecfff', border: '1px solid rgba(0,195,255,0.3)', background: 'rgba(0,195,255,0.05)' }}>
                {projectFilter} <ChevronDown className="w-3 h-3" />
              </button>
            }
            className="flex-1">
            <div style={{ height: 110 }}>
              {hasFanData ? (
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={fanData} margin={{ top: 5, right: 5, left: -25, bottom: 0 }}>
                    <defs>
                      <linearGradient id="fanPower" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="5%"  stopColor="#ffaa00" stopOpacity="0.4" />
                        <stop offset="95%" stopColor="#ffaa00" stopOpacity="0.0" />
                      </linearGradient>
                      <linearGradient id="fanWind" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="5%"  stopColor="#00c3ff" stopOpacity="0.4" />
                        <stop offset="95%" stopColor="#00c3ff" stopOpacity="0.0" />
                      </linearGradient>
                    </defs>
                    <CartesianGrid strokeDasharray="2 2" stroke="rgba(0,195,255,0.1)" />
                    <XAxis dataKey="t" stroke="#445" tick={{ fill: '#556', fontSize: 9 }} />
                    <YAxis yAxisId="l" stroke="#445" tick={{ fill: '#556', fontSize: 9 }} />
                    <YAxis yAxisId="r" orientation="right" stroke="#445" tick={{ fill: '#556', fontSize: 9 }} />
                    <Tooltip contentStyle={chartTooltipStyle} />
                    <RechartsArea key="fan-power" yAxisId="l" type="monotone" dataKey="power" stroke="#ffaa00" strokeWidth={1.5} fill="url(#fanPower)" name="功率(kW)" connectNulls />
                    <RechartsArea key="fan-current" yAxisId="r" type="monotone" dataKey="current" stroke="#00c3ff" strokeWidth={1.5} fill="url(#fanWind)" name="电流(A)" connectNulls />
                  </AreaChart>
                </ResponsiveContainer>
              ) : (
                <div className="h-full flex items-center justify-center text-xs" style={{ color: panelError.monitor ? '#ffaa00' : '#556' }}>
                  {panelError.monitor || '暂无设备功率/电流上报数据'}
                </div>
              )}
            </div>
          </Panel>
        </div>

        {/* ══ CENTER COLUMN ══ */}
        <div className="flex flex-col gap-2 min-h-0 overflow-hidden">

          {/* 3D Floor Plan – dynamic areas */}
          <Panel title="设备区域总览" className="flex-1"
            extra={
              <div className="flex gap-2 items-center flex-wrap">
                {zoneAreas.length > 0
                  ? zoneAreas.slice(0, 5).map((area, i) => {
                      const pal = ZONE_PALETTE[i % ZONE_PALETTE.length];
                      return (
                        <div key={area.id} className="flex items-center gap-1">
                          <div className="w-2 h-2 rounded-full" style={{ background: pal.stroke }} />
                          <span className="text-xs" style={{ color: pal.text }}>
                            {area.name.length > 4 ? area.name.slice(0, 4) + '…' : area.name}
                          </span>
                        </div>
                      );
                    })
                  : [{ label: '在线', color: '#00ff9f' }, { label: '故障', color: '#ffaa00' }, { label: '离线', color: '#ff4444' }].map(item => (
                      <div key={item.label} className="flex items-center gap-1">
                        <div className="w-2 h-2 rounded-full" style={{ background: item.color }} />
                        <span className="text-xs" style={{ color: item.color }}>{item.label}</span>
                      </div>
                    ))
                }
              </div>
            }>
            <FloorPlan3D
              devices={devices}
              areas={areas}
              onZoneClick={(area) => {
                if (onNavigateToArea) onNavigateToArea(area.name);
              }}
            />
          </Panel>

          {/* 环境监 */}
          <Panel title="环境监测"
            extra={
              <div className="flex items-center gap-1">
                {(['today', '7days', 'month'] as const).map((tab, i) => (
                  <button key={tab} onClick={() => setEnvTab(tab)}
                    className="text-xs px-2 py-0.5 rounded transition-colors"
                    style={{
                      background: envTab === tab ? 'rgba(0,195,255,0.2)' : 'transparent',
                      color: envTab === tab ? '#00c3ff' : '#556',
                      border: envTab === tab ? '1px solid rgba(0,195,255,0.4)' : '1px solid transparent',
                    }}>
                    {['今日', '近7天', '本月'][i]}
                  </button>
                ))}
                <button className="flex items-center gap-1 text-xs px-2 py-0.5 rounded ml-2"
                  style={{ color: '#8ecfff', border: '1px solid rgba(0,195,255,0.3)', background: 'rgba(0,195,255,0.05)' }}>
                  项目筛选 <ChevronDown className="w-3 h-3" />
                </button>
              </div>
            }
            className="flex-none">
            <div style={{ height: 120 }}>
              {hasEnvData ? (
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={envData} margin={{ top: 5, right: 5, left: -25, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="2 2" stroke="rgba(0,195,255,0.1)" />
                    <XAxis dataKey="t" stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                    <YAxis yAxisId="l" stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                    <YAxis yAxisId="r" orientation="right" stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                    <Tooltip contentStyle={chartTooltipStyle} />
                    <Legend wrapperStyle={{ fontSize: '10px', paddingTop: '2px' }} />
                    <Line key="env-temp" yAxisId="l" type="monotone" dataKey="temperature" stroke="#ffaa00" strokeWidth={1.5} dot={false} name="温度(°C)" connectNulls />
                    <Line key="env-signal" yAxisId="r" type="monotone" dataKey="signal" stroke="#00c3ff" strokeWidth={1.5} dot={false} name="信号强度" connectNulls />
                  </LineChart>
                </ResponsiveContainer>
              ) : (
                <div className="h-full flex items-center justify-center text-xs" style={{ color: panelError.monitor ? '#ffaa00' : '#556' }}>
                  {panelError.monitor || '暂无设备温度/信号上报数据'}
                </div>
              )}
            </div>
            <div className="flex gap-4 mt-1 px-1">
              <span className="text-xs" style={{ color: '#ffaa00' }}>温度(°C)</span>
              <span className="text-xs" style={{ color: '#00c3ff' }}>信号强度</span>
              <span className="text-xs text-gray-500">数据来自设备真实上报</span>
            </div>
          </Panel>
        </div>

        {/* ══ RIGHT COLUMN ══ */}
        <div className="flex flex-col gap-2 min-h-0 overflow-hidden">

          {/* 实时视频监控（后端暂无摄像头/视频流接口，诚实占位，不伪造事件） */}
          <Panel title="实时视频监控" className="flex-none">
            <div className="grid grid-cols-2 gap-1.5">
              {['画面 1', '画面 2'].map((label, i) => (
                <div key={i} className="relative rounded overflow-hidden flex items-center justify-center"
                  style={{ aspectRatio: '16/9', background: 'rgba(0,20,50,0.6)', border: '1px dashed rgba(0,195,255,0.25)' }}>
                  <span className="text-xs" style={{ color: '#4a7a9f' }}>暂无视频源</span>
                </div>
              ))}
            </div>
            <div className="mt-2 text-center text-xs" style={{ color: '#5a8ab0' }}>
              暂未接入摄像头 / 视频流服务
            </div>
          </Panel>

          {/* 设备情况（真实设备统计，无假兜底） */}
          <Panel title="设备情况">
            <DeviceDonut
              online={onlineDevices}
              offline={offlineDevices}
            />
          </Panel>

          {/* 能耗统计 */}
          <Panel title="能耗统计"
            extra={
              <div className="flex gap-1">
                {['日', '月', '年'].map(tab => (
                  <button key={tab} className="text-xs px-2 py-0.5 rounded"
                    style={{ color: '#8ecfff', border: '1px solid rgba(0,195,255,0.3)', background: 'rgba(0,195,255,0.05)' }}>
                    {tab}
                  </button>
                ))}
              </div>
            }
            className="flex-1">
            <div style={{ height: 120 }}>
              {hasEnergyData ? (
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={energyData} margin={{ top: 5, right: 5, left: -20, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="2 2" stroke="rgba(0,195,255,0.1)" vertical={false} />
                    <XAxis dataKey="d" stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                    <YAxis stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                    <Tooltip contentStyle={chartTooltipStyle} formatter={(v: number) => [Number(v).toFixed(3), '瞬时功率(kW)']} />
                    <Bar key="energy-bar" dataKey="v" radius={[2, 2, 0, 0]} maxBarSize={24}>
                      {energyData.map((entry, i) => (
                        <Cell key={`energy-cell-${entry.d}-${i}`} fill={i === energyData.length - 1 ? '#00c3ff' : '#0a4a7a'}
                          style={i === energyData.length - 1 ? { filter: 'drop-shadow(0 0 4px #00c3ff)' } : {}} />
                      ))}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              ) : (
                <div className="h-full flex items-center justify-center text-xs" style={{ color: panelError.monitor ? '#ffaa00' : '#556' }}>
                  {panelError.monitor || '暂无设备功率上报数据'}
                </div>
              )}
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}