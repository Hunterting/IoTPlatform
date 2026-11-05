import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 单个时间桶的聚合点 */
export interface EnergyPoint {
  label: string;
  value: number;
  sampleCount: number;
}

/** 用能卡片汇总（真实口径：当日平均 + 累计采样数） */
export interface EnergySummary {
  today: number;
  total: number;
  todayLabel: string;
  totalLabel: string;
}

/** 用能分析完整响应 */
export interface EnergyAnalysis {
  energyType: string;
  metric: string;
  unit: string;
  year: number;
  month: number;
  areaId?: number | null;
  hasData: boolean;
  sampleCount: number;
  source: string;
  summary: EnergySummary;
  daily: EnergyPoint[];
  monthly: EnergyPoint[];
  yearly: EnergyPoint[];
}

/** 通用 API 响应信封 */
export interface ApiResponse<T = unknown> {
  code: number;
  data?: T;
  message?: string;
  success?: boolean;
}

// ── API 函数 ──────────────────────────────────────────────────────────────────

const BASE_URL = '/analytics';

/**
 * 用能分析（一次返回卡片汇总 + 日历/月柱/年线三组真实聚合数据）
 */
export async function getEnergyAnalysis(params?: {
  energyType?: 'electric' | 'water' | 'gas';
  year?: number;
  month?: number;
  areaId?: number;
}): Promise<ApiResponse<EnergyAnalysis>> {
  const searchParams = new URLSearchParams();
  if (params?.energyType) searchParams.set('energyType', params.energyType);
  if (params?.year) searchParams.set('year', params.year.toString());
  if (params?.month) searchParams.set('month', params.month.toString());
  if (params?.areaId) searchParams.set('areaId', params.areaId.toString());

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<EnergyAnalysis>>(
    `${BASE_URL}/energy${query ? `?${query}` : ''}`
  );
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const analyticsApi = {
  getEnergyAnalysis,
};

export default analyticsApi;
