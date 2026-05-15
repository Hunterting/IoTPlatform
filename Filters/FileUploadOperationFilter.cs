using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IoTPlatform.Filters;

/// <summary>
/// Swagger 文件上传操作过滤器
/// 解决 Swashbuckle 6.x 对含 IFormFile 属性的 [FromForm] DTO 无法正确生成 Schema 的问题。
/// </summary>
public class FileUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // 找出所有带 [FromForm] 且参数类型含有 IFormFile 属性的参数
        var formFileParams = context.MethodInfo.GetParameters()
            .Where(p => p.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.FromFormAttribute), true).Any()
                        && HasFormFileProperty(p.ParameterType))
            .ToList();

        // 同时检测直接使用 IFormFile 的参数（兼容旧写法）
        var directFileParams = context.MethodInfo.GetParameters()
            .Where(p =>
                p.ParameterType == typeof(IFormFile) ||
                p.ParameterType == typeof(IEnumerable<IFormFile>) ||
                p.ParameterType == typeof(List<IFormFile>) ||
                p.ParameterType == typeof(IFormFile[]))
            .ToList();

        if (!formFileParams.Any() && !directFileParams.Any())
            return;

        // 构建完整的 multipart/form-data schema
        var properties = new Dictionary<string, OpenApiSchema>();
        var required = new HashSet<string>();

        // 处理 DTO 类中含有 IFormFile 的情况
        foreach (var param in formFileParams)
        {
            foreach (var prop in param.ParameterType.GetProperties())
            {
                var propType = prop.PropertyType;
                var propName = ToCamelCase(prop.Name);

                if (propType == typeof(IFormFile))
                {
                    properties[propName] = new OpenApiSchema { Type = "string", Format = "binary" };
                }
                else if (propType == typeof(IEnumerable<IFormFile>) ||
                         propType == typeof(List<IFormFile>) ||
                         propType == typeof(IFormFile[]))
                {
                    properties[propName] = new OpenApiSchema
                    {
                        Type = "array",
                        Items = new OpenApiSchema { Type = "string", Format = "binary" }
                    };
                }
                else if (propType == typeof(string))
                {
                    properties[propName] = new OpenApiSchema { Type = "string" };
                }
                else if (propType == typeof(long) || propType == typeof(int))
                {
                    properties[propName] = new OpenApiSchema { Type = "integer", Format = "int64" };
                    required.Add(propName);
                }
                else if (propType == typeof(long?) || propType == typeof(int?))
                {
                    properties[propName] = new OpenApiSchema { Type = "integer", Format = "int64", Nullable = true };
                }
                else if (propType == typeof(bool))
                {
                    properties[propName] = new OpenApiSchema { Type = "boolean" };
                }
                else
                {
                    properties[propName] = new OpenApiSchema { Type = "string" };
                }

                // 非 nullable、非 string、非 IFormFile? 的属性视为必填
                bool isNullable = IsNullableType(propType) || propType == typeof(string);
                if (!isNullable && !required.Contains(propName))
                {
                    required.Add(propName);
                }
            }
        }

        // 处理直接 IFormFile 参数
        foreach (var param in directFileParams)
        {
            var paramName = param.Name!;
            bool isList = param.ParameterType != typeof(IFormFile);

            properties[paramName] = isList
                ? new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "string", Format = "binary" } }
                : new OpenApiSchema { Type = "string", Format = "binary" };

            if (!param.HasDefaultValue)
                required.Add(paramName);
        }

        // 完全替换 RequestBody 为正确的 multipart/form-data 定义
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content =
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = properties,
                        Required = required
                    }
                }
            }
        };

        // 移除因 IFormFile 直接参数导致的错误 parameter 条目
        var paramNamesToRemove = directFileParams.Select(p => p.Name).ToHashSet();
        var toRemove = operation.Parameters.Where(p => paramNamesToRemove.Contains(p.Name)).ToList();
        foreach (var p in toRemove)
            operation.Parameters.Remove(p);
    }

    private static bool HasFormFileProperty(Type type)
    {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return false;
        return type.GetProperties().Any(p =>
            p.PropertyType == typeof(IFormFile) ||
            p.PropertyType == typeof(IEnumerable<IFormFile>) ||
            p.PropertyType == typeof(List<IFormFile>) ||
            p.PropertyType == typeof(IFormFile[]));
    }

    private static bool IsNullableType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
