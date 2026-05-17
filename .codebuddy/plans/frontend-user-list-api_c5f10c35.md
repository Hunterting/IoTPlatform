---
name: frontend-user-list-api
overview: 将前端用户列表从静态数据改为调用真实后端API，包括创建用户API服务、适配器和修改用户管理页面。
todos:
  - id: create-user-types
    content: 创建用户类型定义 user.types.ts，包含 UserDto、CreateUserRequest、UpdateUserRequest 的 TypeScript 接口
    status: completed
  - id: create-user-api
    content: 创建用户 API 服务 userApi.ts，封装 GET/POST/PUT/DELETE 接口调用
    status: completed
    dependencies:
      - create-user-types
  - id: create-user-adapter
    content: 创建用户数据适配器 userAdapter.ts，实现前后端数据格式转换（IsActive ↔ status）
    status: completed
    dependencies:
      - create-user-types
  - id: update-api-index
    content: 更新 services/api/index.ts 和 services/adapters/index.ts 导出新模块
    status: completed
    dependencies:
      - create-user-api
      - create-user-adapter
  - id: modify-user-management-page
    content: 修改 UserManagementPage.tsx，移除硬编码数据，改用真实 API 调用
    status: completed
    dependencies:
      - create-user-api
      - create-user-adapter
---

## 用户需求

将前端用户管理页面 `UserManagementPage.tsx` 的硬编码模拟数据替换为真实的后端 API 调用。

## 产品概述

IoT 平台用户管理模块，包含用户列表展示、用户创建、编辑、删除等功能。

## 核心功能

1. **用户列表** - 从后端 API 获取用户列表并展示，支持搜索、分页
2. **用户创建** - 调用 POST `/api/v1/users` 创建新用户
3. **用户编辑** - 调用 PUT `/api/v1/users/{id}` 更新用户信息
4. **用户删除** - 调用 DELETE `/api/v1/users/{id}` 删除用户
5. **类型转换** - 后端 `UserDto`（IsActive 布尔值）与前端（status 字符串）之间的数据适配

## 技术栈

- 前端：React + TypeScript + Tailwind CSS
- HTTP 客户端：Axios（使用现有的 httpClient）

## 实现方案

### 1. 类型定义 (`user.types.ts`)

定义 TypeScript 类型，与后端 `UserDto`、`CreateUserRequest`、`UpdateUserRequest` 对应。

### 2. API 服务 (`userApi.ts`)

封装用户管理相关的 API 调用：

- `getUsers(page, pageSize, keyword)` - 获取用户列表
- `getUser(id)` - 获取用户详情
- `createUser(data)` - 创建用户
- `updateUser(id, data)` - 更新用户
- `deleteUser(id)` - 删除用户

### 3. 数据适配器 (`userAdapter.ts`)

实现前后端数据转换：

- `adaptUserDtoToUserItem` - 将后端 `UserDto` 转换为前端 `UserItem`（IsActive → status）
- `adaptUserItemToCreateRequest` - 创建请求转换
- `adaptUserItemToUpdateRequest` - 更新请求转换

### 4. 修改 `UserManagementPage.tsx`

- 移除硬编码的模拟数据 `users` state
- 使用 `useEffect` 从 API 加载用户列表
- 修改 `handleAddUser`、`handleEditUser`、`handleDeleteUser` 使用真实 API 调用
- 添加加载状态和错误处理

## 目录结构

```
Web/src/
├── app/
│   ├── services/
│   │   ├── api/
│   │   │   ├── types/
│   │   │   │   └── user.types.ts        # [NEW] 用户相关类型定义
│   │   │   ├── userApi.ts                # [NEW] 用户API服务
│   │   │   └── index.ts                  # [MODIFY] 添加 userApi 导出
│   │   └── adapters/
│   │       ├── userAdapter.ts            # [NEW] 用户数据适配器
│   │       └── index.ts                  # [MODIFY] 添加 userAdapter 导出
│   └── pages/
│       └── UserManagementPage.tsx        # [MODIFY] 改用真实API
```

## 关键设计决策

1. **复用现有模式**：参考 `deviceAdapter.ts` 和 `deviceApi.ts` 的实现模式
2. **类型兼容性**：后端 `IsActive` (bool) 转换为前端 `status` ('active' | 'inactive')
3. **状态管理**：直接在页面组件内管理用户数据状态，暂不创建独立的 UserContext（参考现有项目其他页面的做法）