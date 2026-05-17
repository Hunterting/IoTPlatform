// 适配器统一导出
// 这些适配器用于转换前后端数据结构

// 通用的适配器函数

/**
 * 将后端ID（long）转换为前端ID（string）
 */
export const adaptIdFromBackend = (id: number | string | null | undefined): string => {
  if (id === null || id === undefined) return '';
  return id.toString();
};

/**
 * 将前端ID（string）转换为后端ID（long）
 */
export const adaptIdToBackend = (id: string | number | null | undefined): number | null => {
  if (id === null || id === undefined || id === '') return null;
  
  if (typeof id === 'number') {
    return id;
  }
  
  const num = Number(id);
  return isNaN(num) ? null : num;
};

/**
 * 转换日期时间格式
 */
export const adaptDateTime = (dateTime: string | Date | null | undefined): string => {
  if (!dateTime) return '';
  
  if (dateTime instanceof Date) {
    return dateTime.toISOString();
  }
  
  return dateTime;
};

/**
 * 转换前端日期到后端格式
 */
export const adaptDateToBackend = (date: Date | string | null | undefined): string => {
  if (!date) return '';
  
  if (date instanceof Date) {
    return date.toISOString();
  }
  
  return date;
};

/**
 * 转换后端日期到前端格式
 */
export const adaptDateFromBackend = (date: string | null | undefined): string => {
  if (!date) return '';
  
  try {
    const dateObj = new Date(date);
    return dateObj.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return date;
  }
};

/**
 * 处理空值转换
 */
export const adaptNullable = <T>(value: T | null | undefined, defaultValue: T): T => {
  return value === null || value === undefined ? defaultValue : value;
};

/**
 * 处理枚举值转换
 */
export const adaptEnum = <T extends string>(
  value: T | string | null | undefined,
  enumValues: readonly T[],
  defaultValue: T
): T => {
  if (!value) return defaultValue;
  
  const stringValue = value.toString();
  return enumValues.includes(stringValue as T) ? (stringValue as T) : defaultValue;
};

// 导入各个模块的适配器
export * from './authAdapter';
export * from './customerAdapter';
export * from './deviceAdapter';
export * from './areaAdapter';
export * from './userAdapter';
export * from './roleAdapter';
export * from './logAdapter';
export * from './archiveAdapter';