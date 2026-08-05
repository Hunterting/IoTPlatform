import {
  Activity, Info, Zap, Play, Ban, Upload, Settings, RotateCcw,
} from 'lucide-react';

/**
 * 安圣命令控制台 —— 协议族隔离（T14）。
 *
 * 【这个文件解决什么问题】
 * 改造前，「二开设备命令面板不出现 orderStart/orderEnd/orderUp」这条约束
 * 是靠<b>人工挑选</b>维持的：有人手写了一份只含 3 条命令的数组，注释里写着别加 order*。
 * 这种约定活不过第三次需求变更 —— 只要有人复制粘贴一条模板过去，
 * 现网的二开设备就会收到一条充电桩协议的报文，而且没有任何东西会报错。
 *
 * 现在改成：所有模板集中登记在 {@link COMMAND_TEMPLATE_REGISTRY}，每条<b>必须</b>显式标注
 * 所属协议族；各控制台的可见列表由 {@link filterByProtocolFamily} 按族<b>推导</b>出来。
 * 往二开控制台里错加一条 ChargingPile 模板不再会泄漏到 UI —— 它会被过滤掉并在开发期告警。
 *
 * 这与后端 T14 的做法是同一套思路的两端：后端用 `AnShengProtocolFamilyResolver`
 * 把「不在目录里就按 Legacy 发」的隐式兜底换成三态显式判定，前端用这里的显式过滤
 * 把「这份数组恰好没写 order*」的隐式约定换成结构性保证。
 */

// ── 协议族 ────────────────────────────────────────────────────────

/**
 * 协议族标识。取值与后端 `AnShengProtocolFamily` 枚举<b>逐字对应</b>。
 *
 * 后端经 `JsonStringEnumConverter` 以字符串出网，因此这里用字符串字面量联合类型
 * 而不是数字 —— 避免枚举序号漂移导致前后端对不上（数字 0/1 谁也看不出是什么）。
 */
export type AnShengProtocolFamily = 'OpenProtocol' | 'ChargingPile';

/** 协议族的中文展示名与配色，用于面板上的来源标注。 */
export const PROTOCOL_FAMILY_META: Record<
  AnShengProtocolFamily,
  { label: string; shortLabel: string; description: string }
> = {
  OpenProtocol: {
    label: '二开协议',
    shortLabel: '二开',
    description: '参数平铺在报文顶层，秒级 timestamp（asopen.md）',
  },
  ChargingPile: {
    label: 'Legacy 充电桩',
    shortLabel: 'Legacy',
    description: '参数包裹在 param 内，毫秒字符串 timestamp（旧版充电桩协议）',
  },
};

// ── 模板类型 ──────────────────────────────────────────────────────

/** 命令参数字段描述。 */
export interface ParamField {
  key: string;
  label: string;
  type: 'text' | 'number' | 'select';
  options?: string[];
  defaultValue?: string;
  placeholder?: string;
}

/** 命令模板。 */
export interface CommandTemplate {
  method: string;
  label: string;
  icon: React.ReactNode;
  description: string;
  color: string;
  /**
   * 所属协议族 —— <b>必填</b>，没有默认值。
   *
   * 刻意不给默认值：一旦允许省略，新增模板时漏填就会静默落到某一族里，
   * 这正是 T14 要消灭的那类「不写就当 Legacy」的隐式兜底。
   * 编译期强制填写的成本是一行，收益是错误在写代码时就暴露。
   */
  protocolFamily: AnShengProtocolFamily;
  params: ParamField[];
}

// ── 模板总登记处（唯一真相来源）────────────────────────────────────

/**
 * 全部命令模板。各控制台的可见列表一律从这里按协议族推导，不再各自维护数组。
 *
 * 【不得收录】setSwitch / getSwitchStatus / setSwitchConfig / getSwitchConfig ——
 * 这四个方法在安圣官方协议（asopen.md）中不存在，属历史臆造的「伪命令」，
 * 后端端点已于 T3 物理删除。开关通断请用官方 action / actions，
 * 状态查询请用 getDevStatus(q=slots)。
 */
export const COMMAND_TEMPLATE_REGISTRY: CommandTemplate[] = [
  {
    method: 'getDevStatus',
    label: '查询设备状态',
    icon: <Activity className="w-4 h-4" />,
    description: '获取设备温度、电量计等实时状态',
    color: 'blue',
    protocolFamily: 'OpenProtocol',
    params: [],
  },
  {
    method: 'getDevInfo',
    label: '查询设备信息',
    icon: <Info className="w-4 h-4" />,
    description: '获取设备型号、网络类型等基础信息',
    color: 'cyan',
    protocolFamily: 'OpenProtocol',
    params: [],
  },
  {
    method: 'getEMRealtime',
    label: '实时电量查询',
    icon: <Zap className="w-4 h-4" />,
    description: '获取各插槽实时电压、电流、功率',
    color: 'amber',
    protocolFamily: 'OpenProtocol',
    params: [],
  },
  {
    method: 'orderStart',
    label: '开始充电',
    icon: <Play className="w-4 h-4" />,
    description: '启动指定插槽充电',
    color: 'green',
    protocolFamily: 'ChargingPile',
    params: [
      { key: 'slot', label: '插槽编号', type: 'number', defaultValue: '1', placeholder: '1' },
    ],
  },
  {
    method: 'orderEnd',
    label: '停止充电',
    icon: <Ban className="w-4 h-4" />,
    description: '停止指定插槽充电',
    color: 'red',
    protocolFamily: 'ChargingPile',
    params: [
      { key: 'slot', label: '插槽编号', type: 'number', defaultValue: '1', placeholder: '1' },
    ],
  },
  {
    method: 'orderUp',
    label: '订单推送',
    icon: <Upload className="w-4 h-4" />,
    description: '向设备推送完整的充电订单',
    color: 'purple',
    protocolFamily: 'ChargingPile',
    params: [
      { key: 'orderId', label: '订单号', type: 'text', placeholder: '例如：ORD123456' },
      { key: 'slot', label: '插槽编号', type: 'number', defaultValue: '1', placeholder: '1' },
      { key: 'durationMin', label: '充电时长(分)', type: 'number', defaultValue: '60', placeholder: '60' },
    ],
  },
  {
    method: 'setAutoReport',
    label: '设置自动上报',
    icon: <Settings className="w-4 h-4" />,
    description: '配置设备定时上报间隔',
    color: 'slate',
    protocolFamily: 'OpenProtocol',
    params: [
      { key: 'getDevStatusSec', label: '状态上报间隔(秒)', type: 'number', defaultValue: '60', placeholder: '60' },
      { key: 'orderUpSec', label: '订单推送间隔(秒)', type: 'number', defaultValue: '300', placeholder: '300' },
    ],
  },
  {
    method: 'reboot',
    label: '重启设备',
    icon: <RotateCcw className="w-4 h-4" />,
    description: '远程重启设备',
    color: 'orange',
    protocolFamily: 'OpenProtocol',
    params: [],
  },
];

// ── 协议族过滤 ────────────────────────────────────────────────────

/**
 * 按协议族过滤模板 —— 「默认拒绝、显式放行」在前端的落点。
 *
 * @param templates 待过滤的模板集合。
 * @param allowed 放行的协议族集合；未列入的一律剔除。
 * @param consoleName 控制台名称，仅用于开发期告警定位。
 * @returns 只含放行协议族的模板（保持原有顺序）。
 */
export function filterByProtocolFamily(
  templates: CommandTemplate[],
  allowed: readonly AnShengProtocolFamily[],
  consoleName: string,
): CommandTemplate[] {
  const allowSet = new Set<AnShengProtocolFamily>(allowed);
  const kept: CommandTemplate[] = [];

  for (const tpl of templates) {
    if (allowSet.has(tpl.protocolFamily)) {
      kept.push(tpl);
      continue;
    }

    // 被拦下来的模板在开发期必须吵一声：静默过滤会让「命令怎么不见了」变成一桩悬案。
    if (import.meta.env.DEV) {
      console.warn(
        `[CommandConsole] 模板 "${tpl.method}" 属于 ${tpl.protocolFamily} 协议族，` +
          `不在「${consoleName}」允许的 [${allowed.join(', ')}] 之内，已从面板剔除。`,
      );
    }
  }

  return kept;
}

/**
 * 充电桩控制台可见命令。
 *
 * 充电桩既认 3 条 Legacy 订单命令，也认二开的通用查询/配置命令
 * （报文体被 param 包裹，但 method 集合是共享的），因此两族都放行。
 */
export const CHARGING_PILE_CONSOLE_TEMPLATES: CommandTemplate[] = filterByProtocolFamily(
  COMMAND_TEMPLATE_REGISTRY,
  ['OpenProtocol', 'ChargingPile'],
  '充电桩控制台',
);

/**
 * 二开设备控制台可见命令 —— <b>验收标准 ①</b> 的结构性保证。
 *
 * 只放行 OpenProtocol，因此 orderStart / orderEnd / orderUp
 * 在类型与运行时两个层面都<b>不可能</b>出现在这个列表里。
 *
 * 这里额外收窄到「当前面板确实在用」的三条，保持 T14 前后 UI 一致（最小变更）：
 * getEMRealtime / setAutoReport 虽同属二开协议，但充电桩语义更重，
 * 本次不顺手加进二开面板 —— 扩列表是产品决策，不该混在协议族归位里做掉。
 */
const OPEN_DEVICE_VISIBLE_METHODS: readonly string[] = ['reboot', 'getDevInfo', 'getDevStatus'];

export const OPEN_DEVICE_CONSOLE_TEMPLATES: CommandTemplate[] = filterByProtocolFamily(
  OPEN_DEVICE_VISIBLE_METHODS
    .map(method => COMMAND_TEMPLATE_REGISTRY.find(t => t.method === method))
    .filter((t): t is CommandTemplate => t !== undefined),
  ['OpenProtocol'],
  '二开设备控制台',
);

// ── 展示组件 ──────────────────────────────────────────────────────

/** 协议族角标。让操作员一眼看出这条命令按哪套报文结构下发。 */
export function ProtocolFamilyBadge({ family }: { family: AnShengProtocolFamily }) {
  const meta = PROTOCOL_FAMILY_META[family];
  const tone =
    family === 'ChargingPile'
      ? 'bg-amber-500/15 text-amber-300 border-amber-500/30'
      : 'bg-sky-500/15 text-sky-300 border-sky-500/30';

  return (
    <span
      title={meta.description}
      className={`shrink-0 px-1.5 py-0.5 rounded text-[10px] leading-none border ${tone}`}
    >
      {meta.shortLabel}
    </span>
  );
}

/** 命令模板选择器属性。 */
export interface CommandTemplatePickerProps {
  /** 可选模板（应由协议族过滤后的列表传入）。 */
  templates: CommandTemplate[];
  /** 当前选中的 method。 */
  selectedMethod: string;
  /** 选中回调。 */
  onSelect: (template: CommandTemplate) => void;
  /**
   * 视觉变体：两个面板的既有样式略有差异，
   * 原样保留以避免协议族归位顺带改动 UI 观感。
   */
  variant: 'pile' | 'open';
  /** 是否显示协议族角标。 */
  showFamilyBadge?: boolean;
}

/**
 * 命令模板选择列表（纯展示，无内部状态）。
 *
 * @param props 选择器属性。
 * @returns 模板按钮列表。
 */
export function CommandTemplatePicker({
  templates,
  selectedMethod,
  onSelect,
  variant,
  showFamilyBadge = false,
}: CommandTemplatePickerProps) {
  if (variant === 'pile') {
    return (
      <div className="space-y-1">
        {templates.map(tpl => (
          <button
            key={tpl.method}
            onClick={() => onSelect(tpl)}
            className={`w-full text-left px-3 py-2 rounded-lg text-sm transition-all ${
              selectedMethod === tpl.method
                ? 'bg-purple-600/20 border border-purple-500/30'
                : 'border border-transparent hover:bg-slate-700/50'
            }`}
          >
            <div className="flex items-center gap-2">
              <span className={`text-${tpl.color}-400`}>{tpl.icon}</span>
              <span className="font-medium text-slate-200">{tpl.label}</span>
              {showFamilyBadge && <ProtocolFamilyBadge family={tpl.protocolFamily} />}
            </div>
            <div className="text-xs text-slate-500 mt-0.5 ml-6">{tpl.description}</div>
          </button>
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-1.5">
      {templates.map(tpl => (
        <button
          key={tpl.method}
          onClick={() => onSelect(tpl)}
          className={`w-full text-left px-3 py-2 rounded-lg transition-all ${
            selectedMethod === tpl.method
              ? `bg-${tpl.color}-500/15 border border-${tpl.color}-500/30`
              : 'hover:bg-slate-700/20 border border-transparent'
          }`}
        >
          <div className="flex items-center gap-2">
            <span className={`text-${tpl.color}-400`}>{tpl.icon}</span>
            <div>
              <div className="text-sm font-medium text-slate-200">{tpl.label}</div>
              <div className="text-xs text-slate-500">{tpl.description}</div>
            </div>
            {showFamilyBadge && <ProtocolFamilyBadge family={tpl.protocolFamily} />}
          </div>
        </button>
      ))}
    </div>
  );
}
