/**
 * 字典类型 DTO
 */
export interface DictionaryTypeDto {
  id: number;
  code: string;
  name: string;
  description?: string;
  sortOrder: number;
  isActive: boolean;
  appCode?: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * 字典项 DTO
 */
export interface DictionaryItemDto {
  id: number;
  type: string;
  code: string;
  name: string;
  sort: number;
  description?: string;
  status: string;
  appCode?: string;
  createdAt: string;
  updatedAt: string;
}
