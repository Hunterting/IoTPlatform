// 区域数据适配器
import { adaptIdFromBackend, adaptIdToBackend, adaptDateTime } from './index';
import { 
  AreaDto, 
  AreaTreeNodeDto,
  AreaType
} from '../api/types/area.types';

// 后端响应类型接口
interface BackendAreaDto {
  id: number;
  name: string;
  type: string;
  image?: string | null;
  parentId?: number | null;
  parentName?: string | null;
  customerId?: number | null;
  customerName?: string | null;
  appCode?: string | null;
  description?: string | null;
  deviceCount: number;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}

interface BackendAreaTreeNodeDto {
  id: number;
  name: string;
  type: string;
  parentId?: number | null;
  deviceCount: number;
  children?: BackendAreaTreeNodeDto[] | null;
}

/**
 * 将后端区域信息转换为前端格式
 */
export const adaptAreaFromBackend = (backendArea: BackendAreaDto): AreaDto => {
  return {
    id: adaptIdFromBackend(backendArea.id),
    name: backendArea.name,
    type: backendArea.type,
    image: backendArea.image,
    parentId: adaptIdFromBackend(backendArea.parentId),
    parentName: backendArea.parentName,
    customerId: adaptIdFromBackend(backendArea.customerId),
    customerName: backendArea.customerName,
    appCode: backendArea.appCode,
    description: backendArea.description,
    deviceCount: backendArea.deviceCount,
    sortOrder: backendArea.sortOrder,
    createdAt: adaptDateTime(backendArea.createdAt),
    updatedAt: adaptDateTime(backendArea.updatedAt)
  };
};

/**
 * 将后端区域树节点转换为前端格式
 */
export const adaptAreaTreeNodeFromBackend = (backendNode: BackendAreaTreeNodeDto): AreaTreeNodeDto => {
  return {
    id: adaptIdFromBackend(backendNode.id),
    name: backendNode.name,
    type: backendNode.type,
    parentId: adaptIdFromBackend(backendNode.parentId),
    deviceCount: backendNode.deviceCount,
    children: backendNode.children?.map(child => adaptAreaTreeNodeFromBackend(child)) || null
  };
};

/**
 * 将前端区域信息转换为后端格式
 */
export const adaptAreaToBackend = (area: Partial<AreaDto>): Partial<BackendAreaDto> => {
  const backendArea: Partial<BackendAreaDto> = {};
  
  if (area.id !== undefined) {
    backendArea.id = adaptIdToBackend(area.id) as number;
  }
  if (area.name !== undefined) {
    backendArea.name = area.name;
  }
  if (area.type !== undefined) {
    backendArea.type = area.type;
  }
  if (area.image !== undefined) {
    backendArea.image = area.image;
  }
  if (area.parentId !== undefined) {
    backendArea.parentId = adaptIdToBackend(area.parentId);
  }
  if (area.customerId !== undefined) {
    backendArea.customerId = adaptIdToBackend(area.customerId);
  }
  if (area.appCode !== undefined) {
    backendArea.appCode = area.appCode;
  }
  if (area.description !== undefined) {
    backendArea.description = area.description;
  }
  if (area.sortOrder !== undefined) {
    backendArea.sortOrder = area.sortOrder;
  }
  
  return backendArea;
};

/**
 * 将前端创建区域请求转换为后端格式
 */
export const adaptCreateAreaToBackend = (area: any): any => {
  const backendArea: any = { ...area };
  
  // 转换ID字段
  if (backendArea.parentId !== undefined) {
    backendArea.parentId = adaptIdToBackend(backendArea.parentId);
  }
  if (backendArea.customerId !== undefined) {
    backendArea.customerId = adaptIdToBackend(backendArea.customerId);
  }
  
  return backendArea;
};

/**
 * 将前端更新区域请求转换为后端格式
 */
export const adaptUpdateAreaToBackend = (area: any): any => {
  const backendArea: any = { ...area };
  
  // 转换ID字段
  if (backendArea.parentId !== undefined) {
    backendArea.parentId = adaptIdToBackend(backendArea.parentId);
  }
  if (backendArea.customerId !== undefined) {
    backendArea.customerId = adaptIdToBackend(backendArea.customerId);
  }
  
  return backendArea;
};

/**
 * 验证区域类型
 */
export const validateAreaType = (type: string): AreaType => {
  const validTypes = Object.values(AreaType);
  return validTypes.includes(type as AreaType) ? type as AreaType : AreaType.AREA;
};

/**
 * 获取区域类型文本
 */
export const getAreaTypeText = (type: AreaType): string => {
  const typeTexts: Record<AreaType, string> = {
    [AreaType.BUILDING]: '建筑',
    [AreaType.FLOOR]: '楼层',
    [AreaType.ROOM]: '房间',
    [AreaType.AREA]: '区域',
    [AreaType.ZONE]: '分区',
    [AreaType.REGION]: '区域组'
  };
  
  return typeTexts[type] || '区域';
};

/**
 * 获取区域类型图标
 */
export const getAreaTypeIcon = (type: AreaType): string => {
  const typeIcons: Record<AreaType, string> = {
    [AreaType.BUILDING]: 'building',
    [AreaType.FLOOR]: 'layers',
    [AreaType.ROOM]: 'door-open',
    [AreaType.AREA]: 'map',
    [AreaType.ZONE]: 'grid',
    [AreaType.REGION]: 'globe'
  };
  
  return typeIcons[type] || 'map';
};

/**
 * 构建区域树
 */
export const buildAreaTree = (areas: AreaDto[]): AreaTreeNodeDto[] => {
  // 创建ID到节点的映射
  const nodeMap = new Map<string, AreaTreeNodeDto>();
  const rootNodes: AreaTreeNodeDto[] = [];
  
  // 首先创建所有节点
  areas.forEach(area => {
    const node: AreaTreeNodeDto = {
      id: area.id,
      name: area.name,
      type: area.type,
      parentId: area.parentId,
      deviceCount: area.deviceCount,
      children: null
    };
    
    nodeMap.set(area.id, node);
  });
  
  // 然后构建树结构
  areas.forEach(area => {
    const node = nodeMap.get(area.id);
    if (!node) return;
    
    if (area.parentId) {
      const parentNode = nodeMap.get(area.parentId);
      if (parentNode) {
        if (!parentNode.children) {
          parentNode.children = [];
        }
        parentNode.children.push(node);
      } else {
        // 父节点不存在，当作根节点
        rootNodes.push(node);
      }
    } else {
      // 没有父节点，是根节点
      rootNodes.push(node);
    }
  });
  
  // 对子节点排序
  const sortNodes = (nodes: AreaTreeNodeDto[]) => {
    nodes.sort((a, b) => {
      // 可以在这里添加排序逻辑，比如按名称排序
      return a.name.localeCompare(b.name);
    });
    
    nodes.forEach(node => {
      if (node.children) {
        sortNodes(node.children);
      }
    });
  };
  
  sortNodes(rootNodes);
  
  return rootNodes;
};

/**
 * 扁平化区域树
 */
export const flattenAreaTree = (tree: AreaTreeNodeDto[]): AreaDto[] => {
  const result: AreaDto[] = [];
  
  const traverse = (node: AreaTreeNodeDto, depth = 0) => {
    // 将树节点转换为区域DTO
    const area: AreaDto = {
      id: node.id,
      name: node.name,
      type: node.type,
      image: null,
      parentId: node.parentId,
      parentName: null, // 需要从原始数据中获取
      customerId: null, // 需要从原始数据中获取
      customerName: null, // 需要从原始数据中获取
      appCode: null, // 需要从原始数据中获取
      description: null, // 需要从原始数据中获取
      deviceCount: node.deviceCount,
      sortOrder: depth,
      createdAt: '', // 需要从原始数据中获取
      updatedAt: '' // 需要从原始数据中获取
    };
    
    result.push(area);
    
    if (node.children) {
      node.children.forEach(child => traverse(child, depth + 1));
    }
  };
  
  tree.forEach(node => traverse(node));
  
  return result;
};

/**
 * 获取区域路径
 */
export const getAreaPath = (
  areas: AreaDto[], 
  areaId: string,
  includeSelf = true
): AreaDto[] => {
  const path: AreaDto[] = [];
  
  const findArea = (id: string): AreaDto | undefined => {
    return areas.find(area => area.id === id);
  };
  
  let currentArea = findArea(areaId);
  
  if (!currentArea) {
    return path;
  }
  
  if (includeSelf) {
    path.unshift(currentArea);
  }
  
  // 向上查找父区域
  while (currentArea?.parentId) {
    const parentArea = findArea(currentArea.parentId);
    if (parentArea) {
      path.unshift(parentArea);
      currentArea = parentArea;
    } else {
      break;
    }
  }
  
  return path;
};

/**
 * 获取区域显示名称（包含路径）
 */
export const getAreaDisplayName = (areas: AreaDto[], areaId: string): string => {
  const path = getAreaPath(areas, areaId);
  return path.map(area => area.name).join(' / ');
};

// ------------------------------------------------------------------------------
// AreaContext 专用适配器函数
// ------------------------------------------------------------------------------

/**
 * 将区域DTO转换为AreaContext的Area格式
 */
export const adaptAreaDtoToArea = (areaDto: AreaDto): any => {
  return {
    id: areaDto.id,
    name: areaDto.name,
    type: areaDto.type,
    image: areaDto.image,
    parentId: areaDto.parentId,
    parentName: areaDto.parentName,
    customerId: areaDto.customerId,
    customerName: areaDto.customerName,
    appCode: areaDto.appCode,
    description: areaDto.description,
    deviceCount: areaDto.deviceCount,
    sortOrder: areaDto.sortOrder,
    createdAt: areaDto.createdAt,
    updatedAt: areaDto.updatedAt,
    children: [], // 初始化为空数组，后续需要单独加载
    devices: [] // 初始化为空数组，后续需要单独加载
  };
};

/**
 * 将区域树节点DTO转换为AreaContext的Area格式
 */
export const adaptAreaTreeNodeDtoToArea = (treeNode: AreaTreeNodeDto): any => {
  return {
    id: treeNode.id,
    name: treeNode.name,
    type: treeNode.type,
    parentId: treeNode.parentId,
    deviceCount: treeNode.deviceCount,
    children: treeNode.children?.map(child => adaptAreaTreeNodeDtoToArea(child)) || [],
    image: null, // 树节点可能没有image字段
    customerId: null,
    customerName: null,
    appCode: null,
    description: null,
    sortOrder: 0,
    createdAt: '',
    updatedAt: '',
    devices: []
  };
};

/**
 * 将Area格式转换为创建区域请求
 */
export const adaptCreateAreaRequest = (
  area: Omit<any, 'id' | 'deviceCount' | 'createdAt' | 'updatedAt' | 'children' | 'devices'>
): any => {
  return {
    name: area.name,
    type: area.type || 'level1',
    image: area.image,
    parentId: area.parentId ? Number(area.parentId) : null,
    customerId: area.customerId ? Number(area.customerId) : null,
    appCode: area.appCode,
    description: area.description,
    sortOrder: area.sortOrder || 0
  };
};

/**
 * 将Area更新数据转换为更新区域的请求
 */
export const adaptUpdateAreaRequest = (
  updates: Partial<any>
): any => {
  const request: any = {};
  
  if (updates.name !== undefined) request.name = updates.name;
  if (updates.type !== undefined) request.type = updates.type;
  if (updates.image !== undefined) request.image = updates.image;
  if (updates.parentId !== undefined) request.parentId = updates.parentId ? Number(updates.parentId) : null;
  if (updates.customerId !== undefined) request.customerId = updates.customerId ? Number(updates.customerId) : null;
  if (updates.appCode !== undefined) request.appCode = updates.appCode;
  if (updates.description !== undefined) request.description = updates.description;
  if (updates.sortOrder !== undefined) request.sortOrder = updates.sortOrder;
  
  return request;
};