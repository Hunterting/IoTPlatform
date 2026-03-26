# CAD档案文件上传功能需求文档

## 需求概述
在现有档案管理系统中，实现专门的CAD图纸文件上传流程。用户上传CAD文件时，系统自动识别并将文件分类为"图纸资料"，同时支持后续将该CAD文件转换为3D场景。

## 需求场景具体处理逻辑

### 1. 上传入口与文件类型识别
- 用户在档案管理页面点击"上传文件"按钮
- 在文件选择对话框中选择CAD文件（支持.dxf、.dwg格式）
- 系统自动识别文件类型为CAD图纸（blueprint类型）
- 文件分类自动设置为"图纸资料"（不可手动修改为其他分类）

### 2. 文件上传流程
- 拖拽或点击选择CAD文件后，系统显示文件预览信息
- 文件类型标签显示为"图纸"，颜色为青色（cyan-400）
- 文件分类下拉框默认选中"图纸资料"，且禁用修改
- 用户需输入文件名称、选择关联区域（可选）
- 点击"确认上传"后，文件保存到 `public/assets/cad/` 目录
- 在档案列表中显示新上传的CAD文件

### 3. CAD文件元数据管理
- 记录文件基本信息：名称、大小、上传日期、关联区域
- 存储CAD文件路径，用于后续3D场景加载
- 支持3D转换标志位（is3DModel），初始值为false

### 4. 与3D场景的关联
- 上传的CAD文件在档案列表中显示"图纸"类型图标
- 用户点击"3D查看"按钮时，使用 `CADParserService` 加载该CAD文件
- 使用 `SceneBuilderService` 构建3D场景
- 支持在CAD图纸上标注设备位置，并生成3D设备标记

## 架构技术方案

### 1. 文件上传架构
```
用户交互层 (ArchivesPage UploadFileModal)
    ↓
文件处理层 (检测文件类型)
    ↓
数据存储层 (保存到 public/assets/cad/)
    ↓
数据模型层 (Archive数据结构)
```

### 2. CAD文件处理流程
```
上传CAD文件 → 检测扩展名 → 自动分类为图纸资料 → 保存文件路径 → 更新档案列表
```

### 3. 3D场景转换流程
```
点击3D查看 → 加载CAD文件 → CADParserService解析 → SceneBuilderService构建场景 → 渲染3D视图
```

## 影响文件

### 修改的文件
1. **src/app/pages/ArchivesPage.tsx**
   - 修改类型：逻辑增强
   - 影响函数：`detectType` 函数增强CAD文件识别
   - 影响函数：`UploadFileModal` 组件的文件类型检测和分类逻辑
   - 新增逻辑：当检测到CAD文件时，自动设置分类为"图纸资料"并禁用分类选择

2. **src/app/data/archivesData.ts**
   - 修改类型：数据模型使用
   - 无代码修改，确保 `Archive` 接口支持存储CAD文件路径和3D场景配置

### 不修改的文件
- `src/app/services/cad/CADParserService.ts` - 已支持CAD文件解析
- `src/app/services/scene/SceneBuilderService.ts` - 已支持3D场景构建
- `src/app/types/3d-scene.ts` - 类型定义已完善

## 实现细节

### 1. CAD文件类型检测增强
```typescript
// 在 UploadFileModal 组件中的 detectType 函数
const detectType = (file: File): Archive['type'] => {
  const ext = file.name.split('.').pop()?.toLowerCase() || '';
  if (['dwg', 'dxf', 'svg', 'cad'].includes(ext)) {
    return 'blueprint'; // CAD图纸类型
  }
  if (['pdf'].includes(ext)) return 'pdf';
  if (['doc', 'docx', 'txt', 'xls', 'xlsx', 'csv'].includes(ext)) return 'document';
  if (['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp'].includes(ext)) return 'image';
  return 'document';
};
```

### 2. 自动分类为图纸资料
```typescript
// 在 UploadFileModal 组件中
useEffect(() => {
  if (selectedFile) {
    const fileType = detectType(selectedFile);
    if (fileType === 'blueprint') {
      setCategory('图纸资料'); // 自动设置为图纸资料
    } else {
      setCategory(''); // 其他类型保持为空，用户手动选择
    }
  }
}, [selectedFile]);
```

### 3. 分类选择框禁用逻辑
```typescript
// 在 UploadFileModal 组件的表单中
const isCADFile = selectedFile && detectType(selectedFile) === 'blueprint';

<select
  required
  value={category}
  onChange={e => setCategory(e.target.value)}
  disabled={isCADFile} // CAD文件时禁用选择
  className={`w-full px-4 py-3 border rounded-lg transition-colors ${
    isCADFile 
      ? 'bg-white/10 text-gray-500 cursor-not-allowed' 
      : 'bg-white/5 border-white/10 text-white focus:border-blue-500'
  }`}
>
  <option value="">请选择分类</option>
  {categories.map(cat => (
    <option key={cat} value={cat}>{cat}</option>
  ))}
</select>
```

### 4. 文件保存时包含CAD文件路径
```typescript
const handleSubmit = async (e: React.FormEvent) => {
  e.preventDefault();
  if (!selectedFile) return;
  setUploading(true);
  
  // 模拟文件上传到服务器
  const cadFilePath = `/assets/cad/${selectedFile.name}`;
  
  await new Promise(r => setTimeout(r, 1200));
  setUploading(false);
  setUploadDone(true);
  await new Promise(r => setTimeout(r, 600));

  const newArchive: Archive = {
    id: Date.now().toString(),
    name: fileName || selectedFile.name,
    appCode,
    type: detectType(selectedFile),
    size: formatSize(selectedFile.size),
    date: new Date().toISOString().split('T')[0],
    category: category || '其他',
    areaId,
    areaName,
    // CAD文件特殊字段
    ...(detectType(selectedFile) === 'blueprint' && {
      is3DModel: false, // 初始不支持3D
      sceneConfig: undefined // 初始无场景配置
    })
  };
  onSave(newArchive);
  // ... reset logic
};
```

### 5. CAD文件类型标签显示
```typescript
// 类型标签配置
const typeLabel: Record<string, string> = {
  pdf: 'PDF 文档',
  document: '文档',
  image: '图片',
  blueprint: '图纸', // CAD文件显示为"图纸"
};

const typeColor: Record<string, string> = {
  pdf: 'text-red-400 bg-red-500/10 border-red-500/30',
  document: 'text-green-400 bg-green-500/10 border-green-500/30',
  image: 'text-purple-400 bg-purple-500/10 border-purple-500/30',
  blueprint: 'text-cyan-400 bg-cyan-500/10 border-cyan-500/30', // 青色样式
};
```

## 边界条件与异常处理

### 1. 文件类型边界
- **支持的格式**：.dxf、.dwg（实际.dwg暂不支持，会提示转换）
- **不支持的格式**：上传非CAD文件时，用户仍可手动选择分类为"图纸资料"
- **自动分类限制**：仅当文件扩展名匹配CAD格式时，才自动锁定分类

### 2. 文件大小限制
- 当前实现未限制文件大小，实际部署时应添加大小验证
- 建议限制：单个CAD文件不超过50MB

### 3. 文件名重复处理
- 如果上传同名文件，应提示用户覆盖或重命名
- 当前实现简单覆盖，生产环境需优化

### 4. 区域选择异常
- 如果用户未选择区域，`areaId` 和 `areaName` 为空字符串
- 不影响文件上传，但后续无法关联区域视图

### 5. 字典管理异常
- 如果"图纸资料"分类不存在于字典中，会导致自动分类失败
- 需确保字典管理中包含"图纸资料"分类

## 数据流动路径

### 1. 上传流程数据流
```
用户选择CAD文件 
  → selectedFile 状态更新
  → detectType() 识别为 'blueprint'
  → category 自动设置为 '图纸资料'
  → 分类选择框禁用
  → 用户点击上传
  → 模拟上传延迟
  → 创建 Archive 对象
  → onSave(newArchive) 回调
  → ArchivesPage 更新 archives 状态
  → 文件列表显示新CAD文件
```

### 2. 3D查看流程数据流
```
用户点击"3D查看"
  → handleView3D(archive) 
  → selected3DFile 状态更新
  → setShow3DViewer(true)
  → 3D Viewer Modal 渲染
  → 使用 archive.sceneConfig 渲染场景
  → 点击设备标记显示详情
  → 可跳转到设备管理页面
```

### 3. 状态管理
- `selectedFile`: 当前选择的文件对象
- `fileName`: 文件显示名称
- `category`: 文件分类（CAD文件自动锁定）
- `areaId`/`areaName`: 关联区域信息
- `uploading`: 上传状态
- `uploadDone`: 上传完成状态

## 预期成果

### 1. 用户界面改进
- 上传CAD文件时，文件类型显示为"图纸"（青色标签）
- 分类自动设置为"图纸资料"，且下拉框禁用
- 上传完成后，档案列表显示新的CAD文件
- CAD文件卡片显示"图纸"图标和"图纸资料"分类

### 2. 功能完善
- 支持CAD文件（.dxf、.dwg）上传和识别
- 自动分类为"图纸资料"，减少用户操作
- 保存CAD文件路径，为后续3D转换做准备
- 与现有3D查看功能无缝集成

### 3. 数据完整性
- Archive对象包含完整的CAD文件元数据
- 支持后续将CAD文件转换为3D场景的扩展点
- 与区域管理、设备管理模块的数据关联

### 4. 兼容性
- 不影响现有其他类型文件的上传流程
- 保持与非CAD文件上传行为的一致性
- 向后兼容现有的档案数据和3D场景配置