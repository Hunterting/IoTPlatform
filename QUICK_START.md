# IoT Platform 快速开始指南

## 前置要求

- .NET 8 SDK
- MySQL 5.7 或更高版本
- Redis（可选，用于缓存）
- MQTT Broker（可选，如Mosquitto）

## 安装步骤

### 1. 克隆项目

```bash
git clone <repository-url>
cd IoTPlatform
```

### 2. 还原NuGet包

```bash
dotnet restore
```

### 3. 配置数据库

编辑 `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=iot_platform;User=root;Password=your_password;charset=utf8mb4;"
  }
}
```

### 4. 创建数据库

```sql
CREATE DATABASE iot_platform CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

### 5. 运行数据库迁移

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### 6. 配置Redis（可选）

编辑 `appsettings.json`:

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379,defaultDatabase=0",
    "Enabled": true
  }
}
```

### 7. 运行项目

```bash
dotnet run
```

项目将在 `http://localhost:5000` 启动。

## 访问API文档

打开浏览器访问: `http://localhost:5000/swagger`

## 测试认证API

### 使用curl测试登录

```bash
curl -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@system.com",
    "password": "your_password"
  }'
```

### 使用Postman测试

1. 导入 `IoTPlatform.http` 文件
2. 或者手动配置请求：

**登录请求**
- Method: POST
- URL: `http://localhost:5000/api/v1/auth/login`
- Headers: `Content-Type: application/json`
- Body:
```json
{
  "email": "admin@system.com",
  "password": "your_password"
}
```

**获取当前用户信息**
- Method: GET
- URL: `http://localhost:5000/api/v1/auth/me`
- Headers: `Authorization: Bearer {token_from_login}`

## 初始化超级管理员

在首次运行后，需要创建超级管理员用户。可以通过以下方式：

### 方式1: 使用MySQL直接插入

```sql
INSERT INTO users (Name, Email, PasswordHash, Role, IsActive, CreatedAt, UpdatedAt)
VALUES ('超级管理员', 'admin@system.com', '$2a$11$你的BCrypt哈希密码', 'super_admin', 1, NOW(), NOW());
```

### 方式2: 创建种子数据脚本

在 `Data/SeedData/SeedUsers.cs` 中实现自动初始化逻辑。

## API端点列表

### 认证
- `POST /api/v1/auth/login` - 用户登录
- `GET /api/v1/auth/me` - 获取当前用户信息
- `POST /api/v1/auth/switch-customer` - 切换客户（仅超级管理员）

### 客户管理
- `GET /api/v1/customers` - 获取客户列表
- `GET /api/v1/customers/{id}` - 获取客户详情
- `POST /api/v1/customers` - 创建客户
- `PUT /api/v1/customers/{id}` - 更新客户
- `DELETE /api/v1/customers/{id}` - 删除客户

### 其他API端点

详细的API文档请参考 `README_API_IMPLEMENTATION.md`。

## 开发指南

### 创建新的控制器

```csharp
[ApiController]
[Route("api/v1/{resource}")]
[PermissionAuthorize(Permissions.VIEW_RESOURCE)]
public class ResourceController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ResourceController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ResourceDto>>>> GetList()
    {
        var items = await _dbContext.Resources.ToListAsync();
        var dtos = items.Select(item => new ResourceDto { ... }).ToList();
        return ApiResponse<List<ResourceDto>>.Success(dtos);
    }
}
```

### 使用权限验证

```csharp
[HttpPost]
[PermissionAuthorize(Permissions.CREATE_RESOURCE)]
public async Task<ActionResult<ApiResponse<ResourceDto>>> Create([FromBody] CreateRequest request)
{
    // 只有拥有 CREATE_RESOURCE 权限的用户才能访问
}
```

### 租户数据隔离

```csharp
[HttpGet]
public async Task<ActionResult<ApiResponse<List<ResourceDto>>>> GetList()
{
    var appCode = User.FindFirst("AppCode")?.Value;
    var role = User.FindFirst(ClaimTypes.Role)?.Value;

    var query = _dbContext.Resources.AsQueryable();

    // 超级管理员可以查看所有数据
    if (role != Roles.SUPER_ADMIN)
    {
        // 其他角色只能查看所属租户的数据
        query = query.Where(r => r.AppCode == appCode);
    }

    var items = await query.ToListAsync();
    // ...
}
```

## 常见问题

### Q: 数据库连接失败？

A: 检查 `appsettings.json` 中的连接字符串是否正确，确保MySQL服务正在运行。

### Q: Redis连接失败？

A: 将 `Redis:Enabled` 设置为 `false`，系统将正常运行但不使用缓存。

### Q: Swagger无法访问？

A: 确保项目运行在开发环境 (`ASPNETCORE_ENVIRONMENT=Development`)。

### Q: 权限验证失败？

A: 检查用户角色和权限配置，确保已正确初始化角色和权限。

### Q: 如何生成BCrypt密码哈希？

A: 使用以下C#代码：
```csharp
string password = "your_password";
string hash = BCrypt.Net.BCrypt.HashPassword(password);
Console.WriteLine(hash); // 保存到数据库
```

### Q: 如何验证密码？

A: 使用以下C#代码：
```csharp
bool isValid = BCrypt.Net.BCrypt.Verify(inputPassword, storedHash);
```

## 部署

### 发布项目

```bash
dotnet publish -c Release -o ./publish
```

### 使用Docker

创建 `Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["IoTPlatform.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "IoTPlatform.dll"]
```

构建和运行：
```bash
docker build -t iot-platform .
docker run -p 80:80 -e ConnectionStrings__DefaultConnection="..." iot-platform
```

## 支持与反馈

如有问题，请查看以下资源：
- `README_API_IMPLEMENTATION.md` - 详细实施指南
- `IMPLEMENTATION_SUMMARY.md` - 实施总结
- Swagger文档 - API参考

## 许可证

[您的许可证信息]
