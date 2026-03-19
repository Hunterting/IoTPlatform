import { useState } from 'react';
import { motion } from 'motion/react';
import { ShoppingCart, Search, Filter, Eye, CheckCircle, Clock, XCircle, Package } from 'lucide-react';

interface Order {
  id: string;
  orderNo: string;
  deviceName: string;
  quantity: number;
  amount: number;
  status: 'pending' | 'processing' | 'completed' | 'cancelled';
  paymentStatus: 'unpaid' | 'paid';
  date: string;
  customer: string;
}

export function OrdersPage() {
  const [searchTerm, setSearchTerm] = useState('');
  const [filterStatus, setFilterStatus] = useState<string>('all');

  const [orders] = useState<Order[]>([
    {
      id: '1',
      orderNo: 'ORD20260123001',
      deviceName: '智能冰箱 SR-F520BX',
      quantity: 2,
      amount: 15800,
      status: 'completed',
      paymentStatus: 'paid',
      date: '2026-01-20',
      customer: '海底捞火锅连锁',
    },
    {
      id: '2',
      orderNo: 'ORD20260123002',
      deviceName: '智能烤箱 OV-X8000',
      quantity: 1,
      amount: 12500,
      status: 'processing',
      paymentStatus: 'paid',
      date: '2026-01-21',
      customer: '喜茶门店管理',
    },
    {
      id: '3',
      orderNo: 'ORD20260123003',
      deviceName: '洗碗机 DW-M9000',
      quantity: 3,
      amount: 24000,
      status: 'pending',
      paymentStatus: 'unpaid',
      date: '2026-01-22',
      customer: '星巴克华东区',
    },
    {
      id: '4',
      orderNo: 'ORD20260123004',
      deviceName: '智能炉灶 GS-P4000',
      quantity: 4,
      amount: 32000,
      status: 'completed',
      paymentStatus: 'paid',
      date: '2026-01-18',
      customer: '海底捞火锅连锁',
    },
    {
      id: '5',
      orderNo: 'ORD20260123005',
      deviceName: '咖啡机 CF-L5000',
      quantity: 2,
      amount: 18600,
      status: 'cancelled',
      paymentStatus: 'unpaid',
      date: '2026-01-19',
      customer: '喜茶门店管理',
    },
  ]);

  const filteredOrders = orders.filter((order) => {
    const matchesSearch =
      order.orderNo.toLowerCase().includes(searchTerm.toLowerCase()) ||
      order.deviceName.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesStatus = filterStatus === 'all' || order.status === filterStatus;
    return matchesSearch && matchesStatus;
  });

  const statusStats = {
    pending: orders.filter((o) => o.status === 'pending').length,
    processing: orders.filter((o) => o.status === 'processing').length,
    completed: orders.filter((o) => o.status === 'completed').length,
    cancelled: orders.filter((o) => o.status === 'cancelled').length,
  };

  const totalAmount = orders
    .filter((o) => o.status === 'completed' && o.paymentStatus === 'paid')
    .reduce((sum, o) => sum + o.amount, 0);

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'pending':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-amber-500/20 text-amber-400 border border-amber-500/30">
            <Clock className="w-3 h-3" />
            待处理
          </span>
        );
      case 'processing':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-blue-500/20 text-blue-400 border border-blue-500/30">
            <Package className="w-3 h-3" />
            处理中
          </span>
        );
      case 'completed':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-green-500/20 text-green-400 border border-green-500/30">
            <CheckCircle className="w-3 h-3" />
            已完成
          </span>
        );
      case 'cancelled':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-gray-500/20 text-gray-400 border border-gray-500/30">
            <XCircle className="w-3 h-3" />
            已取消
          </span>
        );
    }
  };

  const getPaymentBadge = (status: string) => {
    return status === 'paid' ? (
      <span className="px-2 py-1 bg-green-500/20 text-green-400 rounded text-xs">已支付</span>
    ) : (
      <span className="px-2 py-1 bg-red-500/20 text-red-400 rounded text-xs">未支付</span>
    );
  };

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-3xl font-bold text-white mb-2">订单管理</h1>
        <p className="text-gray-400">设备采购订单查询与跟踪</p>
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-5 gap-4">
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="p-6 bg-gradient-to-br from-blue-500/20 to-cyan-500/10 border border-blue-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <ShoppingCart className="w-8 h-8 text-blue-400" />
            <span className="text-3xl font-bold text-white">{orders.length}</span>
          </div>
          <p className="text-gray-300">总订单数</p>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1 }}
          className="p-6 bg-gradient-to-br from-amber-500/20 to-orange-500/10 border border-amber-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <Clock className="w-8 h-8 text-amber-400" />
            <span className="text-3xl font-bold text-white">{statusStats.pending}</span>
          </div>
          <p className="text-gray-300">待处理</p>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.2 }}
          className="p-6 bg-gradient-to-br from-purple-500/20 to-pink-500/10 border border-purple-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <Package className="w-8 h-8 text-purple-400" />
            <span className="text-3xl font-bold text-white">{statusStats.processing}</span>
          </div>
          <p className="text-gray-300">处理中</p>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.3 }}
          className="p-6 bg-gradient-to-br from-green-500/20 to-emerald-500/10 border border-green-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <CheckCircle className="w-8 h-8 text-green-400" />
            <span className="text-3xl font-bold text-white">{statusStats.completed}</span>
          </div>
          <p className="text-gray-300">已完成</p>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.4 }}
          className="p-6 bg-gradient-to-br from-emerald-500/20 to-teal-500/10 border border-emerald-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <span className="text-emerald-400 text-2xl">¥</span>
            <span className="text-2xl font-bold text-white">{(totalAmount / 10000).toFixed(1)}万</span>
          </div>
          <p className="text-gray-300">总金额</p>
        </motion.div>
      </div>

      {/* Search and Filter */}
      <div className="flex items-center gap-4">
        <div className="flex-1 relative">
          <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
          <input
            type="text"
            placeholder="搜索订单号或设备名称..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-12 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>
        <div className="flex items-center gap-2">
          <Filter className="w-5 h-5 text-gray-400" />
          <select
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value)}
            className="px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
          >
            <option value="all" className="bg-gray-800 text-white">全部状态</option>
            <option value="pending" className="bg-gray-800 text-white">待处理</option>
            <option value="processing" className="bg-gray-800 text-white">处理中</option>
            <option value="completed" className="bg-gray-800 text-white">已完成</option>
            <option value="cancelled" className="bg-gray-800 text-white">已取消</option>
          </select>
        </div>
      </div>

      {/* Orders Table */}
      <div className="bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-white/10">
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">订单号</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">设备信息</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">客户</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">数量</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">金额</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">订单状态</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">支付状态</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">日期</th>
                <th className="px-6 py-4 text-center text-sm font-semibold text-gray-300">操作</th>
              </tr>
            </thead>
            <tbody>
              {filteredOrders.map((order, index) => (
                <motion.tr
                  key={order.id}
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: index * 0.05 }}
                  className="border-b border-white/5 hover:bg-white/5 transition-colors"
                >
                  <td className="px-6 py-4">
                    <span className="text-sm font-mono text-blue-400">{order.orderNo}</span>
                  </td>
                  <td className="px-6 py-4">
                    <span className="text-sm text-white">{order.deviceName}</span>
                  </td>
                  <td className="px-6 py-4">
                    <span className="text-sm text-gray-300">{order.customer}</span>
                  </td>
                  <td className="px-6 py-4">
                    <span className="text-sm text-white">{order.quantity}</span>
                  </td>
                  <td className="px-6 py-4">
                    <span className="text-sm font-semibold text-white">¥{order.amount.toLocaleString()}</span>
                  </td>
                  <td className="px-6 py-4">{getStatusBadge(order.status)}</td>
                  <td className="px-6 py-4">{getPaymentBadge(order.paymentStatus)}</td>
                  <td className="px-6 py-4">
                    <span className="text-sm text-gray-400">{order.date}</span>
                  </td>
                  <td className="px-6 py-4">
                    <div className="flex items-center justify-center">
                      <button className="p-2 hover:bg-blue-500/20 text-blue-400 rounded-lg transition-colors">
                        <Eye className="w-4 h-4" />
                      </button>
                    </div>
                  </td>
                </motion.tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}