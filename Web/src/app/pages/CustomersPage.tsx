import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Plus,
  Search,
  Edit,
  Trash2,
  Building2,
  FileText,
  Upload,
  Eye,
  X,
  ArrowRight,
} from 'lucide-react';
import { useAuth, Customer, Project, Contract, WorkSummary } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';

export function CustomersPage() {
  const { customers, user, hasPermission, updateCustomer, updateCustomerProjects, addCustomer } = useAuth();
  const [searchTerm, setSearchTerm] = useState('');
  const [showAddModal, setShowAddModal] = useState(false);
  const [showDetailModal, setShowDetailModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [showAddProjectModal, setShowAddProjectModal] = useState(false);
  const [showUploadContractModal, setShowUploadContractModal] = useState(false);
  const [showPreviewModal, setShowPreviewModal] = useState(false);
  const [selectedCustomer, setSelectedCustomer] = useState<Customer | null>(null);
  const [selectedProject, setSelectedProject] = useState<Project | null>(null);
  const [showProjectDetailModal, setShowProjectDetailModal] = useState(false);
  const [showAddWorkSummaryModal, setShowAddWorkSummaryModal] = useState(false);
  const [previewContract, setPreviewContract] = useState<Contract | null>(null);

  // 当 AuthContext 里 customers 更新时，同步刷新已选中的 customer
  useEffect(() => {
    if (selectedCustomer) {
      const fresh = customers.find(c => c.id === selectedCustomer.id);
      if (fresh) setSelectedCustomer(fresh);
    }
  }, [customers]);

  const filteredCustomers = customers
    .filter(customer => {
      if (user?.role === 'super_admin') return true;
      if (user?.appCode) return customer.appCode === user.appCode;
      return false;
    })
    .filter(customer =>
      customer.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      customer.code.toLowerCase().includes(searchTerm.toLowerCase())
    );

  const handleViewDetail = (customer: Customer) => {
    setSelectedCustomer(customer);
    setShowDetailModal(true);
  };

  const handleEdit = (customer: Customer) => {
    setSelectedCustomer(customer);
    setShowEditModal(true);
  };

  const handleDelete = (customer: Customer) => {
    setSelectedCustomer(customer);
    setShowDeleteModal(true);
  };

  const confirmDelete = () => {
    // 删除客户：通过 updateCustomer 标记为 inactive 或直接从列表移除
    // 这里简单实现：让父组件知道，实际上我们在 AuthContext 里没有 deleteCustomer
    // 暂时用 status 更改来模拟删除
    if (selectedCustomer) {
      updateCustomer(selectedCustomer.id, { status: 'inactive' });
      setShowDeleteModal(false);
      setSelectedCustomer(null);
    }
  };

  // ── 项目操作（所有变更都通过 updateCustomerProjects 写回 AuthContext）────────

  const handleAddProject = (projectForm: { name: string; address: string; status: string; deviceCount: number; onlineDate: string }) => {
    if (!selectedCustomer) return;
    const newProject: Project = {
      id: Date.now().toString(),
      name: projectForm.name,
      address: projectForm.address,
      status: (projectForm.status as Project['status']) || 'planning',
      deviceCount: projectForm.deviceCount || 0,
      onlineDate: projectForm.onlineDate || '',
      contracts: [],
      workSummaries: [],
    };
    const updatedProjects = [...selectedCustomer.projects, newProject];
    updateCustomerProjects(selectedCustomer.id, updatedProjects);
    setShowAddProjectModal(false);
  };

  const handleUploadContract = (contract: Omit<Contract, 'id' | 'uploadDate'>) => {
    if (!selectedCustomer || !selectedProject) return;
    const newContract: Contract = {
      ...contract,
      id: Date.now().toString(),
      uploadDate: new Date().toISOString().split('T')[0],
    };
    const updatedProjects = selectedCustomer.projects.map(p =>
      p.id === selectedProject.id
        ? { ...p, contracts: [...p.contracts, newContract] }
        : p
    );
    updateCustomerProjects(selectedCustomer.id, updatedProjects);
    // 更新 selectedProject 本地引用
    const freshProject = updatedProjects.find(p => p.id === selectedProject.id);
    if (freshProject) setSelectedProject(freshProject);
    setShowUploadContractModal(false);
  };

  const handlePreviewContract = (contract: Contract) => {
    setPreviewContract(contract);
    setShowPreviewModal(true);
  };

  const handleDownloadContract = (contract: Contract) => {
    const link = document.createElement('a');
    link.href = contract.fileUrl;
    link.download = contract.name + '.pdf';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const handleDeleteContract = (contract: Contract) => {
    if (!selectedCustomer || !selectedProject) return;
    if (!window.confirm(`确定要删除合同 "${contract.name}" 吗？`)) return;
    const updatedProjects = selectedCustomer.projects.map(p =>
      p.id === selectedProject.id
        ? { ...p, contracts: p.contracts.filter(c => c.id !== contract.id) }
        : p
    );
    updateCustomerProjects(selectedCustomer.id, updatedProjects);
    const freshProject = updatedProjects.find(p => p.id === selectedProject.id);
    if (freshProject) setSelectedProject(freshProject);
  };

  const handleUpdateProject = (updatedProject: Project) => {
    if (!selectedCustomer) return;
    const updatedProjects = selectedCustomer.projects.map(p =>
      p.id === updatedProject.id ? updatedProject : p
    );
    updateCustomerProjects(selectedCustomer.id, updatedProjects);
    setSelectedProject(updatedProject);
  };

  const handleAddWorkSummary = (summary: Omit<WorkSummary, 'id'>) => {
    if (!selectedCustomer || !selectedProject) return;
    const newSummary: WorkSummary = { ...summary, id: Date.now().toString() };
    const updatedProject: Project = {
      ...selectedProject,
      workSummaries: [newSummary, ...(selectedProject.workSummaries || [])],
    };
    handleUpdateProject(updatedProject);
    setShowAddWorkSummaryModal(false);
  };

  const getProjectStatusBadge = (status: string) => {
    switch (status) {
      case 'online':   return <span className="px-2 py-1 bg-green-500/20 text-green-400 border border-green-500/30 rounded text-xs">已上线</span>;
      case 'building': return <span className="px-2 py-1 bg-blue-500/20 text-blue-400 border border-blue-500/30 rounded text-xs">建设中</span>;
      case 'planning': return <span className="px-2 py-1 bg-amber-500/20 text-amber-400 border border-amber-500/30 rounded text-xs">规划中</span>;
      case 'offline':  return <span className="px-2 py-1 bg-gray-500/20 text-gray-400 border border-gray-500/30 rounded text-xs">已下线</span>;
      default: return <></>;
    }
  };

  const getContractTypeBadge = (type: string) => {
    switch (type) {
      case 'service':  return <span className="px-2 py-1 bg-blue-500/20 text-blue-400 rounded text-xs">服务合同</span>;
      case 'purchase': return <span className="px-2 py-1 bg-purple-500/20 text-purple-400 rounded text-xs">采购合同</span>;
      case 'other':    return <span className="px-2 py-1 bg-gray-500/20 text-gray-400 rounded text-xs">其他</span>;
      default: return <></>;
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">客户管理</h1>
          <p className="text-gray-400">管理所有租户客户信息与权限</p>
        </div>
        {hasPermission(PERMISSIONS.CREATE_CUSTOMERS) && (
          <button
            onClick={() => setShowAddModal(true)}
            className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all"
          >
            <Plus className="w-5 h-5" />
            <span>添加客户</span>
          </button>
        )}
      </div>

      <div className="relative">
        <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
        <input
          type="text"
          placeholder="搜索客户名称或编号..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          className="w-full pl-12 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
        />
      </div>

      <div className="grid grid-cols-4 gap-4">
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="p-6 bg-gradient-to-br from-blue-500/20 to-cyan-500/10 border border-blue-500/30 rounded-xl">
          <div className="flex items-center justify-between mb-2">
            <Building2 className="w-8 h-8 text-blue-400" />
            <span className="text-3xl font-bold text-white">{filteredCustomers.length}</span>
          </div>
          <p className="text-gray-300">总客户数</p>
        </motion.div>
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.05 }} className="p-6 bg-gradient-to-br from-purple-500/20 to-pink-500/10 border border-purple-500/30 rounded-xl">
          <div className="flex items-center justify-between mb-2">
            <FileText className="w-8 h-8 text-purple-400" />
            <span className="text-3xl font-bold text-white">
              {filteredCustomers.reduce((sum, c) => sum + (c.projects?.length ?? 0), 0)}
            </span>
          </div>
          <p className="text-gray-300">总项目数</p>
        </motion.div>
      </div>

      <div className="bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-white/10">
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">客户信息</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">编号</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">联系方式</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">项目数</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">状态</th>
                <th className="px-6 py-4 text-center text-sm font-semibold text-gray-300">操作</th>
              </tr>
            </thead>
            <tbody>
              {filteredCustomers.map((customer, index) => (
                <motion.tr key={customer.id} initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.05 }} className="border-b border-white/5 hover:bg-white/5 transition-colors">
                  <td className="px-6 py-4">
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 bg-gradient-to-br from-blue-500 to-purple-500 rounded-lg flex items-center justify-center">
                        <Building2 className="w-5 h-5 text-white" />
                      </div>
                      <div>
                        <p className="font-medium text-white">{customer.name}</p>
                        <p className="text-xs text-gray-400 mt-1">{customer.address}</p>
                      </div>
                    </div>
                  </td>
                  <td className="px-6 py-4 text-gray-300">{customer.code}</td>
                  <td className="px-6 py-4">
                    <div className="text-sm text-gray-300">{customer.contact}</div>
                    <div className="text-xs text-gray-400">{customer.phone}</div>
                  </td>
                  <td className="px-6 py-4">
                    <div className="text-sm text-gray-300">{customer.projects?.length ?? 0} 个项目</div>
                  </td>
                  <td className="px-6 py-4">
                    <span className={`inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium ${customer.status === 'active' ? 'bg-green-500/20 text-green-400' : 'bg-gray-500/20 text-gray-400'}`}>
                      {customer.status === 'active' ? '活跃' : '未激活'}
                    </span>
                  </td>
                  <td className="px-6 py-4">
                    <div className="flex items-center justify-center gap-2">
                      <button onClick={() => handleViewDetail(customer)} className="p-2 hover:bg-green-500/20 text-green-400 rounded-lg transition-colors" title="查看">
                        <Eye className="w-4 h-4" />
                      </button>
                      {hasPermission(PERMISSIONS.UPDATE_CUSTOMERS) && (
                        <button onClick={() => handleEdit(customer)} className="p-2 hover:bg-blue-500/20 text-blue-400 rounded-lg transition-colors" title="编辑">
                          <Edit className="w-4 h-4" />
                        </button>
                      )}
                      {hasPermission(PERMISSIONS.DELETE_CUSTOMERS) && (
                        <button onClick={() => handleDelete(customer)} className="p-2 hover:bg-red-500/20 text-red-400 rounded-lg transition-colors" title="删除">
                          <Trash2 className="w-4 h-4" />
                        </button>
                      )}
                    </div>
                  </td>
                </motion.tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* ── 详情弹窗 ── */}
      <AnimatePresence>
        {showDetailModal && selectedCustomer && (
          <CustomerDetailModal
            customer={selectedCustomer}
            onClose={() => setShowDetailModal(false)}
            onAddProject={() => setShowAddProjectModal(true)}
            onViewProject={(project: Project) => { setSelectedProject(project); setShowProjectDetailModal(true); }}
            getProjectStatusBadge={getProjectStatusBadge}
            hasPermission={hasPermission}
          />
        )}
      </AnimatePresence>

      <AnimatePresence>
        {showProjectDetailModal && selectedProject && (
          <ProjectDetailModal
            project={selectedProject}
            onClose={() => setShowProjectDetailModal(false)}
            onUpdateProject={handleUpdateProject}
            onUploadContract={() => setShowUploadContractModal(true)}
            onPreviewContract={handlePreviewContract}
            onDownloadContract={handleDownloadContract}
            onDeleteContract={handleDeleteContract}
            onAddWorkSummary={() => setShowAddWorkSummaryModal(true)}
            getContractTypeBadge={getContractTypeBadge}
          />
        )}
      </AnimatePresence>

      <AddProjectModal show={showAddProjectModal} onClose={() => setShowAddProjectModal(false)} onSave={handleAddProject} />
      <UploadContractModal show={showUploadContractModal} onClose={() => setShowUploadContractModal(false)} onSave={handleUploadContract} />
      <AddWorkSummaryModal show={showAddWorkSummaryModal} onClose={() => setShowAddWorkSummaryModal(false)} onSave={handleAddWorkSummary} />
      <PreviewContractModal show={showPreviewModal} contract={previewContract} onClose={() => setShowPreviewModal(false)} />

      <CustomerFormModal
        show={showAddModal || showEditModal}
        onClose={() => { setShowAddModal(false); setShowEditModal(false); setSelectedCustomer(null); }}
        onSave={(data: Partial<Customer>) => {
          if (showEditModal && selectedCustomer) {
            updateCustomer(selectedCustomer.id, data);
          } else {
            addCustomer(data as Omit<Customer, 'id' | 'createdAt' | 'deviceCount' | 'status' | 'projects'>);
          }
          setShowAddModal(false);
          setShowEditModal(false);
          setSelectedCustomer(null);
        }}
        title={showEditModal ? '编辑客户' : '添加客户'}
        initialData={selectedCustomer || undefined}
      />

      {/* 删除确认弹窗 */}
      <AnimatePresence>
        {showDeleteModal && (
          <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60]" onClick={() => setShowDeleteModal(false)}>
            <div className="bg-gray-800 p-6 rounded-xl border border-white/10" onClick={e => e.stopPropagation()}>
              <h3 className="text-xl text-white mb-4">确认删除客户？</h3>
              <p className="text-gray-400 mb-4 text-sm">删除后该客户将变为未激活状态。</p>
              <div className="flex gap-4">
                <button onClick={() => setShowDeleteModal(false)} className="px-4 py-2 bg-white/10 rounded text-white">取消</button>
                <button onClick={confirmDelete} className="px-4 py-2 bg-red-500 rounded text-white">删除</button>
              </div>
            </div>
          </div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ── 子组件 ────────────────────────────────────────────────────────────────────

function CustomerDetailModal({ customer, onClose, onAddProject, onViewProject, getProjectStatusBadge, hasPermission }: any) {
  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4" onClick={onClose}>
      <div className="bg-gray-900 border border-white/10 rounded-xl max-w-4xl w-full max-h-[90vh] overflow-y-auto p-6" onClick={e => e.stopPropagation()}>
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-2xl font-bold text-white">{customer.name} - 详情</h2>
          <button onClick={onClose}><X className="text-gray-400" /></button>
        </div>
        <div className="mb-6 grid grid-cols-2 gap-4 text-gray-300">
          <div><span className="text-gray-500">地址:</span> {customer.address}</div>
          <div><span className="text-gray-500">联系人:</span> {customer.contact}</div>
          <div><span className="text-gray-500">电话:</span> {customer.phone}</div>
        </div>
        <div className="flex justify-between items-center mb-4">
          <h3 className="text-xl font-bold text-white">项目列表 <span className="text-base font-normal text-gray-400">（共 {customer.projects.length} 个）</span></h3>
          <button onClick={onAddProject} className="flex items-center gap-2 px-3 py-1 bg-blue-500 rounded text-white text-sm">
            <Plus className="w-4 h-4" />添加项目
          </button>
        </div>
        <div className="space-y-3">
          {(customer.projects ?? []).map((p: Project) => (
            <div key={p.id} className="p-4 bg-white/5 rounded border border-white/10 flex justify-between items-center cursor-pointer hover:bg-white/10" onClick={() => onViewProject(p)}>
              <div>
                <div className="font-bold text-white">{p.name}</div>
                <div className="text-sm text-gray-400">{p.address}</div>
              </div>
              <div className="flex items-center gap-4">
                {getProjectStatusBadge(p.status)}
                <ArrowRight className="text-gray-500 w-4 h-4" />
              </div>
            </div>
          ))}
          {(customer.projects ?? []).length === 0 && <div className="text-center text-gray-500 py-4">暂无项目，点击"添加项目"创建</div>}
        </div>
      </div>
    </div>
  );
}

function ProjectDetailModal({ project, onClose, onUpdateProject, onUploadContract, onPreviewContract, onDownloadContract, onDeleteContract, onAddWorkSummary, getContractTypeBadge }: any) {
  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4" onClick={onClose}>
      <div className="bg-gray-900 border border-white/10 rounded-xl max-w-4xl w-full max-h-[90vh] overflow-y-auto p-6" onClick={e => e.stopPropagation()}>
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-2xl font-bold text-white">{project.name}</h2>
          <button onClick={onClose}><X className="text-gray-400" /></button>
        </div>
        <div className="grid grid-cols-2 gap-6 mb-6">
          <div>
            <h3 className="font-bold text-white mb-3">合同文件</h3>
            <div className="space-y-2">
              {project.contracts.map((c: Contract) => (
                <div key={c.id} className="flex justify-between items-center p-2 bg-white/5 rounded">
                  <div className="flex items-center gap-2 text-white">
                    <FileText className="w-4 h-4 text-blue-400" />
                    <span className="text-sm cursor-pointer hover:underline" onClick={() => onPreviewContract(c)}>{c.name}</span>
                  </div>
                  <button onClick={() => onDeleteContract(c)} className="text-red-400 hover:text-red-300"><Trash2 className="w-4 h-4" /></button>
                </div>
              ))}
              <button onClick={onUploadContract} className="w-full py-2 border border-dashed border-gray-600 text-gray-400 hover:border-gray-400 hover:text-gray-300 rounded flex justify-center gap-2">
                <Upload className="w-4 h-4" /> 上传合同
              </button>
            </div>
          </div>
          <div>
            <h3 className="font-bold text-white mb-3">工作纪要</h3>
            <div className="space-y-2 max-h-60 overflow-y-auto">
              {project.workSummaries?.map((w: WorkSummary) => (
                <div key={w.id} className="p-3 bg-white/5 rounded">
                  <div className="text-xs text-gray-500 mb-1">{w.date} - {w.assignee}</div>
                  <div className="text-sm text-gray-300">{w.workContent}</div>
                </div>
              ))}
              <button onClick={onAddWorkSummary} className="w-full py-2 bg-white/5 hover:bg-white/10 text-blue-400 rounded text-sm mt-2">添加纪要</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function AddProjectModal({ show, onClose, onSave }: any) {
  const [form, setForm] = useState({ name: '', address: '', status: 'planning', deviceCount: 0, onlineDate: '' });
  if (!show) return null;
  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60]" onClick={onClose}>
      <div className="bg-gray-800 p-6 rounded-xl w-full max-w-md border border-white/10" onClick={e => e.stopPropagation()}>
        <h3 className="text-xl font-bold text-white mb-4">添加项目</h3>
        <input className="w-full mb-3 p-2 bg-white/5 rounded border border-white/10 text-white placeholder-gray-500" placeholder="项目名称" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
        <input className="w-full mb-3 p-2 bg-white/5 rounded border border-white/10 text-white placeholder-gray-500" placeholder="地址" value={form.address} onChange={e => setForm({ ...form, address: e.target.value })} />
        <select className="w-full mb-3 p-2 bg-gray-700 rounded border border-white/10 text-white" value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>
          <option value="planning">规划中</option>
          <option value="building">建设中</option>
          <option value="online">已上线</option>
          <option value="offline">已下线</option>
        </select>
        <div className="flex gap-2 mb-4">
          <button onClick={() => { onSave(form); setForm({ name: '', address: '', status: 'planning', deviceCount: 0, onlineDate: '' }); }} className="flex-1 bg-blue-500 text-white py-2 rounded">保存</button>
          <button onClick={onClose} className="flex-1 bg-white/10 text-white py-2 rounded">取消</button>
        </div>
      </div>
    </div>
  );
}

function UploadContractModal({ show, onClose, onSave }: any) {
  const [form, setForm] = useState({ name: '', type: 'service', fileSize: '1MB', fileUrl: 'https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf' });
  if (!show) return null;
  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60]" onClick={onClose}>
      <div className="bg-gray-800 p-6 rounded-xl w-full max-w-md border border-white/10" onClick={e => e.stopPropagation()}>
        <h3 className="text-xl font-bold text-white mb-4">上传合同</h3>
        <input className="w-full mb-3 p-2 bg-white/5 rounded border border-white/10 text-white placeholder-gray-500" placeholder="合同名称" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
        <select className="w-full mb-3 p-2 bg-gray-700 rounded border border-white/10 text-white" value={form.type} onChange={e => setForm({ ...form, type: e.target.value })}>
          <option value="service">服务合同</option>
          <option value="purchase">采购合同</option>
          <option value="other">其他</option>
        </select>
        <div className="flex gap-2 mb-4">
          <button onClick={() => onSave(form)} className="flex-1 bg-blue-500 text-white py-2 rounded">上传</button>
          <button onClick={onClose} className="flex-1 bg-white/10 text-white py-2 rounded">取消</button>
        </div>
      </div>
    </div>
  );
}

function AddWorkSummaryModal({ show, onClose, onSave }: any) {
  const [form, setForm] = useState({ workContent: '', assignee: '', feedbackPerson: '', date: new Date().toISOString().split('T')[0] });
  if (!show) return null;
  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60]" onClick={onClose}>
      <div className="bg-gray-800 p-6 rounded-xl w-full max-w-md border border-white/10" onClick={e => e.stopPropagation()}>
        <h3 className="text-xl font-bold text-white mb-4">添加工作纪要</h3>
        <textarea className="w-full mb-3 p-2 bg-white/5 rounded border border-white/10 text-white h-24 placeholder-gray-500" placeholder="工作内容" value={form.workContent} onChange={e => setForm({ ...form, workContent: e.target.value })} />
        <input className="w-full mb-3 p-2 bg-white/5 rounded border border-white/10 text-white placeholder-gray-500" placeholder="负责人" value={form.assignee} onChange={e => setForm({ ...form, assignee: e.target.value })} />
        <input className="w-full mb-3 p-2 bg-white/5 rounded border border-white/10 text-white placeholder-gray-500" placeholder="反馈人" value={form.feedbackPerson} onChange={e => setForm({ ...form, feedbackPerson: e.target.value })} />
        <div className="flex gap-2 mb-4">
          <button onClick={() => onSave(form)} className="flex-1 bg-blue-500 text-white py-2 rounded">保存</button>
          <button onClick={onClose} className="flex-1 bg-white/10 text-white py-2 rounded">取消</button>
        </div>
      </div>
    </div>
  );
}

function PreviewContractModal({ show, contract, onClose }: any) {
  if (!show || !contract) return null;
  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60]" onClick={onClose}>
      <div className="bg-gray-900 p-6 rounded-xl w-full max-w-4xl h-[80vh] flex flex-col border border-white/10" onClick={e => e.stopPropagation()}>
        <div className="flex justify-between items-center mb-4">
          <h3 className="text-xl font-bold text-white">{contract.name}</h3>
          <button onClick={onClose}><X className="text-gray-400" /></button>
        </div>
        <div className="flex-1 bg-white/5 rounded flex items-center justify-center text-gray-400">
          PDF 预览区域（模拟）
        </div>
      </div>
    </div>
  );
}

function CustomerFormModal({ show, onClose, onSave, title, initialData }: any) {
  const [form, setForm] = useState(initialData || { name: '', code: '', contact: '', phone: '', address: '', appCode: '' });

  useEffect(() => {
    if (initialData) setForm(initialData);
    else setForm({ name: '', code: '', contact: '', phone: '', address: '', appCode: '' });
  }, [initialData, show]);

  if (!show) return null;
  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60]" onClick={onClose}>
      <div className="bg-gray-800 p-6 rounded-xl w-full max-w-2xl border border-white/10" onClick={e => e.stopPropagation()}>
        <h3 className="text-xl font-bold text-white mb-6">{title}</h3>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="text-sm text-gray-400 block mb-1">客户名称</label>
            <input className="w-full p-2 bg-white/5 rounded border border-white/10 text-white" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
          </div>
          <div>
            <label className="text-sm text-gray-400 block mb-1">客户编号</label>
            <input className="w-full p-2 bg-white/5 rounded border border-white/10 text-white" value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} />
          </div>
          <div>
            <label className="text-sm text-gray-400 block mb-1">联系人</label>
            <input className="w-full p-2 bg-white/5 rounded border border-white/10 text-white" value={form.contact} onChange={e => setForm({ ...form, contact: e.target.value })} />
          </div>
          <div>
            <label className="text-sm text-gray-400 block mb-1">电话</label>
            <input className="w-full p-2 bg-white/5 rounded border border-white/10 text-white" value={form.phone} onChange={e => setForm({ ...form, phone: e.target.value })} />
          </div>
          <div className="col-span-2">
            <label className="text-sm text-gray-400 block mb-1">地址</label>
            <input className="w-full p-2 bg-white/5 rounded border border-white/10 text-white" value={form.address} onChange={e => setForm({ ...form, address: e.target.value })} />
          </div>
          <div className="col-span-2">
            <label className="text-sm text-gray-400 block mb-1">AppCode</label>
            <input className="w-full p-2 bg-white/5 rounded border border-white/10 text-white" value={form.appCode} onChange={e => setForm({ ...form, appCode: e.target.value })} />
          </div>
        </div>
        <div className="flex gap-3 mt-6">
          <button onClick={onClose} className="flex-1 bg-white/10 text-white py-3 rounded">取消</button>
          <button onClick={() => onSave(form)} className="flex-1 bg-blue-500 text-white py-3 rounded">保存</button>
        </div>
      </div>
    </div>
  );
}