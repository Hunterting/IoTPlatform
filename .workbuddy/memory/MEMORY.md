# IoTPlatform 长期记忆

## 概况
- IoT SaaS 多租户；.NET 8 / EF Core 8 / MySQL / Redis / MQTTnet / SignalR / Serilog
- 后端 ASP.NET Core Web API 单体分层；前端 `H:/IoTPlatform/Web`（React18+Vite+Tailwind+Radix+MUI+axios，无 tsconfig，靠 esbuild 转译，tsc 历史错不强制）

## 多租户
- AppCode 行级隔离；实体 `IHasAppCode` + EF `HasQueryFilter`；JWT 带 `AppCode/CustomerId/UserId`，超管 `IsSuperAdmin` 跳过
- ⚠缺陷：Global Query Filter 构造时静态冻结 appCode，变更不更新，需改动态 Lambda

## 铁律级约定
- **枚举出网**：`Program.cs` 注册 `JsonStringEnumConverter`，控制器出网枚举用 PascalCase 字符串；前端按字符串分支，勿按整数
- **协议配置前端字段 PascalCase**：`Config` 是 `Dictionary<string,object>`，key 原样透传大小写敏感；必须用 Host/Port/PublishTopicPattern… 精确匹配 C# 属性，否则静默落默认值。value 用 `ansheng_mqtt`（工厂 `.ToUpperInvariant()` 归一）
- **协议派生字段**：`Type`→`ProtocolType`(ToUpper)、`Status`→`IsActive` 必须同步；消费方按 `IsActive && ProtocolType=="ANSHENG_MQTT"` 筛选
- **登录用 Email**：`POST /api/v1/auth/login` body `{email,password}`，超管 `admin@system.com`/`admin123`
- **起后端禁 dotnet run**（launchSettings 强制 Development 触发清库）→ `dotnet bin/Debug/net8.0/IoTPlatform.dll` + `ASPNETCORE_ENVIRONMENT=Staging`
- **改码必 build 回写 bin/Debug** 再重启，勿只编 /tmp（否则跑旧 DLL）
- **EF/MySQL 事务**：`EnableRetryOnFailure` 禁止用户事务，`BeginTransactionAsync()` 会抛；Service 层勿手动开事务
- **DateTime 出网** `yyyy-MM-dd HH:mm:ss`（本地 UTC+8）；客户端反序列化须注册 `LocalDateTimeJsonConverter`
- **多租户隔离**：JWT `AppCode` 必须优先 `user.AppCode`；超管不签 AppCode claim；区域过滤 `AreaId.HasValue && allowedAreaIds.Contains`；写接口 AppCode 从 JWT 取勿前端传
- **前端依赖**：`react/react-dom` 须 `dependencies`（曾误放 peer 致 504）；装包后 `npm ls react` 确认未 prune

## 协作
- 改动不 commit 待决策；子代理禁 git 写
- 路径用 POSIX `/h/...`；`rm -rf` 用 PowerShell `Remove-Item -LiteralPath -Recurse -Force`
- 日志以 Serilog 文件槽 `logs/log-YYYYMMDD.txt` 为准（勿只 grep 单实例 stdout）

## 近期交付（详记各 dated 日志）
- 2026-11-07：登录日志 AppCode 落库+回填迁移；权限清单派生 `Permissions.All` 根治漂移；租户管理员设备列表 AllowedAreaIds 补 15；EF 幂等建库脚本 `scripts/create-database-idempotent.sql`；前端隐藏快速登录+绝对地址 `https://cloudiot.hbnewworld.com/api/v1` 重建包 `IoTPlatform-Web-dist-20261107-cloudiot.zip`
- 2026-11-05：协议冷启动自动拉起 `ProtocolConfigAutoStartHostedService` 已实现并真机验证

## 待办/技术债
- mqtt 小写 host/port 静默连 localhost 隐患待核查
- T12 运维命令后端（AnShengMaintenanceController 等 9 method）未开工
- 全局过滤器动态化、OPC UA 骨架、RabbitMQ、限流熔断、软删除+审计
