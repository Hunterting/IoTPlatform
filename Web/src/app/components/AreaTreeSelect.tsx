import { useState, useRef, useEffect } from 'react';
import { ChevronRight, ChevronDown, Check, Folder, File, Building2, Layout } from 'lucide-react';
import { Area } from '@/app/contexts/AreaContext';
import { motion, AnimatePresence } from 'motion/react';

interface AreaTreeSelectProps {
  areas: Area[];
  value?: string; // Selected Area Name (as per current DeviceItem) or ID? 
                  // The DeviceItem uses 'location' string which matches Area Name.
                  // But for robustness we might want ID, but let's stick to Name as per existing data structure to minimize refactor.
                  // Actually, better to pass ID and Name if possible, but let's stick to passing the Name as value to match existing form.
  onChange: (name: string, id: string) => void;
  placeholder?: string;
  className?: string;
}

export function AreaTreeSelect({ areas, value, onChange, placeholder = '请选择区域', className = '' }: AreaTreeSelectProps) {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Close when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Helper to flatten and find selected name for display if needed
  // But we pass 'value' which is the name.

  return (
    <div className={`relative ${className}`} ref={containerRef}>
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center justify-between px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
      >
        <span className={value ? 'text-white' : 'text-gray-500'}>
          {value || placeholder}
        </span>
        <ChevronDown className={`w-4 h-4 text-gray-400 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      <AnimatePresence>
        {isOpen && (
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="absolute z-50 w-full mt-2 bg-gray-800 border border-white/10 rounded-lg shadow-xl max-h-60 overflow-y-auto"
          >
            <div className="p-2">
              {/* Clear selection option */}
              {value && (
                <button
                  type="button"
                  onClick={() => {
                    onChange('', '');
                    setIsOpen(false);
                  }}
                  className="w-full flex items-center gap-2 px-2 py-2 mb-2 rounded-md text-gray-400 hover:bg-red-500/10 hover:text-red-400 transition-colors text-sm border-b border-white/10"
                >
                  <span className="ml-8">清除选择</span>
                </button>
              )}
              
              {areas.length > 0 ? (
                <TreeList 
                  nodes={areas} 
                  selectedValue={value} 
                  onSelect={(node) => {
                    onChange(node.name, node.id);
                    setIsOpen(false);
                  }} 
                />
              ) : (
                <div className="p-4 text-center text-gray-500 text-sm">暂无区域数据</div>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function TreeList({ 
  nodes, 
  level = 0, 
  selectedValue, 
  onSelect 
}: { 
  nodes: Area[]; 
  level?: number; 
  selectedValue?: string;
  onSelect: (node: Area) => void;
}) {
  return (
    <div className="flex flex-col">
      {nodes.map((node) => (
        <TreeNode 
          key={node.id} 
          node={node} 
          level={level} 
          selectedValue={selectedValue}
          onSelect={onSelect}
        />
      ))}
    </div>
  );
}

function TreeNode({ 
  node, 
  level, 
  selectedValue,
  onSelect 
}: { 
  node: Area; 
  level: number; 
  selectedValue?: string;
  onSelect: (node: Area) => void;
}) {
  // Default expand for level 1 and level 2, collapse for level 3 (to reduce clutter)
  const [isExpanded, setIsExpanded] = useState(level < 2);
  const hasChildren = node.children && node.children.length > 0;
  const isSelected = node.name === selectedValue;

  const getIcon = () => {
    switch (node.type) {
      case 'level1': return <Layout className="w-4 h-4 text-yellow-400" />;
      case 'level2': return <Folder className="w-4 h-4 text-yellow-400" />;
      case 'level3': return <File className="w-4 h-4 text-blue-400" />;
      default: return <Building2 className="w-4 h-4 text-purple-400" />;
    }
  };

  return (
    <div>
      <div 
        className={`flex items-center gap-2 px-2 py-2 rounded-md cursor-pointer transition-colors ${
          isSelected ? 'bg-blue-500/20 text-blue-400' : 'text-gray-300 hover:bg-white/5'
        }`}
        style={{ paddingLeft: `${level * 16 + 8}px` }}
        onClick={() => {
            // If it has children, just toggle expand? Or can we select parent nodes?
            // Usually devices are placed in leaf nodes (level 3), but maybe level 2.
            // Let's allow selecting any node.
            onSelect(node);
        }}
      >
        <div 
            className="w-4 h-4 flex items-center justify-center cursor-pointer hover:bg-white/10 rounded shrink-0"
            onClick={(e) => {
                if (hasChildren) {
                    e.stopPropagation();
                    setIsExpanded(!isExpanded);
                }
            }}
        >
             {hasChildren ? (
                 isExpanded ? <ChevronDown className="w-3 h-3 text-gray-500" /> : <ChevronRight className="w-3 h-3 text-gray-500" />
             ) : (
                 <div className="w-3 h-3" /> // Spacer for alignment
             )}
        </div>
        
        {getIcon()}
        <span className="text-sm truncate flex-1">{node.name}</span>
        {isSelected && <Check className="w-4 h-4 text-blue-500 shrink-0" />}
      </div>
      
      <AnimatePresence>
        {hasChildren && isExpanded && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.2 }}
          >
            <TreeList 
              nodes={node.children!} 
              level={level + 1} 
              selectedValue={selectedValue}
              onSelect={onSelect}
            />
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}