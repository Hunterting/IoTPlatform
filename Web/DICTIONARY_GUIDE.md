# 字典管理系统扩展指南

## 概述

字典管理系统采用配置化设计，可以轻松扩展新的字典类型。所有字典类型的配置都集中在 `/src/app/contexts/DictionaryContext.tsx` 文件中。

## 如何添加新的字典类型

### 步骤 1: 在 DictionaryType 枚举中添加新类型

在 `DictionaryContext.tsx` 文件中，找到 `DictionaryType` 对象，添加新的字典类型：

```typescript
export const DictionaryType = {
  DEVICE_CATEGORY: 'device_category',
  ARCHIVE_CATEGORY: 'archive_category',
  DEVICE_STATUS: 'device_status',
  // ... 其他已有类型
  
  // 添加新的字典类型
  YOUR_NEW_TYPE: 'your_new_type', // 新类型的唯一标识
} as const;
```

### 步骤 2: 在 DICTIONARY_TYPE_CONFIGS 中添加配置

在同一文件中，找到 `DICTIONARY_TYPE_CONFIGS` 数组，添加新类型的配置：

```typescript
export const DICTIONARY_TYPE_CONFIGS: DictionaryTypeConfig[] = [
  // ... 其他已有配置
  
  {
    key: DictionaryType.YOUR_NEW_TYPE,      // 必须与步骤1中的key一致
    name: '您的字典名称',                    // 显示名称
    icon: 'IconName',                        // Lucide图标名称（见下方图标列表）
    description: '这个字典的用途说明...',   // 描述信息
    color: 'blue',                           // 主题色（见下方颜色列表）
  },
];
```

### 步骤 3: （可选）添加初始数据

如果需要为新字典类型添加初始数据，在 `initialDictionaries` 数组中添加：

```typescript
const initialDictionaries: DictionaryItem[] = [
  // ... 其他初始数据
  
  {
    id: 'dict-your-type-001',
    type: DictionaryType.YOUR_NEW_TYPE,
    code: 'example_code',
    name: '示例项',
    sort: 1,
    description: '这是一个示例',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
];
```

### 步骤 4: （可选）在 DictionaryManagementPage.tsx 中添加图标

如果您使用了新的图标，需要在 `DictionaryManagementPage.tsx` 的 `iconMap` 中添加导入：

```typescript
import { 
  // ... 其他图标
  YourNewIcon,  // 从 lucide-react 导入新图标
} from 'lucide-react';

const iconMap: Record<string, LucideIcon> = {
  // ... 其他图标
  YourNewIcon,  // 添加到映射中
};
```

## 可用的图标选项

当前已支持的图标（可以在 [Lucide Icons](https://lucide.dev/icons/) 查看更多）：

- `Cpu` - 处理器图标
- `Archive` - 归档图标
- `Activity` - 活动图标
- `Tag` - 标签图标
- `FileText` - 文件文本图标
- `AlertTriangle` - 警告三角图标
- `Bell` - 铃铛图标
- `Radio` - 无线电图标

## 可用的颜色选项

- `blue` - 蓝色
- `purple` - 紫色
- `green` - 绿色
- `orange` - 橙色
- `cyan` - 青色
- `red` - 红色
- `yellow` - 黄色
- `indigo` - 靛蓝色

## 完整示例：添加"部门类型"字典

```typescript
// 1. 添加枚举
export const DictionaryType = {
  // ... 其他类型
  DEPARTMENT_TYPE: 'department_type',
} as const;

// 2. 添加配置
export const DICTIONARY_TYPE_CONFIGS: DictionaryTypeConfig[] = [
  // ... 其他配置
  {
    key: DictionaryType.DEPARTMENT_TYPE,
    name: '部门类型',
    icon: 'Users',  // 需要在 iconMap 中添加
    description: '管理组织的部门分类',
    color: 'cyan',
  },
];

// 3. 添加初始数据（可选）
const initialDictionaries: DictionaryItem[] = [
  // ... 其他数据
  {
    id: 'dict-dept-001',
    type: DictionaryType.DEPARTMENT_TYPE,
    code: 'tech',
    name: '技术部',
    sort: 1,
    description: '负责技术研发',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
];
```

## 使用字典数据

在其他组件中使用字典数据：

```typescript
import { useDictionary, DictionaryType } from '@/app/contexts/DictionaryContext';

function YourComponent() {
  const { getItemsByType } = useDictionary();
  
  // 获取某个类型的所有启用项
  const departments = getItemsByType(DictionaryType.DEPARTMENT_TYPE);
  
  return (
    <select>
      {departments.map(item => (
        <option key={item.id} value={item.code}>
          {item.name}
        </option>
      ))}
    </select>
  );
}
```

## 最佳实践

1. **命名规范**: 字典类型的 key 使用大写下划线格式（如 `DEVICE_CATEGORY`），value 使用小写下划线格式（如 `device_category`）
2. **唯一编码**: 每个字典项的 code 应该在同一类型内保持唯一
3. **排序**: 使用 sort 字段控制字典项的显示顺序
4. **状态管理**: 使用 status 字段来启用/禁用字典项，而不是直接删除
5. **描述信息**: 为每个字典项添加清晰的描述，方便后期维护

## 注意事项

- 添加新字典类型后，系统会自动在字典管理页面显示新的 Tab
- 所有字典数据都通过 DictionaryContext 统一管理
- 字典数据变更会实时反映到所有使用该字典的组件中
- 建议在添加新字典类型前，先规划好其用途和数据结构
