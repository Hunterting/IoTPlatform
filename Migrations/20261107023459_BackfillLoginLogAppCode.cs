using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLoginLogAppCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 回填历史登录日志的 AppCode：按 UserId 关联 users 表取该用户的 AppCode。
            // 仅更新 AppCode 为 NULL 的历史行；login_logs / users 为实体经 [Table] 实际映射的表名
            // （见 Models/LoginLog.cs、Models/User.cs），不可用 C# 实体名 LoginLogs/Users。
            // UserId 为 NULL 的行（匿名/失败登录）子查询返回 NULL，等价于维持 NULL，不影响租户过滤语义。
            migrationBuilder.Sql(
                "UPDATE login_logs SET AppCode = (SELECT u.AppCode FROM users u WHERE u.Id = login_logs.UserId) WHERE AppCode IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
