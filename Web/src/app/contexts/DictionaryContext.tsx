import { createContext, useContext, useState, ReactNode } from 'react';

// 字典类型枚举
export const DictionaryType = {
  DEVICE_CATEGORY: 'device_category', // 设备类别
  ARCHIVE_CATEGORY: 'archive_category', // 档案分类
  DEVICE_STATUS: 'device_status', // 设备状态
  DEVICE_BRAND: 'device_brand', // 设备品牌
  WORK_ORDER_TYPE: 'work_order_type', // 工单类型
  WORK_ORDER_PRIORITY: 'work_order_priority', // 工单优先级
  ALERT_LEVEL: 'alert_level', // 告警级别
  SENSOR_TYPE: 'sensor_type', // 传感器类型
  // 可以继续添加更多字典类型...
} as const;

export type DictionaryTypeValue = typeof DictionaryType[keyof typeof DictionaryType];

// 字典类型配置接口
export interface DictionaryTypeConfig {
  key: DictionaryTypeValue;
  name: string;
  icon: string; // 图标名称
  description: string;
  color: string; // 主题色
}

// 字典类型配置 - 集中管理所有字典类型
export const DICTIONARY_TYPE_CONFIGS: DictionaryTypeConfig[] = [
  {
    key: DictionaryType.DEVICE_CATEGORY,
    name: '设备类别',
    icon: 'Cpu',
    description: '管理设备的分类，如制冷设备、烹饪设备等',
    color: 'blue',
  },
  {
    key: DictionaryType.ARCHIVE_CATEGORY,
    name: '档案分类',
    icon: 'Archive',
    description: '管理档案的分类，如图纸资料、设备手册等',
    color: 'purple',
  },
  {
    key: DictionaryType.DEVICE_STATUS,
    name: '设备状态',
    icon: 'Activity',
    description: '管理设备的状态类型，如运行中、故障、维护中等',
    color: 'green',
  },
  {
    key: DictionaryType.DEVICE_BRAND,
    name: '设备品牌',
    icon: 'Tag',
    description: '管理设备的品牌信息',
    color: 'orange',
  },
  {
    key: DictionaryType.WORK_ORDER_TYPE,
    name: '工单类型',
    icon: 'FileText',
    description: '管理工单的类型，如维修、保养、巡检等',
    color: 'cyan',
  },
  {
    key: DictionaryType.WORK_ORDER_PRIORITY,
    name: '工单优先级',
    icon: 'AlertTriangle',
    description: '管理工单的优先级，如紧急、高、中、低等',
    color: 'red',
  },
  {
    key: DictionaryType.ALERT_LEVEL,
    name: '告警级别',
    icon: 'Bell',
    description: '管理告警的级别，如严重、警告、提示等',
    color: 'yellow',
  },
  {
    key: DictionaryType.SENSOR_TYPE,
    name: '传感器类型',
    icon: 'Radio',
    description: '管理传感器的类型，如温度、湿度、压力等',
    color: 'indigo',
  },
];

// 辅助函数：根据key获取字典类型配置
export const getDictionaryTypeConfig = (key: DictionaryTypeValue): DictionaryTypeConfig | undefined => {
  return DICTIONARY_TYPE_CONFIGS.find(config => config.key === key);
};

// 字典项接口
export interface DictionaryItem {
  id: string;
  type: DictionaryTypeValue;
  code: string; // 字典编码
  name: string; // 字典名称
  sort: number; // 排序
  description?: string; // 描述
  status: 'active' | 'inactive'; // 状态
  createdAt: string;
  updatedAt: string;
}

interface DictionaryContextType {
  // 获取所有字典项
  getAllItems: () => DictionaryItem[];
  // 根据类型获取字典项
  getItemsByType: (type: DictionaryTypeValue) => DictionaryItem[];
  // 添加字典项
  addItem: (item: Omit<DictionaryItem, 'id' | 'createdAt' | 'updatedAt'>) => void;
  // 更新字典项
  updateItem: (id: string, item: Partial<DictionaryItem>) => void;
  // 删除字典项
  deleteItem: (id: string) => void;
  // 根据ID获取字典项
  getItemById: (id: string) => DictionaryItem | undefined;
  // 字典类型配置管理
  typeConfigs: DictionaryTypeConfig[];
  addTypeConfig: (config: DictionaryTypeConfig) => void;
  updateTypeConfig: (key: string, config: Partial<DictionaryTypeConfig>) => void;
  deleteTypeConfig: (key: string) => void;
  getTypeConfig: (key: string) => DictionaryTypeConfig | undefined;
}

const DictionaryContext = createContext<DictionaryContextType | undefined>(undefined);

// 初始字典数据 - 迁移自设备类别和档案分类
const initialDictionaries: DictionaryItem[] = [
  // 设备类别
  {
    id: 'dict-device-001',
    type: DictionaryType.DEVICE_CATEGORY,
    code: 'refrigeration',
    name: '制冷设备',
    sort: 1,
    description: '冰箱、冷柜等制冷设备',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-device-002',
    type: DictionaryType.DEVICE_CATEGORY,
    code: 'cooking',
    name: '烹饪设备',
    sort: 2,
    description: '炉灶、烤箱等烹饪设备',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-device-003',
    type: DictionaryType.DEVICE_CATEGORY,
    code: 'cleaning',
    name: '清洗设备',
    sort: 3,
    description: '洗碗机、水槽等清洗设备',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-device-004',
    type: DictionaryType.DEVICE_CATEGORY,
    code: 'ventilation',
    name: '通风设备',
    sort: 4,
    description: '排烟罩、风机等通风设备',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-device-005',
    type: DictionaryType.DEVICE_CATEGORY,
    code: 'storage',
    name: '储存设备',
    sort: 5,
    description: '货架储物柜等储存设备',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },

  // 档案分类
  {
    id: 'dict-archive-001',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'blueprints',
    name: '图纸资料',
    sort: 1,
    description: '建筑图纸、设计图等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-archive-002',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'equipment_manuals',
    name: '设备手册',
    sort: 2,
    description: '设备使用手册、维护手册等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-archive-003',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'maintenance_records',
    name: '维护记录',
    sort: 3,
    description: '设备维护记录、保养记录等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-archive-004',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'safety_docs',
    name: '安全文档',
    sort: 4,
    description: '安全操作规程、应急预案等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-archive-005',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'contracts',
    name: '合同协议',
    sort: 5,
    description: '采购合同、服务协议等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-archive-006',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'inspection_reports',
    name: '检测报告',
    sort: 6,
    description: '设备检测报告、验收报告等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-archive-007',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'air_quality',
    name: '空气质量',
    sort: 7,
    description: '空气质量监测数据、分析报告等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'dict-archive-008',
    type: DictionaryType.ARCHIVE_CATEGORY,
    code: 'video_monitoring',
    name: '视频监控',
    sort: 8,
    description: '视频监控录像、监控截图等',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
];

export function DictionaryProvider({ children }: { children: ReactNode }) {
  const [dictionaries, setDictionaries] = useState<DictionaryItem[]>(initialDictionaries);
  const [typeConfigs, setTypeConfigs] = useState<DictionaryTypeConfig[]>(DICTIONARY_TYPE_CONFIGS);

  const getAllItems = () => {
    return dictionaries;
  };

  const getItemsByType = (type: DictionaryTypeValue) => {
    return dictionaries
      .filter(item => item.type === type && item.status === 'active')
      .sort((a, b) => a.sort - b.sort);
  };

  const addItem = (item: Omit<DictionaryItem, 'id' | 'createdAt' | 'updatedAt'>) => {
    const newItem: DictionaryItem = {
      ...item,
      id: `dict-${Date.now()}`,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    setDictionaries(prev => [...prev, newItem]);
  };

  const updateItem = (id: string, updates: Partial<DictionaryItem>) => {
    setDictionaries(prev =>
      prev.map(item =>
        item.id === id
          ? { ...item, ...updates, updatedAt: new Date().toISOString() }
          : item
      )
    );
  };

  const deleteItem = (id: string) => {
    setDictionaries(prev => prev.filter(item => item.id !== id));
  };

  const getItemById = (id: string) => {
    return dictionaries.find(item => item.id === id);
  };

  const addTypeConfig = (config: DictionaryTypeConfig) => {
    setTypeConfigs(prev => [...prev, config]);
  };

  const updateTypeConfig = (key: string, config: Partial<DictionaryTypeConfig>) => {
    setTypeConfigs(prev =>
      prev.map(item =>
        item.key === key
          ? { ...item, ...config }
          : item
      )
    );
  };

  const deleteTypeConfig = (key: string) => {
    setTypeConfigs(prev => prev.filter(item => item.key !== key));
  };

  const getTypeConfig = (key: string) => {
    return typeConfigs.find(item => item.key === key);
  };

  return (
    <DictionaryContext.Provider
      value={{
        getAllItems,
        getItemsByType,
        addItem,
        updateItem,
        deleteItem,
        getItemById,
        typeConfigs,
        addTypeConfig,
        updateTypeConfig,
        deleteTypeConfig,
        getTypeConfig,
      }}
    >
      {children}
    </DictionaryContext.Provider>
  );
}

export function useDictionary() {
  const context = useContext(DictionaryContext);
  if (!context) {
    throw new Error('useDictionary must be used within DictionaryProvider');
  }
  return context;
}