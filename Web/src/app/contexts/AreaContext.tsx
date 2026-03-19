import { createContext, useContext, useState, ReactNode, useMemo } from 'react';
import { useAuth } from './AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';

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
  type: 'level1' | 'level2' | 'level3';
  image: string;
  parentId?: string;
  customerId?: string; // Link Level 1 area to a customer
  appCode?: string; // Application code for data isolation
  children?: Area[];
  devices?: AreaDevice[];
  description?: string;
  deviceCount?: number;
}

interface AreaContextType {
  areas: Area[];
  setAreas: (areas: Area[]) => void;
  getAreasByCustomerId: (customerId: string) => Area[];
  flattenAreas: (areas: Area[]) => Area[];
  accessibleAreaIds: string[]; // List of all area IDs the user is allowed to access
}

const AreaContext = createContext<AreaContextType | undefined>(undefined);

// Mock Data
const MOCK_AREAS: Area[] = [
  {
    id: 'L1-001',
    name: '海底捞火锅（建国路店）',
    type: 'level1',
    appCode: 'HDLAPP001',
    customerId: '1',
    image: 'https://images.unsplash.com/photo-1759065456033-9f2d6a400932?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxmYWN0b3J5JTIwYnVpbGRpbmclMjBleHRlcmlvcnxlbnwxfHx8fDE3Njk5NDk5NjJ8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    description: '北京市朝阳区建国路88号',
    deviceCount: 156,
    children: [
      {
        id: 'L2-001',
        name: '一层大厅区',
        type: 'level2',
        image: 'https://images.unsplash.com/photo-1721244654210-a505a99661e9?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxmbG9vciUyMHBsYW4lMjBibHVlcHJpbnR8ZW58MXx8fHwxNzcwMDE4MzMwfDA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
        description: '包含前厅接待、A区用餐区、B区用餐区',
        parentId: 'L1-001',
        deviceCount: 45,
        children: [
          {
            id: 'L3-001',
            name: 'A区用餐区',
            type: 'level3',
            image: 'https://images.unsplash.com/photo-1760001553414-5634201efc36?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxjb21tZXJjaWFsJTIwa2l0Y2hlbiUyMGxheW91dHxlbnwxfHx8fDE3NzAwMTgzMzN8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
            parentId: 'L2-001',
            deviceCount: 20,
            devices: [
              { id: 'd1', name: '智能电磁炉-A01', type: '加热设备', status: 'online', x: 20, y: 30 },
              { id: 'd2', name: '智能电磁炉-A02', type: '加热设备', status: 'online', x: 20, y: 60 },
              { id: 'd3', name: '送餐机器人-R01', type: '服务设备', status: 'online', x: 50, y: 50 },
            ]
          },
          {
            id: 'L3-002',
            name: '饮品制作台',
            type: 'level3',
            image: 'https://images.unsplash.com/photo-1760001553414-5634201efc36?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxjb21tZXJjaWFsJTIwa2l0Y2hlbiUyMGxheW91dHxlbnwxfHx8fDE3NzAwMTgzMzN8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
            parentId: 'L2-001',
            deviceCount: 15,
            devices: [
              { id: 'd4', name: '制冰机-I01', type: '冷藏设备', status: 'warning', x: 30, y: 40 },
              { id: 'd5', name: '咖啡机-C01', type: '饮品设备', status: 'online', x: 60, y: 40 },
            ]
          }
        ]
      },
      {
        id: 'L2-002',
        name: '二层后厨区',
        type: 'level2',
        image: 'https://images.unsplash.com/photo-1721244654210-a505a99661e9?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxmbG9vciUyMHBsYW4lMjBibHVlcHJpbnR8ZW58MXx8fHwxNzcwMDE4MzMwfDA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
        description: '包含中央厨房、冷库、清洗间',
        parentId: 'L1-001',
        deviceCount: 111,
        children: [
           {
            id: 'L3-003',
            name: '中央厨房',
            type: 'level3',
            image: 'https://images.unsplash.com/photo-1760001553414-5634201efc36?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxjb21tZXJjaWFsJTIwa2l0Y2hlbiUyMGxheW91dHxlbnwxfHx8fDE3NzAwMTgzMzN8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
            parentId: 'L2-002',
            deviceCount: 45,
            devices: [
              { id: 'd6', name: '蒸箱-Z01', type: '烹饪设备', status: 'online', x: 15, y: 25 },
              { id: 'd7', name: '炒灶-C01', type: '烹饪设备', status: 'online', x: 15, y: 45 },
              { id: 'd8', name: '炒灶-C02', type: '烹饪设备', status: 'offline', x: 15, y: 65 },
              { id: 'd9', name: '排烟风机-F01', type: '通风设备', status: 'online', x: 80, y: 20 },
            ]
          },
          {
            id: 'L3-004',
            name: '冷库',
            type: 'level3',
            image: 'https://images.unsplash.com/photo-1760001553414-5634201efc36?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxjb21tZXJjaWFsJTIwa2l0Y2hlbiUyMGxheW91dHxlbnwxfHx8fDE3NzAwMTgzMzN8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
            parentId: 'L2-002',
            deviceCount: 12,
            devices: [
              { id: 'd10', name: '温湿度传感器-T01', type: '传感设备', status: 'online', x: 50, y: 50 },
            ]
          }
        ]
      }
    ]
  },
  {
    id: 'L1-002',
    name: '喜茶（上海世纪大道店）',
    type: 'level1',
    appCode: 'XCAPP002',
    customerId: '2',
    image: 'https://images.unsplash.com/photo-1759065456033-9f2d6a400932?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxmYWN0b3J5JTIwYnVpbGRpbmclMjBleHRlcmlvcnxlbnwxfHx8fDE3Njk5NDk5NjJ8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    description: '上海市浦东新区世纪大道100号',
    deviceCount: 35,
    children: []
  }
];

export function AreaProvider({ children }: { children: ReactNode }) {
  const { currentCustomer, user, getUserRole, hasPermission } = useAuth();
  const [allAreas, setAllAreas] = useState<Area[]>(MOCK_AREAS);

  // Helper to flatten area tree to list of all nodes
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

  // Determine accessible areas based on user RBAC settings
  const { filteredAreas, accessibleAreaIds } = useMemo(() => {
    // 1. Basic tenant isolation first
    const customerAreas = allAreas.filter((a) => 
      currentCustomer ? a.appCode === currentCustomer.appCode : false
    );

    const role = getUserRole();
    
    // 2. If no user/role, or user has 'VIEW_AREAS' but DataScope is 'ALL', return everything
    // Note: checking hasPermission(VIEW_AREAS) is good practice, but for now we assume basic access if logged in.
    // We focus on Data Scope here.
    
    if (!role) {
      return { filteredAreas: [], accessibleAreaIds: [] };
    }

    // RBAC: Check Data Scope
    if (role.dataScope === 'ALL') {
       return { 
        filteredAreas: customerAreas, 
        accessibleAreaIds: flattenAreas(customerAreas).map(a => a.id) 
      };
    }

    // RBAC: Custom Scope - use allowedAreaIds
    if (role.dataScope === 'CUSTOM') {
      if (!user?.allowedAreaIds || user.allowedAreaIds.length === 0) {
        return { filteredAreas: [], accessibleAreaIds: [] };
      }

      const allowedSet = new Set(user.allowedAreaIds);
      const accessibleIds = new Set<string>();

      // Recursive filter function that also populates accessibleIds
      const filterTree = (nodes: Area[]): Area[] => {
        return nodes.map(node => {
          const isDirectlyAllowed = allowedSet.has(node.id);
          
          // If directly allowed, we include it AND all its descendants
          if (isDirectlyAllowed) {
            accessibleIds.add(node.id);
            const addDescendants = (n: Area) => {
               accessibleIds.add(n.id);
               n.children?.forEach(addDescendants);
            };
            node.children?.forEach(addDescendants);
            return node; // Return the full node with children intact
          }

          // If not directly allowed, check if any children are allowed (Backtracking)
          if (node.children) {
            const filteredChildren = filterTree(node.children);
            if (filteredChildren.length > 0) {
              accessibleIds.add(node.id); // Add self because we need to traverse through here
              return { ...node, children: filteredChildren };
            }
          }

          return null;
        }).filter((n): n is Area => n !== null);
      };

      const result = filterTree(customerAreas);
      return { 
        filteredAreas: result, 
        accessibleAreaIds: Array.from(accessibleIds) 
      };
    }

    // Default fallback
    return { filteredAreas: [], accessibleAreaIds: [] };

  }, [allAreas, currentCustomer, user, getUserRole]);

  const setAreas = (newAreas: Area[]) => {
    if (!currentCustomer) return;
    const otherAreas = allAreas.filter(a => a.appCode !== currentCustomer.appCode);
    setAllAreas([...otherAreas, ...newAreas]);
  };

  const getAreasByCustomerId = (customerId: string) => {
    return allAreas.filter(area => area.customerId === customerId);
  };

  return (
    <AreaContext.Provider value={{ 
      areas: filteredAreas, 
      setAreas, 
      getAreasByCustomerId, 
      flattenAreas,
      accessibleAreaIds 
    }}>
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
          setAreas: () => {},
          getAreasByCustomerId: () => [],
          flattenAreas: () => [],
          accessibleAreaIds: []
        }
    }
    throw new Error('useArea must be used within an AreaProvider');
  }
  return context;
}
