---
name: 前端设备列表和添加设备表单改造
overview: 将前端设备列表改为真实API，添加设备页面的所属项目获取租户的项目列表，所属区域获取租户的区域管理树状选择
todos:
  - id: modify-device-form-projects
    content: 修改DeviceFormModal组件，添加项目列表API调用，从projectApi获取租户项目列表替换currentCustomer.projects
    status: completed
  - id: verify-area-tree-data
    content: 验证区域树数据来源，确保AreaTreeSelect组件使用从API获取的区域数据
    status: completed
    dependencies:
      - modify-device-form-projects
  - id: test-device-form-flow
    content: 测试添加设备表单完整流程，包括项目选择和区域树选择功能
    status: completed
    dependencies:
      - verify-area-tree-data
---

## 用户需求

1. **设备列表使用真实API**：设备列表页面调用后端真实API获取设备数据
2. **添加设备表单-所属项目**：从API获取当前租户的项目列表供选择
3. **添加设备表单-所属区域**：使用区域管理树状选择组件，从API获取租户的区域树状数据

## 现状分析

### 设备列表

- DeviceContext.tsx 已实现调用 `deviceApi.getDevices()` 真实API
- DevicesPage.tsx 使用 DeviceContext 提供的数据
- **状态**：已实现真实API，无需修改

### 添加设备表单-项目选择

- 当前代码从 `currentCustomer?.projects` 获取项目列表
- 项目数据可能为空或未从API加载
- 需要改为直接调用 `projectApi.getProjects(customerId)` 获取数据

### 添加设备表单-区域选择

- 当前使用 `AreaTreeSelect` 组件，传入 `customerAreas`
- `customerAreas` 来自 `getAreasByCustomerId(currentCustomer.id)`
- 需要确保区域树从API获取

## 核心功能

1. **设备列表**：显示设备列表，支持搜索、过滤、分页
2. **添加设备**：表单包含设备基本信息、所属项目下拉选择、区域树状选择
3. **项目列表获取**：根据当前客户的customerId从API获取项目列表
4. **区域树获取**：从AreaContext获取区域树数据

## 技术方案

### 技术栈

- 前端框架：React + TypeScript + Vite
- UI组件：Tailwind CSS + shadcn/ui组件
- 状态管理：React Context API
- HTTP客户端：Axios (httpClient)

### 实现方案

#### 1. 设备列表（已实现）

- DeviceContext.tsx 调用 `deviceApi.getDevices()`
- 无需修改

#### 2. 项目列表获取（需修改）

在 DeviceFormModal 组件内：

- 添加 `useState` 保存项目列表
- 添加 `useEffect` 监听 `currentCustomer?.id` 变化
- 调用 `projectApi.getProjects(currentCustomer?.id)` 获取项目列表
- 项目列表用于 "所属项目" 下拉选择

#### 3. 区域树获取（需确认）

- AreaContext.tsx 已实现调用 `areaApi.getAreaTree()`
- DeviceFormModal 使用 `getAreasByCustomerId(currentCustomer.id)` 获取区域数据
- 确保数据从API加载后再渲染表单

### 关键文件

| 文件 | 操作 | 说明 |
| --- | --- | --- |
| `Web/src/app/pages/DevicesPage.tsx` | 修改 | DeviceFormModal组件内添加项目API调用 |
| `Web/src/app/services/api/projectApi.ts` | 无 | 项目API已存在 |
| `Web/src/app/contexts/AreaContext.tsx` | 无 | 区域Context已存在 |


### 实施细节

#### DeviceFormModal 修改点

```
// 添加状态
const [projects, setProjects] = useState<Project[]>([]);
const [loadingProjects, setLoadingProjects] = useState(false);

// 添加API调用
useEffect(() => {
  if (currentCustomer?.id) {
    setLoadingProjects(true);
    projectApi.getProjects(currentCustomer.id, 1, 100)
      .then(res => {
        if (res.data.code === 200) {
          setProjects(res.data.data.items);
        }
      })
      .finally(() => setLoadingProjects(false));
  }
}, [currentCustomer?.id]);

// 项目下拉使用projects状态而非customerProjects
```

#### 错误处理

- 添加加载状态提示
- 添加API错误捕获
- 项目加载失败时显示友好提示

#### 性能优化

- 仅在打开添加设备弹窗时加载项目列表
- 使用useCallback优化回调函数
- 避免不必要的重渲染