import { useState, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { 
  ChevronRight, 
  ChevronDown, 
  Layout, 
  Building2, 
  Folder, 
  File, 
  Plus,
  Check,
  Edit,
  Trash2,
  AlertTriangle
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { useArea, Area } from '@/app/contexts/AreaContext';
import { PERMISSIONS } from '@/app/config/permissions';

// Tree Item Component
const TreeItem = ({ 
  label, 
  icon, 
  children, 
  level = 0, 
  isActive, 
  onSelect,
  isExpanded,
  onToggle
}: { 
  label: string;
  icon: React.ReactNode;
  children?: React.ReactNode;
  level?: number;
  isActive?: boolean;
  onSelect?: () => void;
  isExpanded?: boolean;
  onToggle?: () => void;
}) => {
  return (
    <div>
      <div 
        onClick={() => {
          onToggle && onToggle();
          onSelect && onSelect();
        }}
        className={`flex items-center gap-2 py-1.5 px-2 rounded-lg cursor-pointer transition-colors select-none ${
          isActive ? 'bg-blue-500/20 text-white' : 'text-gray-400 hover:bg-white/5 hover:text-gray-200'
        }`}
        style={{ paddingLeft: `${level * 16 + 8}px` }}
      >
        <div className="w-4 h-4 flex items-center justify-center">
           {children ? (
             isExpanded ? <ChevronDown className="w-3 h-3" /> : <ChevronRight className="w-3 h-3" />
           ) : null}
        </div>
        {icon}
        <span className="text-sm truncate">{label}</span>
      </div>
      <AnimatePresence>
        {isExpanded && children && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            className="overflow-hidden"
          >
            {children}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
};

export function AreaManagementPage() {
  const { customers, hasPermission } = useAuth();
  const { areas, setAreas } = useArea();

  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set(['1', 'L1-001'])); // Default expand first customer
  
  // State for Modals
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  
  const [addMode, setAddMode] = useState<'root' | 'child'>('root');
  const [newItemName, setNewItemName] = useState('');
  const [selectedCustomerForRoot, setSelectedCustomerForRoot] = useState<string>('');

  // Helper to find node data recursively
  const findNode = (id: string | null): { data: any, type: string } | null => {
    if (!id) return null;
    
    // Check customers
    const customer = customers.find(c => c.id === id);
    if (customer) return { data: customer, type: 'customer' };

    // Check areas recursively
    const findInAreas = (areaList: Area[]): Area | null => {
      for (const area of areaList) {
        if (area.id === id) return area;
        if (area.children) {
          const found = findInAreas(area.children);
          if (found) return found;
        }
      }
      return null;
    };

    const area = findInAreas(areas);
    if (area) return { data: area, type: area.type };

    return null;
  };

  const selectedNode = useMemo(() => findNode(selectedNodeId), [selectedNodeId, customers, areas]);

  const toggleNode = (id: string) => {
    const newExpanded = new Set(expandedNodes);
    if (newExpanded.has(id)) {
      newExpanded.delete(id);
    } else {
      newExpanded.add(id);
    }
    setExpandedNodes(newExpanded);
  };

  // --- CRUD Operations ---

  const handleAddNode = () => {
      if (addMode === 'root') {
          if (!selectedCustomerForRoot) return;
          if (!newItemName.trim()) return;

          const customer = customers.find(c => c.id === selectedCustomerForRoot);
          if (!customer) return;

          const newId = `L-${Date.now()}`;
          const newArea: Area = {
              id: newId,
              name: newItemName,
              type: 'level1',
              image: 'https://images.unsplash.com/photo-1587578932405-7c740a74a27e',
              description: '新增区域',
              deviceCount: 0,
              children: [],
              devices: [],
              appCode: customer.appCode,
              customerId: customer.id,
          };

          setAreas([...areas, newArea]);
          setExpandedNodes(new Set([...expandedNodes, customer.id]));
          setSelectedNodeId(newId);

      } else {
          // Add Child Node
          if (!newItemName.trim()) return;
          if (!selectedNode) return;

          const newId = `L-${Date.now()}`;
          const newArea: Area = {
              id: newId,
              name: newItemName,
              type: selectedNode.type === 'customer' ? 'level1' : selectedNode.type === 'level1' ? 'level2' : 'level3',
              image: 'https://images.unsplash.com/photo-1587578932405-7c740a74a27e',
              description: '新增区域',
              deviceCount: 0,
              children: [],
              devices: [],
              appCode: selectedNode.data.appCode,
              customerId: selectedNode.type === 'customer' ? selectedNode.data.id : undefined,
              parentId: selectedNode.type !== 'customer' ? selectedNode.data.id : undefined
          };

          if (selectedNode.type === 'customer') {
              setAreas([...areas, newArea]);
              setExpandedNodes(new Set([...expandedNodes, selectedNode.data.id]));
          } else {
              const updateAreaTree = (list: Area[]): Area[] => {
                  return list.map(node => {
                      if (node.id === selectedNode.data.id) {
                          return {
                              ...node,
                              children: [...(node.children || []), newArea]
                          };
                      }
                      if (node.children) {
                          return {
                              ...node,
                              children: updateAreaTree(node.children)
                          };
                      }
                      return node;
                  });
              };
              setAreas(updateAreaTree(areas));
              setExpandedNodes(new Set([...expandedNodes, selectedNode.data.id]));
          }
      }
      
      setIsAddModalOpen(false);
      setNewItemName('');
      setSelectedCustomerForRoot('');
  };

  const handleEditNode = () => {
    if (!selectedNode || !newItemName.trim() || selectedNode.type === 'customer') return;

    const updateAreaTree = (list: Area[]): Area[] => {
      return list.map(node => {
        if (node.id === selectedNode.data.id) {
          return { ...node, name: newItemName };
        }
        if (node.children) {
          return { ...node, children: updateAreaTree(node.children) };
        }
        return node;
      });
    };

    setAreas(updateAreaTree(areas));
    setIsEditModalOpen(false);
    setNewItemName('');
  };

  const handleDeleteNode = () => {
    if (!selectedNode || selectedNode.type === 'customer') return;

    // Filter out from root level if applicable
    if (areas.some(a => a.id === selectedNode.data.id)) {
      setAreas(areas.filter(a => a.id !== selectedNode.data.id));
    } else {
      // Recursively remove from children
      const removeFromTree = (list: Area[]): Area[] => {
        return list
          .filter(node => node.id !== selectedNode.data.id)
          .map(node => {
            if (node.children) {
              return { ...node, children: removeFromTree(node.children) };
            }
            return node;
          });
      };
      setAreas(removeFromTree(areas));
    }

    setIsDeleteModalOpen(false);
    setSelectedNodeId(null); // Deselect after delete
  };

  // --- Render Helpers ---

  const renderAreaTree = (areas: Area[], level: number) => {
    return areas.map(area => (
      <TreeItem
        key={area.id}
        label={area.name}
        icon={area.type === 'level3' ? <File className="w-4 h-4 text-blue-400" /> : <Folder className="w-4 h-4 text-yellow-400" />}
        level={level}
        isActive={selectedNodeId === area.id}
        onSelect={() => setSelectedNodeId(area.id)}
        isExpanded={expandedNodes.has(area.id)}
        onToggle={() => toggleNode(area.id)}
      >
        {area.children && area.children.length > 0 && renderAreaTree(area.children, level + 1)}
      </TreeItem>
    ));
  };

  return (
    <div className="flex h-[calc(100vh-64px)] overflow-hidden bg-gray-900 text-white relative">
      
      {/* Add Modal */}
      <AnimatePresence>
        {isAddModalOpen && (
            <motion.div 
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="absolute inset-0 z-50 bg-black/50 backdrop-blur-sm flex items-center justify-center"
                onClick={() => setIsAddModalOpen(false)}
            >
                <motion.div 
                    initial={{ scale: 0.9, opacity: 0 }}
                    animate={{ scale: 1, opacity: 1 }}
                    exit={{ scale: 0.9, opacity: 0 }}
                    onClick={e => e.stopPropagation()}
                    className="bg-gray-800 border border-white/10 rounded-xl p-6 w-96 shadow-2xl"
                >
                    <h3 className="text-lg font-bold text-white mb-4">
                        {addMode === 'root' ? '新增根节点区域' : '新增子区域'}
                    </h3>
                    <div className="space-y-4">
                        {addMode === 'root' && (
                            <div>
                                <label className="block text-sm text-gray-400 mb-2">选择客户</label>
                                <div className="border border-white/20 rounded-lg max-h-40 overflow-y-auto bg-gray-900">
                                    {customers.map(customer => (
                                        <div 
                                            key={customer.id}
                                            onClick={() => setSelectedCustomerForRoot(customer.id)}
                                            className={`flex items-center justify-between px-3 py-2 cursor-pointer transition-colors ${
                                                selectedCustomerForRoot === customer.id 
                                                ? 'bg-blue-500/20 text-white' 
                                                : 'text-gray-300 hover:bg-white/5'
                                            }`}
                                        >
                                            <div className="flex items-center gap-2">
                                                <Building2 className="w-4 h-4 text-purple-400" />
                                                <span className="text-sm">{customer.name}</span>
                                            </div>
                                            {selectedCustomerForRoot === customer.id && (
                                                <Check className="w-4 h-4 text-blue-400" />
                                            )}
                                        </div>
                                    ))}
                                </div>
                            </div>
                        )}
                        
                        <div>
                            <label className="block text-sm text-gray-400 mb-1">区域名称</label>
                            <input 
                                type="text" 
                                autoFocus
                                value={newItemName}
                                onChange={e => setNewItemName(e.target.value)}
                                placeholder="请输入名称..."
                                className="w-full bg-gray-900 border border-white/20 rounded-lg px-4 py-2 text-white focus:outline-none focus:border-blue-500"
                                onKeyDown={e => e.key === 'Enter' && handleAddNode()}
                            />
                        </div>
                        <div className="flex justify-end gap-2">
                            <button 
                                onClick={() => setIsAddModalOpen(false)}
                                className="px-4 py-2 text-gray-400 hover:text-white transition-colors"
                            >
                                取消
                            </button>
                            <button 
                                onClick={handleAddNode}
                                disabled={!newItemName.trim() || (addMode === 'root' && !selectedCustomerForRoot)}
                                className="px-4 py-2 bg-blue-500 text-white rounded-lg hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                确认添加
                            </button>
                        </div>
                    </div>
                </motion.div>
            </motion.div>
        )}
      </AnimatePresence>

      {/* Edit Modal */}
      <AnimatePresence>
        {isEditModalOpen && (
            <motion.div 
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="absolute inset-0 z-50 bg-black/50 backdrop-blur-sm flex items-center justify-center"
                onClick={() => setIsEditModalOpen(false)}
            >
                <motion.div 
                    initial={{ scale: 0.9, opacity: 0 }}
                    animate={{ scale: 1, opacity: 1 }}
                    exit={{ scale: 0.9, opacity: 0 }}
                    onClick={e => e.stopPropagation()}
                    className="bg-gray-800 border border-white/10 rounded-xl p-6 w-96 shadow-2xl"
                >
                    <h3 className="text-lg font-bold text-white mb-4">编辑区域</h3>
                    <div className="space-y-4">
                        <div>
                            <label className="block text-sm text-gray-400 mb-1">区域名称</label>
                            <input 
                                type="text" 
                                autoFocus
                                value={newItemName}
                                onChange={e => setNewItemName(e.target.value)}
                                placeholder="请输入名称..."
                                className="w-full bg-gray-900 border border-white/20 rounded-lg px-4 py-2 text-white focus:outline-none focus:border-blue-500"
                                onKeyDown={e => e.key === 'Enter' && handleEditNode()}
                            />
                        </div>
                        <div className="flex justify-end gap-2">
                            <button 
                                onClick={() => setIsEditModalOpen(false)}
                                className="px-4 py-2 text-gray-400 hover:text-white transition-colors"
                            >
                                取消
                            </button>
                            <button 
                                onClick={handleEditNode}
                                disabled={!newItemName.trim()}
                                className="px-4 py-2 bg-blue-500 text-white rounded-lg hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                保存修改
                            </button>
                        </div>
                    </div>
                </motion.div>
            </motion.div>
        )}
      </AnimatePresence>

      {/* Delete Confirmation Modal */}
      <AnimatePresence>
        {isDeleteModalOpen && (
            <motion.div 
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="absolute inset-0 z-50 bg-black/50 backdrop-blur-sm flex items-center justify-center"
                onClick={() => setIsDeleteModalOpen(false)}
            >
                <motion.div 
                    initial={{ scale: 0.9, opacity: 0 }}
                    animate={{ scale: 1, opacity: 1 }}
                    exit={{ scale: 0.9, opacity: 0 }}
                    onClick={e => e.stopPropagation()}
                    className="bg-gray-800 border border-red-500/30 rounded-xl p-6 w-96 shadow-2xl"
                >
                    <div className="flex items-center gap-3 mb-4">
                        <div className="p-2 bg-red-500/20 rounded-lg">
                            <AlertTriangle className="w-6 h-6 text-red-400" />
                        </div>
                        <h3 className="text-lg font-bold text-white">确认删除</h3>
                    </div>
                    
                    <p className="text-gray-300 mb-6 text-sm">
                        确定要删除 <span className="text-white font-semibold">{selectedNode?.data.name}</span> 吗？
                        <br/>
                        <span className="text-red-400 text-xs mt-1 block">该操作将同时删除其所有子区域，且无法恢复。</span>
                    </p>

                    <div className="flex justify-end gap-2">
                        <button 
                            onClick={() => setIsDeleteModalOpen(false)}
                            className="px-4 py-2 text-gray-400 hover:text-white transition-colors"
                        >
                            取消
                        </button>
                        <button 
                            onClick={handleDeleteNode}
                            className="px-4 py-2 bg-red-500 text-white rounded-lg hover:bg-red-600 transition-colors"
                        >
                            确认删除
                        </button>
                    </div>
                </motion.div>
            </motion.div>
        )}
      </AnimatePresence>

      {/* Sidebar Tree */}
      <div className="w-80 border-r border-white/10 flex flex-col bg-gray-900/50 backdrop-blur-xl">
        <div className="p-4 border-b border-white/10 flex items-center justify-between">
          <h2 className="font-semibold text-white">区域导航</h2>
          {hasPermission(PERMISSIONS.CREATE_AREAS) && (
            <button 
                onClick={() => {
                    setAddMode('root');
                    setNewItemName('');
                    setSelectedCustomerForRoot('');
                    setIsAddModalOpen(true);
                }}
                className="p-1.5 hover:bg-white/10 rounded-lg transition-colors text-blue-400" 
                title="添加根节点区域"
            >
                <Plus className="w-4 h-4" />
            </button>
          )}
        </div>
        <div className="flex-1 overflow-y-auto p-2">
          {customers.map(customer => {
             const customerAreas = areas.filter(a => a.customerId === customer.id);
             return (
               <TreeItem
                 key={customer.id}
                 label={customer.name}
                 icon={<Building2 className="w-4 h-4 text-purple-400" />}
                 level={0}
                 isActive={selectedNodeId === customer.id}
                 onSelect={() => setSelectedNodeId(customer.id)}
                 isExpanded={expandedNodes.has(customer.id)}
                 onToggle={() => toggleNode(customer.id)}
               >
                 {customerAreas.length > 0 && renderAreaTree(customerAreas, 1)}
               </TreeItem>
             );
          })}
        </div>
      </div>

      {/* Main Content Area */}
      <div className="flex-1 overflow-hidden bg-gray-950 flex flex-col">
        {!selectedNode ? (
          <div className="flex-1 flex flex-col items-center justify-center text-gray-500">
            <Layout className="w-16 h-16 mb-4 opacity-20" />
            <p>请选择左侧树节点查看详情</p>
          </div>
        ) : (
          <>
             {/* Header */}
             <div className="h-16 border-b border-white/10 flex items-center justify-between px-6 bg-gray-900/50">
               <div className="flex items-center gap-3">
                 <div className="p-2 bg-white/5 rounded-lg border border-white/10">
                    {selectedNode.type === 'customer' && <Building2 className="w-5 h-5 text-purple-400" />}
                    {selectedNode.type === 'level1' && <Layout className="w-5 h-5 text-yellow-400" />}
                    {selectedNode.type === 'level2' && <Folder className="w-5 h-5 text-yellow-400" />}
                    {selectedNode.type === 'level3' && <File className="w-5 h-5 text-blue-400" />}
                 </div>
                 <div>
                   <h1 className="text-lg font-bold text-white">{selectedNode.data.name}</h1>
                   <p className="text-xs text-gray-400">
                     {selectedNode.type === 'customer' ? '客户根节点' : 
                      selectedNode.type === 'level1' ? '一级区域 (场景)' : 
                      selectedNode.type === 'level2' ? '二级区域 (楼层)' : '三级区域 (功能间)'}
                   </p>
                 </div>
               </div>
               <div className="flex gap-2">
                 {/* Actions */}
                 {selectedNode.type !== 'level3' && hasPermission(PERMISSIONS.CREATE_AREAS) && (
                    <button 
                        onClick={() => {
                            setAddMode('child');
                            setNewItemName('');
                            setIsAddModalOpen(true);
                        }}
                        className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 text-blue-400 rounded-lg text-sm transition-colors border border-blue-500/30"
                    >
                        <Plus className="w-4 h-4" />
                        <span>新增子区域</span>
                    </button>
                 )}
                 
                 {selectedNode.type !== 'customer' && (
                     <>
                        {hasPermission(PERMISSIONS.UPDATE_AREAS) && (
                            <button 
                                onClick={() => {
                                    setNewItemName(selectedNode.data.name);
                                    setIsEditModalOpen(true);
                                }}
                                className="p-2 hover:bg-white/10 rounded-lg text-gray-400 hover:text-white transition-colors"
                                title="编辑"
                            >
                                <Edit className="w-4 h-4" />
                            </button>
                        )}
                        {hasPermission(PERMISSIONS.DELETE_AREAS) && (
                            <button 
                                onClick={() => setIsDeleteModalOpen(true)}
                                className="p-2 hover:bg-red-500/20 rounded-lg text-gray-400 hover:text-red-400 transition-colors"
                                title="删除"
                            >
                                <Trash2 className="w-4 h-4" />
                            </button>
                        )}
                     </>
                 )}
               </div>
             </div>

             {/* Content Body */}
             <div className="flex-1 overflow-hidden relative">
               {selectedNode.type === 'level3' ? (
                 <div className="flex-1 flex flex-col items-center justify-center text-gray-500">
                    <File className="w-16 h-16 mb-4 opacity-20" />
                    <p>末级区域</p>
                    <p className="text-sm mt-2">此处可以添加区域详细信息、图片或绑定的设备列表</p>
                 </div>
               ) : (
                 // List View (Table-like)
                 <div className="p-6">
                   <div className="bg-white/5 border border-white/10 rounded-lg overflow-hidden">
                     <table className="w-full text-sm text-left">
                       <thead className="bg-white/5 text-gray-400 border-b border-white/10">
                         <tr>
                           <th className="px-6 py-3 font-medium">区域名称</th>
                         </tr>
                       </thead>
                       <tbody className="divide-y divide-white/5">
                         {/* Show children if any, else show empty state */}
                         {selectedNode.type === 'customer' ? (
                            areas.filter(a => a.customerId === selectedNode.data.id).length > 0 ? (
                                areas.filter(a => a.customerId === selectedNode.data.id).map(area => (
                                    <tr 
                                        key={area.id} 
                                        className="hover:bg-white/5 transition-colors cursor-pointer group"
                                        onClick={() => {
                                            setSelectedNodeId(area.id);
                                            setExpandedNodes(prev => new Set(prev).add(selectedNode.data.id));
                                        }}
                                    >
                                        <td className="px-6 py-3 text-white flex items-center justify-between">
                                            <div className="flex items-center gap-2">
                                                <Folder className="w-4 h-4 text-yellow-400" />
                                                <span>{area.name}</span>
                                            </div>
                                            <ChevronRight className="w-4 h-4 text-gray-500 group-hover:text-white transition-colors" />
                                        </td>
                                    </tr>
                                ))
                            ) : (
                                <tr><td className="px-6 py-8 text-center text-gray-500">暂无区域数据</td></tr>
                            )
                         ) : (
                            selectedNode.data.children && selectedNode.data.children.length > 0 ? (
                                selectedNode.data.children.map((child: Area) => (
                                    <tr 
                                        key={child.id} 
                                        className="hover:bg-white/5 transition-colors cursor-pointer group"
                                        onClick={() => {
                                            setSelectedNodeId(child.id);
                                            setExpandedNodes(prev => new Set(prev).add(selectedNode.data.id));
                                        }}
                                    >
                                        <td className="px-6 py-3 text-white flex items-center justify-between">
                                            <div className="flex items-center gap-2">
                                                {child.type === 'level3' ? <File className="w-4 h-4 text-blue-400" /> : <Folder className="w-4 h-4 text-yellow-400" />}
                                                <span>{child.name}</span>
                                            </div>
                                            <ChevronRight className="w-4 h-4 text-gray-500 group-hover:text-white transition-colors" />
                                        </td>
                                    </tr>
                                ))
                            ) : (
                                <tr><td className="px-6 py-8 text-center text-gray-500">暂无子区域</td></tr>
                            )
                         )}
                       </tbody>
                     </table>
                   </div>
                 </div>
               )}
             </div>
          </>
        )}
      </div>
      
      {/* End */}
      
    </div>
  );
}
