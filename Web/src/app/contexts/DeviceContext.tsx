import { createContext, useContext, useState, ReactNode, useMemo } from 'react';
import { useAuth } from './AuthContext';
import { useArea } from './AreaContext';

export interface DeviceItem {
  id: string;
  appCode: string;
  name: string;
  model: string;
  serialNumber: string;
  category: string;
  location: string;
  area: string; // Customer Name / Top Level Area Name (Display only)
  areaId: string; // Linked Area ID for permission control
  projectId?: string;   // 所属项目 ID
  projectName?: string; // 所属项目名称（冗余存储，便于展示）
  energyType: ('electric' | 'gas' | 'water')[];
  status: 'online' | 'offline' | 'warning';
  installDate: string;
  lastMaintenance: string;
  supplier?: string;
  warrantyDate?: string;
  power?: number;
  voltage?: string;
  meterInstalled: boolean;
}

interface DeviceContextType {
  devices: DeviceItem[];
  allDevices: DeviceItem[]; // Unfiltered list if needed for admin purposes, but usually hidden
  addDevice: (device: Partial<DeviceItem>) => void;
  updateDevice: (device: DeviceItem) => void;
  deleteDevice: (id: string) => void;
  getDevicesByAppCode: (appCode: string) => DeviceItem[];
}

const DeviceContext = createContext<DeviceContextType | undefined>(undefined);

const INITIAL_DEVICES: DeviceItem[] = [
  {
    id: '1',
    appCode: 'HDLAPP001',
    name: '智能冰箱-A01',
    model: 'SR-F520BX',
    serialNumber: 'SN20250101001',
    category: '冷藏设备',
    location: '冷库',
    area: '海底捞火锅连锁',
    areaId: 'L3-004',
    energyType: ['electric'],
    status: 'online',
    installDate: '2025-01-01',
    lastMaintenance: '2025-12-15',
    supplier: '海尔集团',
    warrantyDate: '2027-01-01',
    power: 120,
    voltage: '220V',
    meterInstalled: true,
  },
  {
    id: '2',
    appCode: 'HDLAPP001',
    name: '智能烤箱-B01',
    model: 'OV-X8000',
    serialNumber: 'SN20250101002',
    category: '烹饪设备',
    location: '中央厨房',
    area: '海底捞火锅连锁',
    areaId: 'L3-003',
    energyType: ['electric'],
    status: 'online',
    installDate: '2025-01-05',
    lastMaintenance: '2025-12-20',
    supplier: '西门子',
    warrantyDate: '2027-01-05',
    power: 3000,
    voltage: '380V',
    meterInstalled: true,
  },
  {
    id: '3',
    appCode: 'HDLAPP001',
    name: '智能炉灶-C01',
    model: 'GS-P4000',
    serialNumber: 'SN20250101003',
    category: '烹饪设备',
    location: '中央厨房',
    area: '海底捞火锅连锁',
    areaId: 'L3-003',
    energyType: ['gas', 'electric'],
    status: 'warning',
    installDate: '2024-12-20',
    lastMaintenance: '2025-11-30',
    supplier: '林内',
    warrantyDate: '2026-12-20',
    power: 4000,
    meterInstalled: true,
  },
  {
    id: '4',
    appCode: 'HDLAPP001',
    name: '洗碗机-D01',
    model: 'DW-M9000',
    serialNumber: 'SN20250101004',
    category: '清洁设备',
    location: '二层后厨区',
    area: '海底捞火锅连锁',
    areaId: 'L2-002',
    energyType: ['electric', 'water'],
    status: 'online',
    installDate: '2025-01-10',
    lastMaintenance: '2026-01-05',
    supplier: '博世',
    warrantyDate: '2027-01-10',
    power: 1800,
    voltage: '220V',
    meterInstalled: true,
  },
  {
    id: '5',
    appCode: 'HDLAPP001',
    name: '咖啡机-E01',
    model: 'CF-L5000',
    serialNumber: 'SN20250101005',
    category: '饮品设备',
    location: '饮品制作台',
    area: '海底捞火锅连锁',
    areaId: 'L3-002',
    energyType: ['electric', 'water'],
    status: 'offline',
    installDate: '2025-01-15',
    lastMaintenance: '2026-01-10',
    supplier: 'De\'Longhi',
    warrantyDate: '2027-01-15',
    power: 1200,
    voltage: '220V',
    meterInstalled: false,
  },
  // XCAPP002 Devices
  {
    id: '6',
    appCode: 'XCAPP002',
    name: '制冰机-I01',
    model: 'ICE-2000',
    serialNumber: 'SN20250201001',
    category: '冷藏设备',
    location: '饮品制作区',
    area: '喜茶（上海世纪大道店）',
    areaId: 'L1-002',
    energyType: ['electric', 'water'],
    status: 'online',
    installDate: '2025-02-01',
    lastMaintenance: '2025-02-15',
    supplier: 'Hoshizaki',
    warrantyDate: '2028-02-01',
    power: 1500,
    voltage: '220V',
    meterInstalled: true,
  },
  {
    id: '7',
    appCode: 'XCAPP002',
    name: '咖啡机-XC01',
    model: 'La Marzocco GB5',
    serialNumber: 'SN20250201002',
    category: '饮品设备',
    location: '前台点单区',
    area: '喜茶（上海世纪大道店）',
    areaId: 'L1-002',
    energyType: ['electric', 'water'],
    status: 'online',
    installDate: '2025-02-01',
    lastMaintenance: '2025-02-10',
    supplier: 'La Marzocco',
    warrantyDate: '2028-02-01',
    power: 3000,
    voltage: '220V',
    meterInstalled: true,
  },
   {
    id: '8',
    appCode: 'XCAPP002',
    name: '冷藏展示柜-XC02',
    model: 'SC-1200',
    serialNumber: 'SN20250201003',
    category: '冷藏设备',
    location: '前台点单区',
    area: '喜茶（上海世纪大道店）',
    areaId: 'L1-002',
    energyType: ['electric'],
    status: 'online',
    installDate: '2025-02-02',
    lastMaintenance: '',
    supplier: '星星冷链',
    warrantyDate: '2027-02-01',
    power: 800,
    voltage: '220V',
    meterInstalled: true,
  },
];

export function DeviceProvider({ children }: { children: ReactNode }) {
  const { currentCustomer } = useAuth();
  const { accessibleAreaIds } = useArea();
  const [allDevices, setAllDevices] = useState<DeviceItem[]>(INITIAL_DEVICES);

  // Derived state: filter devices by current customer's appCode AND accessible areas
  const devices = useMemo(() => {
    return allDevices.filter((d) => {
      // 1. Must match Customer App Code
      const matchesCustomer = currentCustomer ? d.appCode === currentCustomer.appCode : false;
      if (!matchesCustomer) return false;

      // 2. Must be in an accessible area
      // accessibleAreaIds contains the full list of allowed IDs (including implicit children)
      // If accessibleAreaIds is empty, it might mean "loading" or "no access", but AreaContext usually returns full list for admins.
      // If AreaContext returns empty list, it means user sees nothing.
      return accessibleAreaIds.includes(d.areaId);
    });
  }, [allDevices, currentCustomer, accessibleAreaIds]);

  const addDevice = (deviceData: Partial<DeviceItem>) => {
    if (!currentCustomer) return;

    const newDevice: DeviceItem = {
      id: Date.now().toString(),
      appCode: currentCustomer.appCode,
      name: deviceData.name || '',
      model: deviceData.model || '',
      serialNumber: deviceData.serialNumber || '',
      category: deviceData.category || '',
      location: deviceData.location || '',
      area: currentCustomer.name, // Default to customer name
      areaId: deviceData.areaId || '', // Should be provided
      projectId: deviceData.projectId,
      projectName: deviceData.projectName,
      energyType: deviceData.energyType || ['electric'],
      status: 'offline',
      installDate: deviceData.installDate || new Date().toISOString().split('T')[0],
      lastMaintenance: deviceData.lastMaintenance || '',
      supplier: deviceData.supplier,
      warrantyDate: deviceData.warrantyDate,
      power: deviceData.power,
      voltage: deviceData.voltage,
      meterInstalled: deviceData.meterInstalled || false,
    };

    setAllDevices([...allDevices, newDevice]);
  };

  const updateDevice = (updatedDevice: DeviceItem) => {
    setAllDevices(allDevices.map((d) => (d.id === updatedDevice.id ? updatedDevice : d)));
  };

  const deleteDevice = (id: string) => {
    setAllDevices(allDevices.filter((d) => d.id !== id));
  };

  const getDevicesByAppCode = (appCode: string) => {
    return allDevices.filter(d => d.appCode === appCode);
  }

  return (
    <DeviceContext.Provider value={{ devices, allDevices, addDevice, updateDevice, deleteDevice, getDevicesByAppCode }}>
      {children}
    </DeviceContext.Provider>
  );
}

export function useDevices() {
  const context = useContext(DeviceContext);
  if (context === undefined) {
    if (import.meta.env.DEV) {
        console.warn('useDevices called outside of DeviceProvider');
        return {
            devices: [],
            allDevices: [],
            addDevice: () => {},
            updateDevice: () => {},
            deleteDevice: () => {},
            getDevicesByAppCode: () => []
        }
    }
    throw new Error('useDevices must be used within a DeviceProvider');
  }
  return context;
}