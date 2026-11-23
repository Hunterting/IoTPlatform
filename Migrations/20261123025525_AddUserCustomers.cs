using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_customers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    GrantedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "manual")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(3)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(3)"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(3)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(3)"),
                    RevokedAt = table.Column<DateTime>(type: "datetime(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_customers_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_customers_users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_customers_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_customer_id",
                table: "user_customers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "idx_granted_by",
                table: "user_customers",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "uk_user_customer",
                table: "user_customers",
                columns: new[] { "UserId", "CustomerId" },
                unique: true);

            // UpdatedAt 的 ON UPDATE 语义无法由 Pomelo 模型直接表达（§3.1），
            // 用 DB 端 ALTER 补齐；该行为不属于模型快照，后续迁移不会回退它。
            migrationBuilder.Sql(
                "ALTER TABLE user_customers MODIFY COLUMN UpdatedAt datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3);");

            // ── 存量回填（设计 §5.6）：幂等、可重复执行 ─────────────────────────
            // 1) 只处理 role='admin'；super_admin 与下级账号的单值列逐字不动。
            // 2) CustomerId 优先，AppCode 兜底（存量库可能只有 AppCode 而无 CustomerId）。
            // 3) NOT EXISTS 守卫 + uk_user_customer 唯一键，重复执行不会撞重复键。
            // 4) 不显式开启事务：AppDbContext 启用了 EnableRetryOnFailure，手动事务与之冲突。
            migrationBuilder.Sql(@"
INSERT INTO user_customers
    (UserId, CustomerId, GrantedByUserId, Source, CreatedAt, UpdatedAt)
SELECT u.Id, c.Id, NULL, 'migration', NOW(3), NOW(3)
FROM users u
JOIN customers c
  ON (u.CustomerId IS NOT NULL AND u.CustomerId = c.Id)
  OR (u.CustomerId IS NULL AND u.AppCode IS NOT NULL AND u.AppCode = c.AppCode)
WHERE u.Role = 'admin'
  AND (u.CustomerId IS NOT NULL OR u.AppCode IS NOT NULL)
  AND NOT EXISTS (
      SELECT 1 FROM user_customers uc
      WHERE uc.UserId = u.Id AND uc.CustomerId = c.Id);
");

            // 5) 清空 admin 单值列：admin 改走 ManagedCustomerIds 集合分支（D7）；
            //    保留旧单值会让 TenantFilter / AuthService 把 admin 误判为单租户用户。
            migrationBuilder.Sql(@"
UPDATE users
SET CustomerId = NULL, AppCode = NULL, UpdatedAt = NOW(3)
WHERE Role = 'admin';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 尽力回滚：把每个 admin 的最小 customer_id 还原到 users 单值列（含 AppCode 反查）。
            // 多客户授权集合无法完整还原为单值模型；完整回滚依赖数据库备份（设计 §5.6）。
            migrationBuilder.Sql(@"
UPDATE users u
JOIN (
    SELECT uc.UserId, MIN(uc.CustomerId) AS customer_id
    FROM user_customers uc
    GROUP BY uc.UserId
) g ON g.UserId = u.Id
JOIN customers c ON c.Id = g.customer_id
SET u.CustomerId = g.customer_id,
    u.AppCode = c.AppCode,
    u.UpdatedAt = NOW(3)
WHERE u.Role = 'admin';
");

            migrationBuilder.DropTable(
                name: "user_customers");
        }
    }
}
