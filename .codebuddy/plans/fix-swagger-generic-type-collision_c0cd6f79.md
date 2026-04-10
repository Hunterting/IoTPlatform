---
name: fix-swagger-generic-type-collision
overview: 修复 Swagger 中泛型类型的 schemaId 冲突问题，扩展 CustomSchemaIds 函数以正确处理 ApiResponse<T> 和 PagedResponse<T> 等嵌套泛型类型
todos:
  - id: fix-swagger-schemaid
    content: 修改 Program.cs 中的 CustomSchemaIds 配置，使用完整类型信息生成唯一 schemaId
    status: completed
  - id: verify-build
    content: 编译验证修改是否正确
    status: completed
    dependencies:
      - fix-swagger-schemaid
---

## 用户需求

修复 Swagger 文档生成时的泛型类型 schemaId 冲突问题。

错误信息：

- `ApiResponse<AlertDto>` 和 `ApiResponse<PagedResponse<AlertDto>>` 生成了相同的 schemaId
- 导致 Swagger UI 无法正常加载 API 文档

## 问题根源

当前 `CustomSchemaIds` 配置只处理了简单的类型名称冲突（如 `Controllers.CustomerDto` vs `DTOs.Responses.CustomerDto`），但没有处理泛型类型的 schemaId 冲突。

Swashbuckle 默认使用简化的 schemaId（`ApiResponse$1`），导致不同的泛型实例类型产生相同的 schemaId。

## 技术方案

修改 `Program.cs` 中的 `CustomSchemaIds` 配置，为泛型类型生成唯一标识符。

### 修改文件

- `g:\IoTPlatform\Program.cs` (第 168-180 行)

### 实现逻辑

```
c.CustomSchemaIds(modelType =>
{
    // 1. 如果是泛型类型，使用完整泛型签名
    if (modelType.IsGenericType)
    {
        var genericName = modelType.Name.Split('`')[0];
        var typeArgs = modelType.GetGenericArguments();
        var args = string.Join("_", typeArgs.Select(t => GetUniqueSchemaId(t)));
        return $"{genericName}_{args}";
    }
    
    // 2. 对于 Controllers 中的类型，添加前缀
    if (modelType.FullName?.StartsWith("IoTPlatform.Controllers.") == true)
    {
        return "Controller." + modelType.Name;
    }
    
    // 3. 默认使用完整命名空间 + 类型名
    return modelType.FullName ?? modelType.Name;
});
```

### 预期效果

- `ApiResponse<AlertDto>` → `ApiResponse_AlertDto`
- `ApiResponse<PagedResponse<AlertDto>>` → `ApiResponse_PagedResponse_AlertDto`
- `PagedResponse<AlertDto>` → `PagedResponse_AlertDto`