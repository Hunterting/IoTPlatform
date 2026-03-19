// 共享的档案数据

export interface DeviceMarker {
  id: string;
  name: string;
  x: number;
  y: number;
  type: string;
  model?: string;
  serialNumber?: string;
  sensors?: string[];
  specification?: string;
}

export interface Archive {
  id: string;
  name: string;
  appCode: string;
  type: 'document' | 'image' | 'pdf' | 'folder' | 'blueprint';
  size?: string;
  date: string;
  category: string;
  is3DModel?: boolean;
  devices?: DeviceMarker[];
  areaId?: string;
  areaName?: string;
  imageUrl?: string;
}

// 档案数据
export const archivesData: Archive[] = [
  {
    id: '1',
    name: '设备安装验收报告',
    appCode: 'HDLAPP001',
    type: 'pdf',
    size: '2.5 MB',
    date: '2025-01-15',
    category: '检测报告',
  },
  {
    id: '2',
    name: '厨房设备清单',
    appCode: 'HDLAPP001',
    type: 'document',
    size: '128 KB',
    date: '2025-01-10',
    category: '设备手册',
  },
  {
    id: '3',
    name: '电气系统布线图',
    appCode: 'HDLAPP001',
    type: 'blueprint',
    size: '4.2 MB',
    date: '2025-01-08',
    category: '图纸资料',
    is3DModel: true,
    devices: [
      { id: 'd1', name: '智能冰箱-A01', x: 25, y: 30, type: '冷藏设备', model: 'SR-F520BX', serialNumber: 'SN20250101001', sensors: ['温度传感器', '湿度传感器'] },
      { id: 'd2', name: '智能烤箱-B01', x: 60, y: 45, type: '烹饪设备', model: 'OV-X8000', serialNumber: 'SN20250101002', sensors: ['温度传感器'] },
      { id: 'd3', name: '智能炉灶-C01', x: 40, y: 60, type: '烹饪设备', model: 'GS-P4000', serialNumber: 'SN20250101003', sensors: ['用气传感器', '温度传感器'] },
      { id: 'd4', name: '洗碗机-D01', x: 75, y: 70, type: '清洁设备', model: 'DW-M9000', serialNumber: 'SN20250101004', sensors: ['用水传感器'] },
    ],
  },
  {
    id: '4',
    name: '设备维护记录',
    appCode: 'HDLAPP001',
    type: 'document',
    size: '256 KB',
    date: '2025-01-20',
    category: '维护记录',
  },
  {
    id: '5',
    name: '厨房平面布局图',
    appCode: 'HDLAPP001',
    type: 'blueprint',
    size: '3.8 MB',
    date: '2025-01-05',
    category: '图纸资料',
    is3DModel: true,
    areaId: 'L2-001',
    areaName: '一层大厅区',
    devices: [
      { id: 'd5', name: '咖啡机-E01', x: 30, y: 40, type: '饮品设备', model: 'CF-L5000', serialNumber: 'SN20250101005', sensors: ['用水传感器'] },
      { id: 'd6', name: '蒸箱-F01', x: 70, y: 50, type: '烹饪设备', model: 'ST-Z3000', serialNumber: 'SN20250101006', sensors: ['用气传感器', '温度传感器'] },
    ],
  },
  {
    id: '6',
    name: '消防安全检查表',
    appCode: 'HDLAPP001',
    type: 'pdf',
    size: '1.2 MB',
    date: '2025-01-18',
    category: '安全文档',
  },
  {
    id: '7',
    name: '设备操作手册',
    appCode: 'HDLAPP001',
    type: 'folder',
    date: '2025-01-01',
    category: '设备手册',
  },
  {
    id: '8',
    name: '能耗分析报告',
    appCode: 'HDLAPP001',
    type: 'pdf',
    size: '1.8 MB',
    date: '2025-01-22',
    category: '检测报告',
  },
  {
    id: '9',
    name: '一层大厅区空气质量图纸',
    appCode: 'HDLAPP001',
    type: 'blueprint',
    size: '5.2 MB',
    date: '2025-02-01',
    category: '空气质量',
    is3DModel: true,
    areaId: 'L2-001',
    areaName: '一层大厅区',
    imageUrl: 'https://images.unsplash.com/photo-1738168299283-4117c3dfb8ac?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxtb2Rlcm4lMjBraXRjaGVuJTIwdmVudGlsYXRpb24lMjBzeXN0ZW18ZW58MXx8fHwxNzczNzM5MjIwfDA&ixlib=rb-4.1.0&q=80&w=1080',
    devices: [
      { id: 'd7', name: '新风机-AQ01', x: 20, y: 25, type: '新风设备', model: 'XF-8000', serialNumber: 'SN20250201001', sensors: ['风量传感器', '温度传感器', 'PM2.5传感器'] },
      { id: 'd8', name: '排风机-AQ02', x: 80, y: 25, type: '排风设备', model: 'PF-6000', serialNumber: 'SN20250201002', sensors: ['风量传感器'] },
    ],
  },
  {
    id: '10',
    name: 'A区用餐区空气质量图纸',
    appCode: 'HDLAPP001',
    type: 'blueprint',
    size: '4.8 MB',
    date: '2025-02-02',
    category: '空气质量',
    is3DModel: true,
    areaId: 'L2-002',
    areaName: 'A区用餐区',
    imageUrl: 'https://images.unsplash.com/photo-1761990386557-fde67968209c?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxyZXN0YXVyYW50JTIwZGluaW5nJTIwYXJlYSUyMGNlaWxpbmclMjBhaXJmbG93fGVufDF8fHx8MTc3MzczOTIyM3ww&ixlib=rb-4.1.0&q=80&w=1080',
    devices: [
      { id: 'd9', name: '新风机-AQ03', x: 25, y: 30, type: '新风设备', model: 'XF-8000', serialNumber: 'SN20250201003', sensors: ['风量传感器', '温度传感器', 'PM2.5传感器'] },
      { id: 'd10', name: '排风机-AQ04', x: 75, y: 30, type: '排风设备', model: 'PF-6000', serialNumber: 'SN20250201004', sensors: ['风量传感器'] },
      { id: 'd11', name: '油烟净化器-AQ05', x: 50, y: 60, type: '净化设备', model: 'YJ-5000', serialNumber: 'SN20250201005', sensors: ['油烟浓度传感器'] },
    ],
  },
];