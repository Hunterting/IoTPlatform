// 设备数据适配器
import { adaptIdFromBackend, adaptIdToBackend, adaptDateTime } from './index';
import { 
  DeviceDto, 
  DeviceSensorDto, 
  DeviceDetailDto,
  DeviceStatus,
  DeviceCategory
} from '../api/types/device.types';

// 后端响应类型接口
interface BackendDeviceDto {
  id: number;
  appCode: string;
  name: string;
  model?: string | null;
  serialNumber?: string | null;
  category?: string | null;
  location?: string | null;
  areaId?: number | null;
  areaName?: string | null;
  projectId?: number | null;
  projectName?: string | null;
  energyTypes?: string | null;
  status: string;
  installDate?: string | null;
  lastMaintenance?: string | null;
  supplier?: string | null;
  warrantyDate?: string | null;
  power?: number | null;
  voltage?: string | null;
  meterInstalled: boolean;
  createdAt: string;
  updatedAt: string;
}

interface BackendDeviceSensorDto {
  id: number;
  deviceId: number;
  name: string;
  sensorType?: string | null;
  lastValue?: string | null;
  unit?: string | null;
}

interface BackendDeviceDetailDto extends BackendDeviceDto {
  sensors?: BackendDeviceSensorDto[] | null;
}

/**
 * 将后端设备信息转换为前端格式
 */
export const adaptDeviceFromBackend = (backendDevice: BackendDeviceDto): DeviceDto => {
  return {
    id: adaptIdFromBackend(backendDevice.id),
    appCode: backendDevice.appCode,
    name: backendDevice.name,
    model: backendDevice.model,
    serialNumber: backendDevice.serialNumber,
    category: backendDevice.category,
    location: backendDevice.location,
    areaId: adaptIdFromBackend(backendDevice.areaId),
    areaName: backendDevice.areaName,
    projectId: adaptIdFromBackend(backendDevice.projectId),
    projectName: backendDevice.projectName,
    energyTypes: backendDevice.energyTypes,
    status: backendDevice.status,
    installDate: adaptDateTime(backendDevice.installDate),
    lastMaintenance: adaptDateTime(backendDevice.lastMaintenance),
    supplier: backendDevice.supplier,
    warrantyDate: adaptDateTime(backendDevice.warrantyDate),
    power: backendDevice.power,
    voltage: backendDevice.voltage,
    meterInstalled: backendDevice.meterInstalled,
    createdAt: adaptDateTime(backendDevice.createdAt),
    updatedAt: adaptDateTime(backendDevice.updatedAt)
  };
};

/**
 * 将后端设备详情转换为前端格式
 */
export const adaptDeviceDetailFromBackend = (backendDevice: BackendDeviceDetailDto): DeviceDetailDto => {
  const deviceDto = adaptDeviceFromBackend(backendDevice);
  
  return {
    ...deviceDto,
    sensors: backendDevice.sensors?.map(sensor => adaptDeviceSensorFromBackend(sensor)) || null
  };
};

/**
 * 将后端设备传感器信息转换为前端格式
 */
export const adaptDeviceSensorFromBackend = (backendSensor: BackendDeviceSensorDto): DeviceSensorDto => {
  return {
    id: adaptIdFromBackend(backendSensor.id),
    deviceId: adaptIdFromBackend(backendSensor.deviceId),
    name: backendSensor.name,
    sensorType: backendSensor.sensorType,
    lastValue: backendSensor.lastValue,
    unit: backendSensor.unit
  };
};

/**
 * 将前端设备信息转换为后端格式
 */
export const adaptDeviceToBackend = (device: Partial<DeviceDto>): Partial<BackendDeviceDto> => {
  const backendDevice: Partial<BackendDeviceDto> = {};
  
  if (device.id !== undefined) {
    backendDevice.id = adaptIdToBackend(device.id) as number;
  }
  if (device.appCode !== undefined) {
    backendDevice.appCode = device.appCode;
  }
  if (device.name !== undefined) {
    backendDevice.name = device.name;
  }
  if (device.model !== undefined) {
    backendDevice.model = device.model;
  }
  if (device.serialNumber !== undefined) {
    backendDevice.serialNumber = device.serialNumber;
  }
  if (device.category !== undefined) {
    backendDevice.category = device.category;
  }
  if (device.location !== undefined) {
    backendDevice.location = device.location;
  }
  if (device.areaId !== undefined) {
    backendDevice.areaId = adaptIdToBackend(device.areaId);
  }
  if (device.projectId !== undefined) {
    backendDevice.projectId = adaptIdToBackend(device.projectId);
  }
  if (device.energyTypes !== undefined) {
    backendDevice.energyTypes = device.energyTypes;
  }
  if (device.status !== undefined) {
    backendDevice.status = device.status;
  }
  if (device.installDate !== undefined) {
    backendDevice.installDate = device.installDate;
  }
  if (device.lastMaintenance !== undefined) {
    backendDevice.lastMaintenance = device.lastMaintenance;
  }
  if (device.supplier !== undefined) {
    backendDevice.supplier = device.supplier;
  }
  if (device.warrantyDate !== undefined) {
    backendDevice.warrantyDate = device.warrantyDate;
  }
  if (device.power !== undefined) {
    backendDevice.power = device.power;
  }
  if (device.voltage !== undefined) {
    backendDevice.voltage = device.voltage;
  }
  if (device.meterInstalled !== undefined) {
    backendDevice.meterInstalled = device.meterInstalled;
  }
  
  return backendDevice;
};

/**
 * 将前端创建设备请求转换为后端格式
 */
export const adaptCreateDeviceToBackend = (device: any): any => {
  const backendDevice: any = { ...device };
  
  // 转换ID字段
  if (backendDevice.areaId !== undefined) {
    backendDevice.areaId = adaptIdToBackend(backendDevice.areaId);
  }
  if (backendDevice.projectId !== undefined) {
    backendDevice.projectId = adaptIdToBackend(backendDevice.projectId);
  }
  
  return backendDevice;
};

/**
 * 将前端更新设备请求转换为后端格式
 */
export const adaptUpdateDeviceToBackend = (device: any): any => {
  const backendDevice: any = { ...device };
  
  // 转换ID字段
  if (backendDevice.areaId !== undefined) {
    backendDevice.areaId = adaptIdToBackend(backendDevice.areaId);
  }
  if (backendDevice.projectId !== undefined) {
    backendDevice.projectId = adaptIdToBackend(backendDevice.projectId);
  }
  
  return backendDevice;
};

/**
 * 验证设备状态
 */
export const validateDeviceStatus = (status: string): DeviceStatus => {
  const validStatuses = Object.values(DeviceStatus);
  return validStatuses.includes(status as DeviceStatus) ? status as DeviceStatus : DeviceStatus.OFFLINE;
};

/**
 * 验证设备分类
 */
export const validateDeviceCategory = (category: string): DeviceCategory => {
  const validCategories = Object.values(DeviceCategory);
  return validCategories.includes(category as DeviceCategory) ? category as DeviceCategory : DeviceCategory.OTHER;
};

/**
 * 获取设备状态颜色
 */
export const getDeviceStatusColor = (status: DeviceStatus): string => {
  const statusColors: Record<DeviceStatus, string> = {
    [DeviceStatus.ONLINE]: 'green',
    [DeviceStatus.OFFLINE]: 'gray',
    [DeviceStatus.WARNING]: 'orange',
    [DeviceStatus.ERROR]: 'red',
    [DeviceStatus.MAINTENANCE]: 'blue'
  };
  
  return statusColors[status] || 'gray';
};

/**
 * 获取设备状态文本
 */
export const getDeviceStatusText = (status: DeviceStatus): string => {
  const statusTexts: Record<DeviceStatus, string> = {
    [DeviceStatus.ONLINE]: '在线',
    [DeviceStatus.OFFLINE]: '离线',
    [DeviceStatus.WARNING]: '警告',
    [DeviceStatus.ERROR]: '故障',
    [DeviceStatus.MAINTENANCE]: '维护中'
  };
  
  return statusTexts[status] || '未知';
};

/**
 * 获取设备分类图标
 */
export const getDeviceCategoryIcon = (category: DeviceCategory): string => {
  const categoryIcons: Record<DeviceCategory, string> = {
    [DeviceCategory.POWER]: 'bolt',
    [DeviceCategory.LIGHTING]: 'lightbulb',
    [DeviceCategory.HVAC]: 'thermometer',
    [DeviceCategory.SECURITY]: 'shield',
    [DeviceCategory.NETWORK]: 'wifi',
    [DeviceCategory.OTHER]: 'device'
  };
  
  return categoryIcons[category] || 'device';
};

/**
 * 获取设备分类文本
 */
export const getDeviceCategoryText = (category: DeviceCategory): string => {
  const categoryTexts: Record<DeviceCategory, string> = {
    [DeviceCategory.POWER]: '电力设备',
    [DeviceCategory.LIGHTING]: '照明设备',
    [DeviceCategory.HVAC]: '暖通空调',
    [DeviceCategory.SECURITY]: '安防设备',
    [DeviceCategory.NETWORK]: '网络设备',
    [DeviceCategory.OTHER]: '其他设备'
  };
  
  return categoryTexts[category] || '其他设备';
};

// ------------------------------------------------------------------------------
// DeviceContext 专用适配器函数
// ------------------------------------------------------------------------------

/**
 * 将设备DTO转换为DeviceContext的DeviceItem格式
 */
export const adaptDeviceDtoToDeviceItem = (deviceDto: DeviceDto): any => {
  // 解析energyTypes — 后端存储为 JSON 数组字符串，如 ["electric","water"]
  // 兼容旧数据可能为逗号分隔字符串
  let energyTypeArray: string[] = ['electric'];
  if (deviceDto.energyTypes) {
    const raw = deviceDto.energyTypes.trim();
    try {
      // 优先尝试 JSON 解析（后端 NormalizeEnergyTypesJson 统一存为 JSON 数组）
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) {
        energyTypeArray = parsed.filter((t: any) => typeof t === 'string' && t);
      }
    } catch {
      // 非 JSON，按逗号分隔解析（兼容旧数据）
      energyTypeArray = raw.split(',').map(t => t.trim()).filter(t => t);
    }
    if (energyTypeArray.length === 0) energyTypeArray = ['electric'];
  }

  return {
    id: deviceDto.id,
    appCode: deviceDto.appCode,
    name: deviceDto.name,
    model: deviceDto.model,
    serialNumber: deviceDto.serialNumber,
    category: deviceDto.category,
    location: deviceDto.location,
    area: deviceDto.areaName || '未分配区域',
    areaId: deviceDto.areaId,
    areaName: deviceDto.areaName,
    projectId: deviceDto.projectId,
    projectName: deviceDto.projectName,
    energyType: energyTypeArray,
    status: deviceDto.status as 'online' | 'offline' | 'warning' | 'error' | 'maintenance',
    installDate: deviceDto.installDate,
    lastMaintenance: deviceDto.lastMaintenance,
    supplier: deviceDto.supplier,
    warrantyDate: deviceDto.warrantyDate,
    power: deviceDto.power,
    voltage: deviceDto.voltage,
    meterInstalled: deviceDto.meterInstalled,
    createdAt: deviceDto.createdAt,
    updatedAt: deviceDto.updatedAt
  };
};

/**
 * 将DeviceItem格式转换为创建设备请求
 */
export const adaptCreateDeviceRequest = (
  deviceItem: Omit<any, 'id' | 'createdAt' | 'updatedAt'>
): any => {
  // 将energyType数组转换为逗号分隔的字符串
  const energyTypes = deviceItem.energyType?.join(',') || 'electric';

  return {
    name: deviceItem.name,
    appCode: deviceItem.appCode,
    model: deviceItem.model,
    serialNumber: deviceItem.serialNumber,
    category: deviceItem.category,
    location: deviceItem.location,
    areaId: deviceItem.areaId ? Number(deviceItem.areaId) : null,
    projectId: deviceItem.projectId ? Number(deviceItem.projectId) : null,
    projectName: deviceItem.projectName,
    energyTypes: energyTypes,
    status: deviceItem.status || 'offline',
    installDate: deviceItem.installDate,
    lastMaintenance: deviceItem.lastMaintenance,
    supplier: deviceItem.supplier,
    warrantyDate: deviceItem.warrantyDate,
    power: deviceItem.power,
    voltage: deviceItem.voltage,
    meterInstalled: deviceItem.meterInstalled || false
  };
};

/**
 * 将DeviceItem更新数据转换为更新设备的请求
 */
export const adaptUpdateDeviceRequest = (
  updates: Partial<any>
): any => {
  const request: any = {};
  
  if (updates.name !== undefined) request.name = updates.name;
  if (updates.model !== undefined) request.model = updates.model;
  if (updates.serialNumber !== undefined) request.serialNumber = updates.serialNumber;
  if (updates.category !== undefined) request.category = updates.category;
  if (updates.location !== undefined) request.location = updates.location;
  if (updates.areaId !== undefined) request.areaId = updates.areaId ? Number(updates.areaId) : null;
  if (updates.projectId !== undefined) request.projectId = updates.projectId ? Number(updates.projectId) : null;
  if (updates.projectName !== undefined) request.projectName = updates.projectName;
  if (updates.energyType !== undefined) request.energyTypes = updates.energyType.join(',');
  if (updates.status !== undefined) request.status = updates.status;
  if (updates.installDate !== undefined) request.installDate = updates.installDate;
  if (updates.lastMaintenance !== undefined) request.lastMaintenance = updates.lastMaintenance;
  if (updates.supplier !== undefined) request.supplier = updates.supplier;
  if (updates.warrantyDate !== undefined) request.warrantyDate = updates.warrantyDate;
  if (updates.power !== undefined) request.power = updates.power;
  if (updates.voltage !== undefined) request.voltage = updates.voltage;
  if (updates.meterInstalled !== undefined) request.meterInstalled = updates.meterInstalled;
  
  return request;
};