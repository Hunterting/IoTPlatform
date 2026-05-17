---
name: device-control-page-enhancement
overview: 为设备控制页面增加添加、编辑、删除设备功能，完善设备管理能力
design:
  architecture:
    framework: react
  styleKeywords:
    - 深色主题
    - 卡片式布局
    - 模态框
    - 动画过渡
  fontSystem:
    fontFamily: Inter, system-ui, sans-serif
    heading:
      size: 18px
      weight: 600
    subheading:
      size: 14px
      weight: 500
    body:
      size: 14px
      weight: 400
  colorSystem:
    primary:
      - "#3B82F6"
      - "#2563EB"
    background:
      - "#0F172A"
      - "#1E293B"
    text:
      - "#F8FAFC"
      - "#94A3B8"
    functional:
      - "#22C55E"
      - "#EF4444"
      - "#F59E0B"
todos:
  - id: add-device-button-header
    content: 在页头添加"添加设备"按钮
    status: pending
  - id: create-device-form-modal
    content: 创建设备表单弹窗组件，支持创建/编辑模式
    status: pending
    dependencies:
      - add-device-button-header
  - id: add-edit-delete-buttons-device-card
    content: 在设备卡片添加编辑和删除操作按钮
    status: pending
    dependencies:
      - add-device-button-header
  - id: add-delete-confirm-dialog
    content: 实现删除设备确认对话框
    status: pending
    dependencies:
      - add-edit-delete-buttons-device-card
  - id: integrate-permissions
    content: 集成权限控制，按钮根据权限显示/隐藏
    status: pending
    dependencies:
      - add-edit-delete-buttons-device-card
---

## 产品概述

设备控制页面是IoT平台的核心功能页面，用于展示设备列表、发送控制指令、查看指令历史。

## 核心功能

在现有设备控制页面增加以下功能：

1. **添加设备功能**

- 页头添加"添加设备"按钮
- 创建设备表单弹窗，包含字段：名称、序列号、型号、协议类型、位置、描述
- 表单验证和数据提交

2. **编辑设备功能**

- 每个设备卡片增加"编辑"按钮
- 编辑设备表单弹窗，预填充现有数据
- 更新设备信息

3. **删除设备功能**

- 每个设备卡片增加"删除"按钮
- 删除确认对话框，防止误操作
- 删除成功后刷新设备列表

4. **权限控制**

- 根据用户权限显示/隐藏添加、编辑、删除按钮

## 技术栈

- 前端框架：React + TypeScript
- 样式：Tailwind CSS
- UI组件：基于现有shadcn/ui风格
- API调用：deviceApi.ts (已有createDevice, updateDevice, deleteDevice)

## 实现方案

### 1. 设备表单组件

创建设备表单弹窗组件，支持创建和编辑两种模式：

- 通过 `mode` 属性区分 'create' | 'edit'
- 表单字段：名称(必填)、序列号、型号、协议类型、位置、描述
- 表单验证和提交处理

### 2. 删除确认对话框

使用确认对话框组件，包含：

- 设备名称提示
- 取消和确认按钮
- 确认后调用删除API并刷新列表

### 3. 权限集成

使用现有 `useAuth` hook 和 `PERMISSIONS` 配置：

- `MANAGE_DEVICES` - 添加/编辑/删除权限
- 根据权限动态显示操作按钮

## 修改文件

```
Web/src/app/pages/DeviceControlPage.tsx  [MODIFY]
```

## 实现要点

- 表单组件复用：创建设备和编辑设备使用同一个表单组件
- 状态管理：设备列表在组件内部已使用useState管理
- 错误处理：API调用失败时显示错误提示
- 加载状态：提交时显示loading状态

## 设计风格

沿用现有设备控制页面的深色主题风格，保持UI一致性。

### 新增UI元素

1. **添加设备按钮**：页头右侧，蓝底白字图标按钮
2. **设备卡片操作区**：每张卡片底部增加编辑/删除图标按钮
3. **设备表单弹窗**：居中模态框，包含表单字段和提交按钮
4. **删除确认弹窗**：居中警告框，显示设备名称和确认按钮

### 交互设计

- 添加/编辑按钮点击弹出表单弹窗
- 删除按钮点击弹出确认对话框
- 表单提交后自动关闭弹窗并刷新列表
- 操作按钮hover时显示tooltip