import { createContext, useContext, useState, ReactNode, useMemo, useEffect, useCallback } from 'react';
import { useAuth } from './AuthContext';
import { useArea } from './AreaContext';
import { deviceApi } from '../services/api/deviceApi';
import { adaptDeviceDtoToDeviceItem, adaptCreateDeviceRequest, adaptUpdateDeviceRequest } from '../services/adapters';
import { DeviceDto, DeviceFilters } from '../services/api/types/device.types';

export interface DeviceItem {
  id: string;
  appCode: string;
  name: string;
  model?: string | null;
  serialNumber?: string | null;
  category?: string | null;
  location?: string | null;
  area: string; // Customer Name / Top Level Area Name (Display only)
  areaId?: string | null; // Linked Area ID for permission control
  areaName?: string | null;
  projectId?: string | null;
  projectName?: string | null;
  energyType: string[];
  status: 'online' | 'offline' | 'warning' | 'error' | 'maintenance';
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

interface DeviceContextType {
  devices: DeviceItem[];
  allDevices: DeviceItem[]; // 所有设备（未经过滤）
  loading: boolean;
  error: string | null;
  currentPage: number;
  totalPages: number;
  totalCount: number;
  
  // Actions
  addDevice: (device: Omit<DeviceItem, 'id' | 'createdAt' | 'updatedAt'>) => Promise<DeviceItem>;
  updateDevice: (id: string, updates: Partial<DeviceItem>) => Promise<DeviceItem>;
  deleteDevice: (id: string) => Promise<void>;
  refreshDevices: (page?: number, pageSize?: number, filters?: DeviceFilters) => Promise<void>;
  getDevice: (id: string) => Promise<DeviceItem>;
  getDevicesByArea: (areaId: string) => Promise<DeviceItem[]>;
  
  // 工具函数
  clearError: () => void;
  getDevicesByAppCode: (appCode: string) => DeviceItem[];
}

const DeviceContext = createContext<DeviceContextType | undefined>(undefined);

export function DeviceProvider({ children }: { children: ReactNode }) {
  const { currentCustomer, user } = useAuth();
  const { accessibleAreaIds } = useArea();
  
  const [allDevices, setAllDevices] = useState<DeviceItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [totalCount, setTotalCount] = useState(0);

  // 加载设备数据
  const loadDevices = useCallback(async (
    page: number = 1,
    pageSize: number = 20,
    filters?: DeviceFilters
  ) => {
    try {
      setLoading(true);
      setError(null);
      
      const response = await deviceApi.getDevices(page, pageSize, filters);
      
      if (response.data.code === 200) {
        const devices = response.data.data.items.map(adaptDeviceDtoToDeviceItem);
        setAllDevices(devices);
        setCurrentPage(response.data.data.page);
        setTotalPages(response.data.data.totalPages);
        setTotalCount(response.data.data.totalCount);
      } else {
        throw new Error(response.data.message || '加载设备列表失败');
      }
    } catch (err) {
      console.error('Failed to load devices:', err);
      setError(err instanceof Error ? err.message : '加载设备列表失败');
    } finally {
      setLoading(false);
    }
  }, []);

  // 根据当前客户和区域权限过滤设备
  const devices = useMemo(() => {
    return allDevices.filter((d) => {
      // 1. 必须匹配客户AppCode
      const matchesCustomer = currentCustomer ? d.appCode === currentCustomer.appCode : false;
      if (!matchesCustomer) return false;

      // 2. 必须在可访问的区域中
      if (d.areaId && accessibleAreaIds.length > 0) {
        return accessibleAreaIds.includes(d.areaId);
      }
      
      // 如果没有areaId或者用户没有区域限制，则显示所有设备
      return true;
    });
  }, [allDevices, currentCustomer, accessibleAreaIds]);

  // 初始化加载设备数据
  useEffect(() => {
    if (currentCustomer?.appCode || user?.role === 'super_admin') {
      loadDevices();
    }
  }, [currentCustomer, user, loadDevices]);

  const addDevice = async (deviceData: Omit<DeviceItem, 'id' | 'createdAt' | 'updatedAt'>): Promise<DeviceItem> => {
    try {
      setLoading(true);
      
      const createRequest = adaptCreateDeviceRequest(deviceData);
      const response = await deviceApi.createDevice(createRequest);
      
      if (response.data.code === 200) {
        const newDevice = adaptDeviceDtoToDeviceItem(response.data.data);
        setAllDevices(prev => [...prev, newDevice]);
        return newDevice;
      } else {
        throw new Error(response.data.message || '创建设备失败');
      }
    } catch (err) {
      console.error('Add device error:', err);
      setError(err instanceof Error ? err.message : '创建设备失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const updateDevice = async (id: string, updates: Partial<DeviceItem>): Promise<DeviceItem> => {
    try {
      setLoading(true);
      
      const updateRequest = adaptUpdateDeviceRequest(updates);
      const response = await deviceApi.updateDevice(id, updateRequest);
      
      if (response.data.code === 200) {
        const updatedDevice = adaptDeviceDtoToDeviceItem(response.data.data);
        setAllDevices(prev =>
          prev.map(d => d.id === id ? updatedDevice : d)
        );
        return updatedDevice;
      } else {
        throw new Error(response.data.message || '更新设备失败');
      }
    } catch (err) {
      console.error('Update device error:', err);
      setError(err instanceof Error ? err.message : '更新设备失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const deleteDevice = async (id: string): Promise<void> => {
    try {
      setLoading(true);
      
      const response = await deviceApi.deleteDevice(id);
      
      if (response.data.code === 200) {
        setAllDevices(prev => prev.filter(d => d.id !== id));
      } else {
        throw new Error(response.data.message || '删除设备失败');
      }
    } catch (err) {
      console.error('Delete device error:', err);
      setError(err instanceof Error ? err.message : '删除设备失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const refreshDevices = async (
    page: number = 1,
    pageSize: number = 20,
    filters?: DeviceFilters
  ): Promise<void> => {
    return loadDevices(page, pageSize, filters);
  };

  const getDevice = async (id: string): Promise<DeviceItem> => {
    try {
      setLoading(true);
      
      const response = await deviceApi.getDevice(id);
      
      if (response.data.code === 200) {
        return adaptDeviceDtoToDeviceItem(response.data.data);
      } else {
        throw new Error(response.data.message || '获取设备详情失败');
      }
    } catch (err) {
      console.error('Get device error:', err);
      setError(err instanceof Error ? err.message : '获取设备详情失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const getDevicesByArea = async (areaId: string): Promise<DeviceItem[]> => {
    try {
      setLoading(true);
      
      const response = await deviceApi.getDevicesByArea(areaId);
      
      if (response.data.code === 200) {
        return response.data.data.map(adaptDeviceDtoToDeviceItem);
      } else {
        throw new Error(response.data.message || '获取区域设备失败');
      }
    } catch (err) {
      console.error('Get devices by area error:', err);
      setError(err instanceof Error ? err.message : '获取区域设备失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const clearError = () => {
    setError(null);
  };

  const getDevicesByAppCode = (appCode: string): DeviceItem[] => {
    return allDevices.filter(d => d.appCode === appCode);
  };

  return (
    <DeviceContext.Provider
      value={{
        devices,
        allDevices,
        loading,
        error,
        currentPage,
        totalPages,
        totalCount,
        addDevice,
        updateDevice,
        deleteDevice,
        refreshDevices,
        getDevice,
        getDevicesByArea,
        clearError,
        getDevicesByAppCode,
      }}
    >
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
        loading: false,
        error: null,
        currentPage: 1,
        totalPages: 0,
        totalCount: 0,
        addDevice: async () => ({ id: '', appCode: '', name: '', energyType: [], status: 'offline', meterInstalled: false, createdAt: '', updatedAt: '' } as DeviceItem),
        updateDevice: async () => ({ id: '', appCode: '', name: '', energyType: [], status: 'offline', meterInstalled: false, createdAt: '', updatedAt: '' } as DeviceItem),
        deleteDevice: async () => {},
        refreshDevices: async () => {},
        getDevice: async () => ({ id: '', appCode: '', name: '', energyType: [], status: 'offline', meterInstalled: false, createdAt: '', updatedAt: '' } as DeviceItem),
        getDevicesByArea: async () => [],
        clearError: () => {},
        getDevicesByAppCode: () => []
      };
    }
    throw new Error('useDevices must be used within a DeviceProvider');
  }
  return context;
}