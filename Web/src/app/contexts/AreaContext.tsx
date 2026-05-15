import { createContext, useContext, useState, ReactNode, useMemo, useEffect, useCallback } from 'react';
import { useAuth } from './AuthContext';
import { areaApi } from '../services/api/areaApi';
import { adaptAreaDtoToArea, adaptAreaTreeNodeDtoToArea, adaptCreateAreaRequest, adaptUpdateAreaRequest } from '../services/adapters';
import { AreaDto, AreaTreeNodeDto, AreaFilters } from '../services/api/types/area.types';

// Data Structures
export interface AreaDevice {
  id: string;
  name: string;
  type: string;
  status: 'online' | 'offline' | 'warning';
  x: number; // Percentage
  y: number; // Percentage
}

export interface Area {
  id: string;
  name: string;
  type: 'level1' | 'level2' | 'level3' | string;
  image?: string | null;
  parentId?: string | null;
  parentName?: string | null;
  customerId?: string | null;
  customerName?: string | null;
  appCode?: string | null;
  description?: string | null;
  deviceCount: number;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  children?: Area[];
  devices?: AreaDevice[];
}

interface AreaContextType {
  // 状态
  areas: Area[];
  allAreas: Area[];
  areaTree: Area[];
  loading: boolean;
  error: string | null;
  currentPage: number;
  totalPages: number;
  totalCount: number;
  accessibleAreaIds: string[]; // 用户可访问的所有区域ID列表
  
  // 操作
  refreshAreas: (page?: number, pageSize?: number, filters?: AreaFilters) => Promise<void>;
  refreshAreaTree: () => Promise<void>;
  getArea: (id: string) => Promise<Area>;
  getChildAreas: (parentId: string) => Promise<Area[]>;
  addArea: (area: Omit<Area, 'id' | 'deviceCount' | 'createdAt' | 'updatedAt' | 'children' | 'devices'>) => Promise<Area>;
  updateArea: (id: string, updates: Partial<Area>) => Promise<Area>;
  deleteArea: (id: string) => Promise<void>;
  
  // 工具函数
  clearError: () => void;
  setAreas: (areas: Area[]) => void;
  getAreasByCustomerId: (customerId: string) => Area[];
  flattenAreas: (areas: Area[]) => Area[];
  buildAreaTree: (areaList: Area[]) => Area[];
}

const AreaContext = createContext<AreaContextType | undefined>(undefined);

export function AreaProvider({ children }: { children: ReactNode }) {
  const { currentCustomer, user } = useAuth();
  
  const [allAreas, setAllAreas] = useState<Area[]>([]);
  const [areaTree, setAreaTree] = useState<Area[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [totalCount, setTotalCount] = useState(0);

  // 加载区域列表
  const loadAreas = useCallback(async (
    page: number = 1,
    pageSize: number = 20,
    filters?: AreaFilters
  ) => {
    try {
      setLoading(true);
      setError(null);
      
      const response = await areaApi.getAreas(page, pageSize, filters);
      
      if (response.data.code === 200) {
        const areas = response.data.data.items.map(adaptAreaDtoToArea);
        setAllAreas(areas);
        setCurrentPage(response.data.data.page);
        setTotalPages(response.data.data.totalPages);
        setTotalCount(response.data.data.totalCount);
      } else {
        throw new Error(response.data.message || '加载区域列表失败');
      }
    } catch (err) {
      console.error('Failed to load areas:', err);
      setError(err instanceof Error ? err.message : '加载区域列表失败');
    } finally {
      setLoading(false);
    }
  }, []);

  // 加载区域树
  const loadAreaTree = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      
      const response = await areaApi.getAreaTree();
      
      if (response.data.code === 200) {
        const tree = response.data.data.map(adaptAreaTreeNodeDtoToArea);
        setAreaTree(tree);
      } else {
        throw new Error(response.data.message || '加载区域树失败');
      }
    } catch (err) {
      console.error('Failed to load area tree:', err);
      setError(err instanceof Error ? err.message : '加载区域树失败');
    } finally {
      setLoading(false);
    }
  }, []);

  // 根据当前客户过滤区域
  const areas = useMemo(() => {
    if (!currentCustomer?.appCode && user?.role !== 'super_admin') {
      return [];
    }
    
    return allAreas.filter(area => {
      // 超级管理员可以看到所有区域
      if (user?.role === 'super_admin') {
        return true;
      }
      
      // 其他用户只能看到自己客户下的区域
      return area.appCode === currentCustomer?.appCode;
    });
  }, [allAreas, currentCustomer, user]);

  // 确定用户可访问的区域ID（基于RBAC）
  const accessibleAreaIds = useMemo(() => {
    if (!user) {
      return [];
    }

    // 超级管理员可以访问所有区域
    if (user.role === 'super_admin') {
      return allAreas.map(area => area.id);
    }

    // 如果用户有指定的允许区域ID列表
    if (user.allowedAreaIds && user.allowedAreaIds.length > 0) {
      const accessibleIds = new Set<string>();
      
      // 递归收集所有允许的区域及其子区域
      const collectAccessibleIds = (areaList: Area[]) => {
        areaList.forEach(area => {
          if (user.allowedAreaIds?.includes(area.id)) {
            accessibleIds.add(area.id);
            // 如果该区域有子区域，也添加它们
            if (area.children) {
              collectAccessibleIds(area.children);
            }
          } else if (area.children) {
            // 检查子区域是否允许访问
            collectAccessibleIds(area.children);
          }
        });
      };
      
      collectAccessibleIds(areaTree);
      return Array.from(accessibleIds);
    }

    // 默认情况下，用户可以访问自己客户下的所有区域
    return areas.map(area => area.id);
  }, [allAreas, areaTree, user, areas]);

  // 初始化加载数据
  useEffect(() => {
    if (currentCustomer?.appCode || user?.role === 'super_admin') {
      loadAreas();
      loadAreaTree();
    }
  }, [currentCustomer, user, loadAreas, loadAreaTree]);

  const refreshAreas = async (
    page: number = 1,
    pageSize: number = 20,
    filters?: AreaFilters
  ): Promise<void> => {
    return loadAreas(page, pageSize, filters);
  };

  const refreshAreaTree = async (): Promise<void> => {
    return loadAreaTree();
  };

  const getArea = async (id: string): Promise<Area> => {
    try {
      setLoading(true);
      
      const response = await areaApi.getArea(id);
      
      if (response.data.code === 200) {
        return adaptAreaDtoToArea(response.data.data);
      } else {
        throw new Error(response.data.message || '获取区域详情失败');
      }
    } catch (err) {
      console.error('Get area error:', err);
      setError(err instanceof Error ? err.message : '获取区域详情失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const getChildAreas = async (parentId: string): Promise<Area[]> => {
    try {
      setLoading(true);
      
      const response = await areaApi.getChildAreas(parentId);
      
      if (response.data.code === 200) {
        return response.data.data.map(adaptAreaDtoToArea);
      } else {
        throw new Error(response.data.message || '获取子区域失败');
      }
    } catch (err) {
      console.error('Get child areas error:', err);
      setError(err instanceof Error ? err.message : '获取子区域失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const addArea = async (areaData: Omit<Area, 'id' | 'deviceCount' | 'createdAt' | 'updatedAt' | 'children' | 'devices'>): Promise<Area> => {
    try {
      setLoading(true);
      
      const createRequest = adaptCreateAreaRequest(areaData);
      const response = await areaApi.createArea(createRequest);
      
      if (response.data.code === 200) {
        const newArea = adaptAreaDtoToArea(response.data.data);
        setAllAreas(prev => [...prev, newArea]);
        // 刷新区域树
        await loadAreaTree();
        return newArea;
      } else {
        throw new Error(response.data.message || '创建区域失败');
      }
    } catch (err) {
      console.error('Add area error:', err);
      setError(err instanceof Error ? err.message : '创建区域失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const updateArea = async (id: string, updates: Partial<Area>): Promise<Area> => {
    try {
      setLoading(true);
      
      const updateRequest = adaptUpdateAreaRequest(updates);
      const response = await areaApi.updateArea(id, updateRequest);
      
      if (response.data.code === 200) {
        const updatedArea = adaptAreaDtoToArea(response.data.data);
        setAllAreas(prev =>
          prev.map(area => area.id === id ? updatedArea : area)
        );
        // 刷新区域树
        await loadAreaTree();
        return updatedArea;
      } else {
        throw new Error(response.data.message || '更新区域失败');
      }
    } catch (err) {
      console.error('Update area error:', err);
      setError(err instanceof Error ? err.message : '更新区域失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const deleteArea = async (id: string): Promise<void> => {
    try {
      setLoading(true);
      
      const response = await areaApi.deleteArea(id);
      
      if (response.data.code === 200) {
        setAllAreas(prev => prev.filter(area => area.id !== id));
        // 刷新区域树
        await loadAreaTree();
      } else {
        throw new Error(response.data.message || '删除区域失败');
      }
    } catch (err) {
      console.error('Delete area error:', err);
      setError(err instanceof Error ? err.message : '删除区域失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const clearError = () => {
    setError(null);
  };

  const setAreas = (newAreas: Area[]) => {
    setAllAreas(newAreas);
  };

  const getAreasByCustomerId = (customerId: string): Area[] => {
    return allAreas.filter(area => area.customerId === customerId);
  };

  const flattenAreas = (areaList: Area[]): Area[] => {
    let result: Area[] = [];
    areaList.forEach(area => {
      result.push(area);
      if (area.children) {
        result = [...result, ...flattenAreas(area.children)];
      }
    });
    return result;
  };

  const buildAreaTree = (areaList: Area[]): Area[] => {
    const areaMap = new Map<string, Area>();
    const rootAreas: Area[] = [];
    
    // 创建所有区域的映射
    areaList.forEach(area => {
      areaMap.set(area.id, { ...area, children: [] });
    });
    
    // 构建树结构
    areaList.forEach(area => {
      const node = areaMap.get(area.id)!;
      if (area.parentId) {
        const parent = areaMap.get(area.parentId);
        if (parent) {
          parent.children = parent.children || [];
          parent.children.push(node);
        } else {
          // 如果父节点不存在，将其作为根节点
          rootAreas.push(node);
        }
      } else {
        rootAreas.push(node);
      }
    });
    
    return rootAreas;
  };

  return (
    <AreaContext.Provider
      value={{
        areas,
        allAreas,
        areaTree,
        loading,
        error,
        currentPage,
        totalPages,
        totalCount,
        accessibleAreaIds,
        refreshAreas,
        refreshAreaTree,
        getArea,
        getChildAreas,
        addArea,
        updateArea,
        deleteArea,
        clearError,
        setAreas,
        getAreasByCustomerId,
        flattenAreas,
        buildAreaTree,
      }}
    >
      {children}
    </AreaContext.Provider>
  );
}

export function useArea() {
  const context = useContext(AreaContext);
  if (context === undefined) {
    if (import.meta.env.DEV) {
      console.warn('useArea called outside of AreaProvider');
      return {
        areas: [],
        allAreas: [],
        areaTree: [],
        loading: false,
        error: null,
        currentPage: 1,
        totalPages: 0,
        totalCount: 0,
        accessibleAreaIds: [],
        refreshAreas: async () => {},
        refreshAreaTree: async () => {},
        getArea: async () => ({ id: '', name: '', type: '', deviceCount: 0, sortOrder: 0, createdAt: '', updatedAt: '' } as Area),
        getChildAreas: async () => [],
        addArea: async () => ({ id: '', name: '', type: '', deviceCount: 0, sortOrder: 0, createdAt: '', updatedAt: '' } as Area),
        updateArea: async () => ({ id: '', name: '', type: '', deviceCount: 0, sortOrder: 0, createdAt: '', updatedAt: '' } as Area),
        deleteArea: async () => {},
        clearError: () => {},
        setAreas: () => {},
        getAreasByCustomerId: () => [],
        flattenAreas: () => [],
        buildAreaTree: () => []
      };
    }
    throw new Error('useArea must be used within an AreaProvider');
  }
  return context;
}