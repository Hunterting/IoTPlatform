using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <summary>
    /// 为待认领池 <c>discovered_ansheng_devices</c> 补上 <c>UNIQUE(Imei, AppCode)</c>，
    /// 从数据库层兜住设备发现的 check-then-act 竞态（同一 IMEI 落出多行待认领记录）。
    ///
    /// 【建索引前必须先去重】存量库若已有重复行，<c>CREATE UNIQUE INDEX</c> 会直接报
    /// <c>ER_DUP_ENTRY(1062)</c> 使整个迁移失败。故 Up 分两步：先去重，再建索引。
    ///
    /// 【保留哪一行】按 <c>IsClaimed DESC, LastSeenAt DESC, Id DESC</c> 取第一行：
    ///   ① 已认领行优先——它被 <c>devices.ClaimedDeviceId</c> 反向引用，删掉会留下悬空关联；
    ///   ② 其次留 <c>LastSeenAt</c> 最新的——它才是设备当前真正在更新的那一行
    ///      （<c>MySQL</c> 下 <c>DESC</c> 排序 NULL 落在最后，从未在线的行自然被淘汰）；
    ///   ③ 最后按 <c>Id</c> 兜底，保证结果确定。
    ///
    /// 【MySQL 5.7.26 兼容】不用窗口函数 / CTE（5.7 不支持），改用
    /// <c>GROUP_CONCAT(... ORDER BY ...) + SUBSTRING_INDEX</c> 取组内首行（argmax）。
    /// <c>GROUP_CONCAT</c> 的 1024 字节截断只影响尾部，取首元素不受影响。
    /// </summary>
    public partial class AddDiscoveredAnShengUniqueImeiAppCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 步骤 1：清理存量重复行（无重复时为空操作）──
            // 多表 DELETE + 派生表：派生表会被物化，不触发 MySQL 1093
            //（“不能在 FROM 子句中指定目标表”）的限制。
            // AppCode 可空，故连接条件用 NULL 安全等值 <=>；GROUP BY 亦将 NULL 视作同一组。
            migrationBuilder.Sql(@"
DELETE d
FROM discovered_ansheng_devices d
INNER JOIN (
    SELECT t.Imei,
           t.AppCode,
           CAST(SUBSTRING_INDEX(
                    GROUP_CONCAT(t.Id ORDER BY t.IsClaimed DESC, t.LastSeenAt DESC, t.Id DESC),
                    ',', 1) AS UNSIGNED) AS KeepId
    FROM discovered_ansheng_devices t
    GROUP BY t.Imei, t.AppCode
    HAVING COUNT(*) > 1
) k ON d.Imei = k.Imei AND d.AppCode <=> k.AppCode
WHERE d.Id <> k.KeepId;");

            // ── 步骤 2：建唯一索引 ──
            migrationBuilder.CreateIndex(
                name: "IX_discovered_ansheng_devices_Imei_AppCode",
                table: "discovered_ansheng_devices",
                columns: new[] { "Imei", "AppCode" },
                unique: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 只回滚索引。步骤 1 删除的重复行是不可逆的——那些行本就是竞态产生的脏数据，
        /// 回滚时再造回去没有意义，也无从还原。
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_discovered_ansheng_devices_Imei_AppCode",
                table: "discovered_ansheng_devices");
        }
    }
}
