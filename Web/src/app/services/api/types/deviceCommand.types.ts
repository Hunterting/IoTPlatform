/**
 * 设备指令相关类型定义
 */

export type CommandStatus =
  | 'Pending'     // 待发送
  | 'Sent'        // 已发送
  | 'Delivered'   // 设备已接收
  | 'Success'     // 执行成功
  | 'Failed'      // 执行失败
  | 'Timeout';    // 超时

export type CommandHistoryType =
  | 'Created'
  | 'Sent'
  | 'StatusUpdated'
  | 'Cancelled'
  | 'Retried';

/** 发送单条指令请求 */
export interface SendCommandRequest {
  deviceId: number;
  commandType: string;   // restart | setParam | switch | firmwareUpgrade | custom
  parameters?: Record<string, unknown>;
  timeoutSeconds?: number;
}

/** 批量发送指令请求 */
export interface BatchSendCommandRequest {
  deviceIds: number[];
  commandType: string;
  parameters?: Record<string, unknown>;
  timeoutSeconds?: number;
}

/** 指令 DTO */
export interface DeviceCommandDto {
  id: number;
  appCode: string;
  deviceId: number;
  deviceName?: string;
  commandType: string;
  parameters: string;    // JSON 字符串
  status: CommandStatus;
  createdAt: string;
  completedAt?: string;
  result?: string;
  errorMessage?: string;
  retryCount: number;
}

/** 指令历史 DTO */
export interface CommandHistoryDto {
  id: number;
  commandId: number;
  type: CommandHistoryType;
  description: string;
  createdAt: string;
}

/** 发送指令响应 */
export interface SendCommandResponse {
  commandId: number;
  status: CommandStatus;
  message: string;
}

/** 批量发送响应 */
export interface BatchSendCommandResponse {
  successCount: number;
  failedCount: number;
  commandIds: number[];
  errors: Array<{ deviceId: number; error: string }>;
}

/** 查询指令列表参数 */
export interface CommandQueryParams {
  deviceId?: number;
  status?: CommandStatus;
  commandType?: string;
  startDate?: string;
  endDate?: string;
  page?: number;
  pageSize?: number;
}
