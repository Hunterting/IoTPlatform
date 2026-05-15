import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import {
  ApiResponse,
  PagedResponse,
} from './types';
import {
  SendCommandRequest,
  BatchSendCommandRequest,
  DeviceCommandDto,
  CommandHistoryDto,
  SendCommandResponse,
  BatchSendCommandResponse,
  CommandQueryParams,
} from './types/deviceCommand.types';

/**
 * 设备指令下发 API 服务
 */
export const deviceCommandApi = {
  /**
   * 发送单个设备指令
   */
  sendCommand: async (
    request: SendCommandRequest
  ): Promise<AxiosResponse<ApiResponse<SendCommandResponse>>> => {
    return httpClient.post<ApiResponse<SendCommandResponse>>(
      '/device-commands/send',
      request
    );
  },

  /**
   * 批量发送设备指令
   */
  batchSendCommands: async (
    request: BatchSendCommandRequest
  ): Promise<AxiosResponse<ApiResponse<BatchSendCommandResponse>>> => {
    return httpClient.post<ApiResponse<BatchSendCommandResponse>>(
      '/device-commands/send/batch',
      request
    );
  },

  /**
   * 查询指令状态
   */
  getCommandStatus: async (
    commandId: number
  ): Promise<AxiosResponse<ApiResponse<DeviceCommandDto>>> => {
    return httpClient.get<ApiResponse<DeviceCommandDto>>(
      `/device-commands/${commandId}`
    );
  },

  /**
   * 获取指令列表（分页）
   */
  getCommands: async (
    params: CommandQueryParams = {}
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<DeviceCommandDto>>>> => {
    return httpClient.get<ApiResponse<PagedResponse<DeviceCommandDto>>>(
      '/device-commands',
      { params }
    );
  },

  /**
   * 获取指令执行历史
   */
  getCommandHistory: async (
    commandId: number
  ): Promise<AxiosResponse<ApiResponse<CommandHistoryDto[]>>> => {
    return httpClient.get<ApiResponse<CommandHistoryDto[]>>(
      `/device-commands/${commandId}/history`
    );
  },

  /**
   * 取消指令
   */
  cancelCommand: async (
    commandId: number
  ): Promise<AxiosResponse<ApiResponse<void>>> => {
    return httpClient.post<ApiResponse<void>>(
      `/device-commands/${commandId}/cancel`
    );
  },

  /**
   * 重试指令
   */
  retryCommand: async (
    commandId: number
  ): Promise<AxiosResponse<ApiResponse<SendCommandResponse>>> => {
    return httpClient.post<ApiResponse<SendCommandResponse>>(
      `/device-commands/${commandId}/retry`
    );
  },
};

export default deviceCommandApi;
