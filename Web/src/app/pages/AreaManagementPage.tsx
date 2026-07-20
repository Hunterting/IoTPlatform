import { useState, useMemo, useCallback } from 'react';
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
  AlertTriangle,
  Loader2
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
      <AnimatePresence initial={false}>
        {isExpanded && children && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            className="overflow-hidden"
            transition={{ duration: 0.2 }}
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
  const { areas, areaTree, loading, error, addArea, updateArea, deleteArea, refreshAreas, refreshAreaTree } = useArea();

  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set());
  
  // State for Modals
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  
  const [addMode, setAddMode] = useState<'root' | 'child'>('root');
  const [newItemName, setNewItemName] = useState('');
  const [selectedCustomerForRoot, setSelectedCustomerForRoot] = useState<string>('');
  
  // 操作状态
  const [submitting, setSubmitting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  // Helper to find node data recursively
  const findNode = (id: string | null): { data: any, type: string } | null => {
    if (!id) return null;
    
    // Check customers
    const customer = customers.find(c => c.id === id);
    if (customer) return { data: customer, type: 'customer' };

    // Check areas recursively (use areaTree which has proper hierarchy)
    const findInTree = (areaList: Area[]): Area | null => {
      for (const area of areaList) {
        if (area.id === id) return area;
        if (area.children?.length) {
          const found = findInTree(area.children);
          if (found) return found;
        }
      }
      return null;
    };

    // Also check flat areas list for root-level areas
    const area = findInTree(areaTree.length > 0 ? areaTree : areas);
    if (area) return { data: area, type: area.type };

    // Fallback to flat search
    const flatArea = areas.find(a => a.id === id);
    if (flatArea) return { data: flatArea, type: flatArea.type };

    return null;
  };

  const selectedNode = useMemo(() => findNode(selectedNodeId), [selectedNodeId, customers, areas, areaTree]);

  const toggleNode = (id: string) => {
    setExpandedNodes(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  // 无限级层级：动态解析 level{N} 并生成 level{N+1}
  const getNextType = (nodeType: string): string => {
    if (nodeType === 'customer') return 'level1';
    const match = nodeType.match(/^level(\d+)$/);
    if (match) return `level${parseInt(match[1], 10) + 1}`;
    return 'level1';
  };

  // 获取层级显示名称
  const getLevelLabel = (nodeType: string): string => {
    if (nodeType === 'customer') return '客户根节点';
    const match = nodeType.match(/^level(\d+)$/);
    if (match) return `${parseInt(match[1], 10)}级区域`;
    return '区域';
  };

  // --- CRUD Operations (调用真实 API) ---

  const handleAddNode = useCallback(async () => {
    if (!newItemName.trim()) return;

    try {
      setSubmitting(true);
      setActionError(null);

      let parentIdValue: string | undefined;
      let customerIdValue: string | undefined;
      let appCodeValue: string | undefined;
      let typeValue: string;

      if (addMode === 'root') {
        // 根节点区域：选择客户后添加
        if (!selectedCustomerForRoot) return;
        const customer = customers.find(c => c.id === selectedCustomerForRoot);
        if (!customer) return;
        
        customerIdValue = customer.id;
        appCodeValue = customer.appCode;
        typeValue = 'level1';
      } else {
        // 子区域：在选中节点下添加
        if (!selectedNode) return;
        
        if (selectedNode.type === 'customer') {
          // 在客户下添加一级区域
          customerIdValue = selectedNode.data.id;
          appCodeValue = selectedNode.data.appCode;
          typeValue = 'level1';
        } else {
          // 在区域下添加子区域
          parentIdValue = selectedNode.data.id;
          appCodeValue = selectedNode.data.appCode;
          customerIdValue = selectedNode.data.customerId;
          typeValue = getNextType(selectedNode.type);
        }
      }

      const newArea = await addArea({
        name: newItemName.trim(),
        type: typeValue,
        image: undefined,
        description: '',
        parentId: parentIdValue || null,
        customerId: customerIdValue || null,
        appCode: appCodeValue || null,
        sortOrder: 0,
      });

      // 展开新创建区域的父节点，并选中新区域
      if (addMode === 'root' && selectedCustomerForRoot) {
        setExpandedNodes(prev => new Set(prev).add(selectedCustomerForRoot));
      } else if (selectedNode) {
        setExpandedNodes(prev => new Set(prev).add(selectedNode.data.id));
      }
      setSelectedNodeId(newArea.id);

      setIsAddModalOpen(false);
      setNewItemName('');
      setSelectedCustomerForRoot('');
    } catch (err: any) {
      setActionError(err.message || '创建失败');
    } finally {
      setSubmitting(false);
    }
  }, [newItemName, addMode, selectedCustomerForRoot, customers, selectedNode, addArea]);

  const handleEditNode = useCallback(async () => {
    if (!selectedNode || !newItemName.trim() || selectedNode.type === 'customer') return;

    try {
      setSubmitting(true);
      setActionError(null);

      await updateArea(selectedNode.data.id, {
        name: newItemName.trim(),
      });

      setIsEditModalOpen(false);
      setNewItemName('');
    } catch (err: any) {
      setActionError(err.message || '更新失败');
    } finally {
      setSubmitting(false);
    }
  }, [selectedNode, newItemName, updateArea]);

  const handleDeleteNode = useCallback(async () => {
    if (!selectedNode || selectedNode.type === 'customer') return;

    try {
      setSubmitting(true);
      setActionError(null);

      await deleteArea(selectedNode.data.id);

      setIsDeleteModalOpen(false);
      setSelectedNodeId(null);
    } catch (err: any) {
      // 如果后端返回"有子区域或设备"的错误，显示给用户
      setActionError(err.message || '删除失败');
    } finally {
      setSubmitting(false);
    }
  }, [selectedNode, deleteArea]);

  // 递归渲染区域树（保持父子层级关系）
  const renderAreaTree = (areaList: Area[], level: number): React.ReactNode => {
    return areaList.map(area => (
      <TreeItem
        key={area.id}
        label={`${area.name}${area.deviceCount > 0 ? ` (${area.deviceCount})` : ''}`}
        icon={
          area.children && area.children.length > 0
            ? <Folder className="w-4 h-4 text-yellow-400" />
            : <Folder className="w-4 h-4 text-blue-400" />
        }
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

  /**
   * 构建按客户分组的完整递归树。
   * 
   * 核心思路：areaTree 是后端返回的树形数据，但根节点可能没有 customerId 信息。
   * 所以我们用 flat areas 列表（有 customerId）来辅助判断每个根节点归属哪个客户，
   * 然后将完整的 areaTree 子树挂到对应客户下面。
   */
  const customerAreaTrees = useMemo(() => {
    // 如果没有树数据，返回空
    if (areaTree.length === 0 && areas.length === 0) {
      return [] as Array<{ customer: typeof customers[number]; tree: Area[] }>;
    }

    // 用 flat areas 建立 id -> customerId 的映射（flat areas 有 customerId 字段）
    const idToCustomerId = new Map<string, string | null>();
    areas.forEach(a => {
      if (a.customerId) idToCustomerId.set(a.id, a.customerId);
    });

    // 将 areaTree 根节点按 customerId 分组（保留完整 children 子树）
    const grouped = new Map<string | null, Area[]>();
    
    areaTree.forEach(rootArea => {
      // 查找该根节点及其所有后代中是否有 customerId 匹配的
      let matchedCustomerId: string | null = null;
      
      // 先检查自身
      matchedCustomerId = idToCustomerId.get(rootArea.id) ?? null;
      
      // 如果自身没有，递归查找后代
      if (!matchedCustomerId) {
        const findFirstCustomer = (list: Area[]): string | null => {
          for (const node of list) {
            const cid = idToCustomerId.get(node.id);
            if (cid) return cid;
            if (node.children?.length) {
              const found = findFirstCustomer(node.children);
              if (found) return found;
            }
          }
          return null;
        };
        matchedCustomerId = findFirstCustomer([rootArea]);
      }

      const key = matchedCustomerId || '__unmatched__';
      if (!grouped.has(key)) grouped.set(key, []);
      grouped.get(key)!.push(rootArea);
    });

    // 组装成 [{ customer, tree }] 数组
    const result: Array<{ customer: typeof customers[number] | null; tree: Area[] }> = [];
    
    customers.forEach(c => {
      const tree = grouped.get(c.id);
      if (tree?.length) result.push({ customer: c, tree });
    });

    // 把无法匹配的客户也加上（可能有数据但没对应客户）
    const unmatched = grouped.get('__unmatched__');
    if (unmatched?.length) {
      result.push({ customer: null, tree: unmatched });
    }

    return result;
  }, [areaTree, areas, customers]);

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
                onClick={() => { setIsAddModalOpen(false); setActionError(null); }}
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
                                disabled={submitting}
                                className="w-full bg-gray-900 border border-white/20 rounded-lg px-4 py-2 text-white focus:outline-none focus:border-blue-500 disabled:opacity-50"
                                onKeyDown={e => e.key === 'Enter' && !submitting && handleAddNode()}
                            />
                        </div>

                        {actionError && (
                          <p className="text-red-400 text-xs">{actionError}</p>
                        )}

                        <div className="flex justify-end gap-2">
                            <button 
                                onClick={() => { setIsAddModalOpen(false); setActionError(null); }}
                                disabled={submitting}
                                className="px-4 py-2 text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                            >
                                取消
                            </button>
                            <button 
                                onClick={handleAddNode}
                                disabled={!newItemName.trim() || submitting || (addMode === 'root' && !selectedCustomerForRoot)}
                                className="px-4 py-2 bg-blue-500 text-white rounded-lg hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                            >
                                {submitting && <Loader2 className="w-4 h-4 animate-spin" />}
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
                onClick={() => { setIsEditModalOpen(false); setActionError(null); }}
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
                                disabled={submitting}
                                className="w-full bg-gray-900 border border-white/20 rounded-lg px-4 py-2 text-white focus:outline-none focus:border-blue-500 disabled:opacity-50"
                                onKeyDown={e => e.key === 'Enter' && !submitting && handleEditNode()}
                            />
                        </div>

                        {actionError && (
                          <p className="text-red-400 text-xs">{actionError}</p>
                        )}

                        <div className="flex justify-end gap-2">
                            <button 
                                onClick={() => { setIsEditModalOpen(false); setActionError(null); }}
                                disabled={submitting}
                                className="px-4 py-2 text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                            >
                                取消
                            </button>
                            <button 
                                onClick={handleEditNode}
                                disabled={!newItemName.trim() || submitting}
                                className="px-4 py-2 bg-blue-500 text-white rounded-lg hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                            >
                                {submitting && <Loader2 className="w-4 h-4 animate-spin" />}
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
                onClick={() => { setIsDeleteModalOpen(false); setActionError(null); }}
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
                    
                    <p className="text-gray-300 mb-2 text-sm">
                        确定要删除 <span className="text-white font-semibold">{selectedNode?.data.name}</span> 吗？
                    </p>
                    <p className="text-yellow-400/80 text-xs mb-4">
                        注意：如果该区域下存在子区域或已绑定设备，删除将失败。
                    </p>

                    {actionError && (
                      <p className="text-red-400 text-xs mb-4">{actionError}</p>
                    )}

                    <div className="flex justify-end gap-2">
                        <button 
                            onClick={() => { setIsDeleteModalOpen(false); setActionError(null); }}
                            disabled={submitting}
                            className="px-4 py-2 text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                        >
                            取消
                        </button>
                        <button 
                            onClick={handleDeleteNode}
                            disabled={submitting}
                            className="px-4 py-2 bg-red-500 text-white rounded-lg hover:bg-red-600 transition-colors disabled:opacity-50 flex items-center gap-2"
                        >
                            {submitting && <Loader2 className="w-4 h-4 animate-spin" />}
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
          <div className="flex items-center gap-2">
            <h2 className="font-semibold text-white">区域导航</h2>
            {loading && <Loader2 className="w-4 h-4 text-blue-400 animate-spin" />}
          </div>
          {hasPermission(PERMISSIONS.CREATE_AREAS) && (
            <button 
                onClick={() => {
                    setAddMode('root');
                    setNewItemName('');
                    setSelectedCustomerForRoot('');
                    setActionError(null);
                    setIsAddModalOpen(true);
                }}
                className="p-1.5 hover:bg-white/10 rounded-lg transition-colors text-blue-400" 
                title="添加根节点区域"
            >
                <Plus className="w-4 h-4" />
            </button>
          )}
        </div>
        
        {/* Global Error */}
        {error && (
          <div className="mx-4 mt-2 p-2 bg-red-500/10 border border-red-500/30 rounded-lg text-red-400 text-xs">
            {error}
            <button onClick={refreshAreas} className="ml-2 underline">重试</button>
          </div>
        )}

        <div className="flex-1 overflow-y-auto p-2">
          {loading && areas.length === 0 ? (
            <div className="flex items-center justify-center py-8 text-gray-500 text-sm">
              <Loader2 className="w-4 h-4 mr-2 animate-spin" />
              加载中...
            </div>
          ) : customerAreaTrees.length === 0 ? (
            customers.map(customer => (
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
                <div className="pl-6 py-2 text-xs text-gray-500">暂无区域数据</div>
              </TreeItem>
            ))
          ) : customerAreaTrees.map(({ customer, tree }) => {
             const custId = customer?.id ?? '__no_customer__';
             return (
               <TreeItem
                 key={`cust-${custId}`}
                 label={customer?.name || '未分配区域'}
                 icon={<Building2 className="w-4 h-4 text-purple-400" />}
                 level={0}
                 isActive={selectedNodeId === custId}
                 onSelect={() => setSelectedNodeId(custId)}
                 isExpanded={expandedNodes.has(custId)}
                 onToggle={() => toggleNode(custId)}
               >
                 {tree.length > 0 && renderAreaTree(tree, 1)}
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
                    {selectedNode.type !== 'customer' && <Folder className="w-5 h-5 text-yellow-400" />}
                 </div>
                 <div>
                   <h1 className="text-lg font-bold text-white">{selectedNode.data.name}</h1>
                   <p className="text-xs text-gray-400">
                     {getLevelLabel(selectedNode.type)}
                     {selectedNode.type !== 'customer' && selectedNode.data.deviceCount > 0 && 
                       ` · ${selectedNode.data.deviceCount} 个设备`}
                   </p>
                 </div>
               </div>
               <div className="flex gap-2">
                 {/* Actions */}
                 {hasPermission(PERMISSIONS.CREATE_AREAS) && (
                    <button 
                        onClick={() => {
                            setAddMode('child');
                            setNewItemName('');
                            setActionError(null);
                            setIsAddModalOpen(true);
                        }}
                        disabled={loading}
                        className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 text-blue-400 rounded-lg text-sm transition-colors border border-blue-500/30 disabled:opacity-50"
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
                                    setActionError(null);
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
                                onClick={() => { setActionError(null); setIsDeleteModalOpen(true); }}
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
                 {/* List View (Table-like) */}
                 <div className="p-6">
                   <div className="bg-white/5 border border-white/10 rounded-lg overflow-hidden">
                     <table className="w-full text-sm text-left">
                       <thead className="bg-white/5 text-gray-400 border-b border-white/10">
                         <tr>
                           <th className="px-6 py-3 font-medium">区域名称</th>
                           <th className="px-6 py-3 font-medium text-right">类型</th>
                           <th className="px-6 py-3 font-medium text-right">设备数</th>
                         </tr>
                       </thead>
                       <tbody className="divide-y divide-white/5">
                         {loading ? (
                           <tr><td colSpan={3} className="px-6 py-8 text-center text-gray-500">
                             <Loader2 className="w-4 h-4 inline mr-2 animate-spin" />加载中...
                           </td></tr>
                         ) : selectedNode.type === 'customer' ? (
                            // 客户节点：从 customerAreaTrees 中取该客户的完整子树（保持层级）
                            (() => {
                              const entry = customerAreaTrees.find(e => e.customer?.id === selectedNode.data.id);
                              const custAreas = entry?.tree || [];
                              return custAreas.length > 0 ? (
                                custAreas.map(area => (
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
                                              {area.children && area.children.length > 0 && (
                                                <span className="text-gray-500 text-xs">({area.children.length} 子区域)</span>
                                              )}
                                          </div>
                                          <ChevronRight className="w-4 h-4 text-gray-500 group-hover:text-white transition-colors" />
                                      </td>
                                      <td className="px-6 py-3 text-right text-gray-400">{area.type}</td>
                                      <td className="px-6 py-3 text-right text-gray-400">{area.deviceCount}</td>
                                  </tr>
                                ))
                              ) : (
                                <tr><td colSpan={3} className="px-6 py-8 text-center text-gray-500">暂无区域数据</td></tr>
                              );
                            })()
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
                                                <Folder className="w-4 h-4 text-yellow-400" />
                                                <span>{child.name}</span>
                                            </div>
                                            <ChevronRight className="w-4 h-4 text-gray-500 group-hover:text-white transition-colors" />
                                        </td>
                                        <td className="px-6 py-3 text-right text-gray-400">{child.type}</td>
                                        <td className="px-6 py-3 text-right text-gray-400">{child.deviceCount}</td>
                                    </tr>
                                ))
                            ) : (
                                <tr><td colSpan={3} className="px-6 py-8 text-center text-gray-500">暂无子区域</td></tr>
                            )
                         )}
                       </tbody>
                     </table>
                   </div>
                 </div>
             </div>
          </>
        )}
      </div>
      
    </div>
  );
}
