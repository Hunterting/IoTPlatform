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
import { ALARM_COUNT, FAULT_COUNT, ACTIVE_WORK_ORDER_COUNT, SHARED_ALERT_RECORDS, AlertRecord } from '@/app/config/sharedMockData';

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

  // Fallback demo zones when no real data
  const displayAreas: Area[] = zoneAreas.length > 0 ? zoneAreas : [
    { id: 'demo1', name: '大厅区',   type: 'level2', image: '', deviceCount: 45 },
    { id: 'demo2', name: '中央厨房', type: 'level2', image: '', deviceCount: 52 },
    { id: 'demo3', name: '冷库',     type: 'level2', image: '', deviceCount: 12 },
    { id: 'demo4', name: '备餐间',   type: 'level2', image: '', deviceCount: 18 },
  ];

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

  // Stats
  const totalDevices   = devices.length;
  const onlineDevices  = devices.filter(d => d.status === 'online' || d.status === 'warning').length;
  const offlineDevices = devices.filter(d => d.status === 'offline').length;
  const warningDevices = devices.filter(d => d.status === 'warning').length;

  // 超级管理员合计所有客户项目数；租户账号只合计本客户项目数
  const isSuperAdmin = user?.role === 'super_admin';

  // 可见客户列表
  const visibleCustomers = isSuperAdmin
    ? customers
    : customers.filter(c => c.id === (user?.customerId ?? currentCustomer?.id));

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

  // ── Mock data ──
  const envData = [
    { t: '03:00', pm25: 22, co: 35, ch2o: 12, temp: 18 },
    { t: '04:00', pm25: 30, co: 42, ch2o: 15, temp: 17 },
    { t: '05:00', pm25: 28, co: 45, ch2o: 18, temp: 17 },
    { t: '06:00', pm25: 50, co: 60, ch2o: 22, temp: 18 },
    { t: '07:00', pm25: 65, co: 58, ch2o: 28, temp: 18 },
    { t: '08:00', pm25: 45, co: 52, ch2o: 20, temp: 19 },
    { t: '09:00', pm25: 38, co: 48, ch2o: 17, temp: 20 },
    { t: '10:00', pm25: 42, co: 55, ch2o: 19, temp: 20 },
    { t: '11:00', pm25: 35, co: 40, ch2o: 14, temp: 21 },
    { t: '12:00', pm25: 30, co: 38, ch2o: 13, temp: 21 },
  ];

  const energyData = [
    { d: '4日', v: 22491 },
    { d: '5日', v: 16585 },
    { d: '6日', v: 25000 },
    { d: '7日', v: 12353 },
    { d: '8日', v: 15072 },
    { d: '9日', v: 8023 },
    { d: '10日', v: 14311 },
  ];

  const fanData = [
    { t: '03:00', power: 120, wind: 8 },
    { t: '04:00', power: 98, wind: 6 },
    { t: '05:00', power: 150, wind: 10 },
    { t: '06:00', power: 200, wind: 13 },
    { t: '07:00', power: 265, wind: 15 },
    { t: '08:00', power: 180, wind: 12 },
    { t: '09:00', power: 160, wind: 11 },
    { t: '10:00', power: 145, wind: 10 },
    { t: '11:00', power: 130, wind: 9 },
    { t: '12:00', power: 110, wind: 8 },
  ];

  // 获取最近的预警事件，只显示待处理和处理中的，转换格式用于显示
  const alertEvents = SHARED_ALERT_RECORDS
    .filter(alert => alert.status === 'pending' || alert.status === 'processing')
    .slice(0, 6)
    .map(alert => {
      // 根据状态和级别决定颜色
      let color = '#00c3ff';
      let statusText = '待处理';
      
      if (alert.status === 'processing') {
        color = '#ffaa00';
        statusText = '处理中';
      } else if (alert.status === 'pending' && alert.level === 'critical') {
        color = '#ff4444';
        statusText = '待处理';
      }
      
      // 组合设备名称和告警类型作为内容
      const typeMap: Record<string, string> = {
        temperature: '温度异常',
        pressure: '压力异常',
        offline: '设备离线',
        power: '功率异常',
        smoke: '烟雾异常',
        door: '门体异常',
      };
      const content = `${alert.deviceName} ${typeMap[alert.alertType] || '异常'}`;
      
      return {
        time: alert.alertTime.slice(5, 16), // 只显示月日时分
        content,
        status: statusText,
        color,
      };
    });

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
            <span className="text-sm text-gray-300">多云 12-17°C</span>
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

          {/* 运营情况 */}
          <Panel title="运营情况">
            <div className="flex justify-around items-center py-1">
              <CircleStat value={ALARM_COUNT}              max={Math.max(ALARM_COUNT, 10)}              label="报警"   color="#ff4444" />
              <CircleStat value={FAULT_COUNT}              max={Math.max(FAULT_COUNT, 10)}              label="故障"   color="#ffaa00" />
              <CircleStat value={ACTIVE_WORK_ORDER_COUNT}  max={Math.max(ACTIVE_WORK_ORDER_COUNT, 10)}  label="工单"   color="#00c3ff" />
            </div>
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
                <div className="text-center py-4 text-xs text-gray-500">暂无待处理预警</div>
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
                  <RechartsArea key="fan-power" yAxisId="l" type="monotone" dataKey="power" stroke="#ffaa00" strokeWidth={1.5} fill="url(#fanPower)" name="功率(kW)" />
                  <RechartsArea key="fan-wind" yAxisId="r" type="monotone" dataKey="wind" stroke="#00c3ff" strokeWidth={1.5} fill="url(#fanWind)" name="风速(m/s)" />
                </AreaChart>
              </ResponsiveContainer>
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
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={envData} margin={{ top: 5, right: 5, left: -25, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="2 2" stroke="rgba(0,195,255,0.1)" />
                  <XAxis dataKey="t" stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                  <YAxis stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                  <Tooltip contentStyle={chartTooltipStyle} />
                  <Legend wrapperStyle={{ fontSize: '10px', paddingTop: '2px' }} />
                  <Line key="env-pm25" type="monotone" dataKey="pm25" stroke="#00c3ff" strokeWidth={1.5} dot={false} name="PM2.5浓度" />
                  <Line key="env-co" type="monotone" dataKey="co" stroke="#00ff9f" strokeWidth={1.5} dot={false} name="一氧化碳浓度" />
                  <Line key="env-ch2o" type="monotone" dataKey="ch2o" stroke="#ff6b6b" strokeWidth={1.5} dot={false} name="甲醛浓度" />
                  <Line key="env-temp" type="monotone" dataKey="temp" stroke="#ffaa00" strokeWidth={1.5} dot={false} name="温湿度" />
                </LineChart>
              </ResponsiveContainer>
            </div>
            <div className="flex gap-4 mt-1 px-1">
              <span className="text-xs" style={{ color: '#ff6b6b' }}>▲ 超阈值: PM2.5</span>
              <span className="text-xs" style={{ color: '#ffaa00' }}>▲ 超阈值: 甲醛</span>
              <span className="text-xs text-gray-500">日期/月份</span>
            </div>
          </Panel>
        </div>

        {/* ══ RIGHT COLUMN ══ */}
        <div className="flex flex-col gap-2 min-h-0 overflow-hidden">

          {/* 实时视频监控 */}
          <Panel title="实时视频监控" className="flex-none">
            <div className="grid grid-cols-2 gap-1.5">
              {[
                { src: 'https://images.unsplash.com/photo-1740803292814-13d2e35924c3?w=300&q=80', label: '主厨房' },
                { src: 'https://images.unsplash.com/photo-1771574207826-02f0d845e3a7?w=300&q=80', label: '操作台' },
              ].map((cam, i) => (
                <div key={i} className="relative rounded overflow-hidden" style={{ aspectRatio: '16/9' }}>
                  <img src={cam.src} alt={cam.label} className="w-full h-full object-cover" />
                  <div className="absolute inset-0" style={{ background: 'rgba(0,10,30,0.3)' }} />
                  <div className="absolute top-1 left-1 flex items-center gap-1">
                    <div className="w-1.5 h-1.5 rounded-full bg-red-500 animate-pulse" />
                    <span className="text-xs" style={{ color: '#ffffff', textShadow: '0 0 4px #000' }}>REC</span>
                  </div>
                  <div className="absolute" style={{ top: '25%', left: '20%', width: '30%', height: '50%', border: '1px solid #00ff9f', boxShadow: '0 0 4px #00ff9f' }}>
                    <span className="absolute -top-4 left-0 text-xs" style={{ color: '#00ff9f', fontSize: 9 }}>员工识别</span>
                  </div>
                  <div className="absolute bottom-1 left-1 right-1 text-xs truncate"
                    style={{ color: '#ffffff', textShadow: '0 0 4px #000', fontSize: 9 }}>
                    {cam.label}
                  </div>
                </div>
              ))}
            </div>
            <div className="mt-2 space-y-1">
              {[
                { label: '时间', value: '2024-08-25 09:52' },
                { label: '地点', value: '主厨房' },
                { label: '事件', value: '打电话' },
              ].map(item => (
                <div key={item.label} className="flex gap-2 text-xs">
                  <span className="text-gray-500 w-8 flex-shrink-0">{item.label}</span>
                  <span style={{ color: '#8ecfff' }}>{item.value}</span>
                </div>
              ))}
            </div>
          </Panel>

          {/* 设备情况 */}
          <Panel title="设备情况">
            <DeviceDonut
              online={onlineDevices || 5200}
              offline={offlineDevices || 92}
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
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={energyData} margin={{ top: 5, right: 5, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="2 2" stroke="rgba(0,195,255,0.1)" vertical={false} />
                  <XAxis dataKey="d" stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                  <YAxis stroke="#445" tick={{ fill: '#667', fontSize: 9 }} />
                  <Tooltip contentStyle={chartTooltipStyle} formatter={(v: number) => [v.toLocaleString(), '能耗(kWh)']} />
                  <Bar key="energy-bar" dataKey="v" radius={[2, 2, 0, 0]} maxBarSize={24}>
                    {energyData.map((entry, i) => (
                      <Cell key={`energy-cell-${entry.d}-${i}`} fill={i === 2 ? '#00c3ff' : '#0a4a7a'}
                        style={i === 2 ? { filter: 'drop-shadow(0 0 4px #00c3ff)' } : {}} />
                    ))}
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}