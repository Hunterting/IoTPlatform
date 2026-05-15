import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import {
  ApiResponse,
  PagedResponse
} from './types';

/**
 * 项目数据类型
 */
export interface Project {
  id: string;
  customerId: string;
  name: string;
  address?: string;
  deviceCount: number;
  onlineDate?: string;
  status: 'planning' | 'building' | 'online' | 'offline';
  appCode?: string;
  createdAt: string;
  updatedAt: string;
  workSummaries: WorkSummary[];
}

/**
 * 工作纪要数据类型
 */
export interface WorkSummary {
  id: string;
  projectId: string;
  feedbackPerson?: string;
  assignee?: string;
  assistant?: string;
  workContent?: string;
  date: string;
  appCode?: string;
}

/**
 * 创建项目请求
 */
export interface CreateProjectRequest {
  customerId: string;
  name: string;
  address?: string;
  deviceCount?: number;
  onlineDate?: string;
  status?: 'planning' | 'building' | 'online' | 'offline';
  appCode?: string;
}

/**
 * 更新项目请求
 */
export interface UpdateProjectRequest {
  name: string;
  address?: string;
  deviceCount?: number;
  onlineDate?: string;
  status: 'planning' | 'building' | 'online' | 'offline';
  appCode?: string;
}

/**
 * 创建工作纪要请求
 */
export interface CreateWorkSummaryRequest {
  projectId: string;
  feedbackPerson?: string;
  assignee?: string;
  assistant?: string;
  workContent?: string;
  date: string;
  appCode?: string;
}

/**
 * 项目管理API服务
 */
export const projectApi = {
  /**
   * 获取项目列表
   */
  getProjects: async (
    customerId?: string,
    page: number = 1,
    pageSize: number = 20
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<Project>>>> => {
    const params: Record<string, any> = { page, pageSize };
    if (customerId) {
      params.customerId = customerId;
    }
    return httpClient.get<ApiResponse<PagedResponse<Project>>>('/projects', { params });
  },

  /**
   * 获取单个项目详情
   */
  getProject: async (id: string): Promise<AxiosResponse<ApiResponse<Project>>> => {
    return httpClient.get<ApiResponse<Project>>(`/projects/${id}`);
  },

  /**
   * 创建项目
   */
  createProject: async (data: CreateProjectRequest): Promise<AxiosResponse<ApiResponse<Project>>> => {
    return httpClient.post<ApiResponse<Project>>('/projects', data);
  },

  /**
   * 更新项目
   */
  updateProject: async (
    id: string,
    data: UpdateProjectRequest
  ): Promise<AxiosResponse<ApiResponse<Project>>> => {
    return httpClient.put<ApiResponse<Project>>(`/projects/${id}`, data);
  },

  /**
   * 删除项目
   */
  deleteProject: async (id: string): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.delete<ApiResponse>(`/projects/${id}`);
  },

  /**
   * 获取项目的工作纪要列表
   */
  getWorkSummaries: async (projectId: string): Promise<AxiosResponse<ApiResponse<WorkSummary[]>>> => {
    return httpClient.get<ApiResponse<WorkSummary[]>>(`/projects/${projectId}/work-summaries`);
  },

  /**
   * 创建工作纪要
   */
  createWorkSummary: async (
    projectId: string,
    data: Omit<CreateWorkSummaryRequest, 'projectId'>
  ): Promise<AxiosResponse<ApiResponse<WorkSummary>>> => {
    return httpClient.post<ApiResponse<WorkSummary>>(`/projects/${projectId}/work-summaries`, data);
  },

  /**
   * 删除工作纪要
   */
  deleteWorkSummary: async (id: string): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.delete<ApiResponse>(`/projects/work-summaries/${id}`);
  }
};

export default projectApi;
