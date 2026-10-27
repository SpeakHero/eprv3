using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Bas.Migrations
{
    /// <summary>
    /// 阶段三：组织架构与岗位（D2 节点类型重定义 / 组织编码 / 版本快照 / 统一发号器 / 审批挂起请求）。
    /// <para>Up 采用「可空加列 → 回填 → 改非空」三步，避免给存量行写入伪默认值后再留下无用的默认约束。</para>
    /// <para>Down 不可无损回滚，详见 Down 方法头部说明。</para>
    /// </summary>
    public partial class Phase3OrgAndPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ===== 迁移前计数校验（执行前手工跑一遍并留存基线，用于迁移后比对）=====
            // ① 组织总数 / 租户数：
            //    SELECT COUNT(*) AS org_total, COUNT(DISTINCT tenant_id) AS tenant_total FROM bas_organizations;
            // ② 旧类型分布（1=集团 2=子公司 3=部门）：
            //    SELECT type, COUNT(*) AS cnt FROM bas_organizations GROUP BY type;
            // ③ 岗位总数：
            //    SELECT COUNT(*) AS position_total FROM bas_positions;

            // ===== 1) 负责人字段语义变更（D2）=====
            // manager_member_id 指向「租户成员」，leader_person_id 指向「自然人」，二者语义不兼容：
            // 直接改名并统一置空，禁止把旧成员 Id 误当自然人 Id 使用。
            migrationBuilder.RenameColumn(
                name: "manager_member_id",
                table: "bas_organizations",
                newName: "leader_person_id");

            migrationBuilder.Sql("UPDATE bas_organizations SET leader_person_id = NULL;");

            // ===== 2) 组织编码（租户内唯一，统一发号器生成）=====
            // 先加可空列，按主键回填稳定唯一编码，再改非空；唯一索引在非空之后创建。
            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "bas_organizations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE bas_organizations SET code = 'ORG-' + CAST(id AS varchar(20)) WHERE code IS NULL OR code = '';");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "bas_organizations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldNullable: true,
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            // ===== 3) 生效日期 / 有效期 / 版本号 =====
            migrationBuilder.AddColumn<DateOnly>(
                name: "effect_date",
                table: "bas_organizations",
                type: "date",
                nullable: true);

            // 存量行以创建时间所在日期作为生效日期
            migrationBuilder.Sql(
                "UPDATE bas_organizations SET effect_date = CAST(created_at AS date) WHERE effect_date IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "effect_date",
                table: "bas_organizations",
                type: "date",
                nullable: false,
                oldNullable: true,
                oldType: "date");

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                table: "bas_organizations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "bas_organizations",
                type: "int",
                nullable: true);

            // 存量行版本号从 1 起算（与实体默认值一致）
            migrationBuilder.Sql("UPDATE bas_organizations SET version = 1 WHERE version IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "version",
                table: "bas_organizations",
                type: "int",
                nullable: false,
                oldNullable: true,
                oldType: "int");

            // ===== 4) D2 节点类型重定义：旧 1=集团→1、旧 2=子公司→3、旧 3=部门→3 =====
            // 旧类型先落备份表，供 Down 精确还原「旧 2」（旧 2 与旧 3 合并为新 3，仅靠当前值无法区分）。
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'bas_organizations_type_backup', N'U') IS NULL
                BEGIN
                    SELECT id, tenant_id, type AS old_type
                    INTO bas_organizations_type_backup
                    FROM bas_organizations;
                END
                """);

            migrationBuilder.Sql("UPDATE bas_organizations SET type = 3 WHERE type = 2;");

            // ===== 5) 岗位职责字段 =====
            migrationBuilder.AddColumn<string>(
                name: "duty",
                table: "bas_positions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // ===== 6) 审批挂起请求（审批通过后才生效的期望变更 + 事件幂等键）=====
            migrationBuilder.CreateTable(
                name: "bas_approval_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_instance_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    biz_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    biz_id = table.Column<long>(type: "bigint", nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_approval_requests", x => x.id);
                });

            // ===== 7) 统一发号器规则 =====
            migrationBuilder.CreateTable(
                name: "bas_code_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    biz_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    segments_json = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_code_rules", x => x.id);
                });

            // ===== 8) 组织版本快照（asOfDate 历史回看）=====
            migrationBuilder.CreateTable(
                name: "bas_org_node_histories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    org_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    snapshot_json = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    changed_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_org_node_histories", x => x.id);
                });

            // ===== 9) 索引 =====
            // 组织编码租户内唯一（parent_id / status 索引已由 AddBasTenantTables 创建）
            migrationBuilder.CreateIndex(
                name: "IX_bas_organizations_tenant_id_code",
                table: "bas_organizations",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            // 审批实例 Id 租户内唯一；发起审批前为 NULL，必须过滤唯一索引以允许多行空值并存
            migrationBuilder.CreateIndex(
                name: "IX_bas_approval_requests_tenant_id_approval_instance_id",
                table: "bas_approval_requests",
                columns: new[] { "tenant_id", "approval_instance_id" },
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bas_approval_requests_tenant_id_biz_type",
                table: "bas_approval_requests",
                columns: new[] { "tenant_id", "biz_type" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_code_rules_tenant_id_biz_type",
                table: "bas_code_rules",
                columns: new[] { "tenant_id", "biz_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bas_org_node_histories_tenant_id_org_id_version",
                table: "bas_org_node_histories",
                columns: new[] { "tenant_id", "org_id", "version" },
                unique: true);

            // ===== 迁移后计数校验（与迁移前基线逐项比对，任一不一致必须中止发布）=====
            // ① 组织总数/租户数应与迁移前一致：SELECT COUNT(*), COUNT(DISTINCT tenant_id) FROM bas_organizations;
            // ② 类型分布应为：1=原集团数，3=原（2+3）之和，4=原 4（无）：
            //    SELECT type, COUNT(*) FROM bas_organizations GROUP BY type;
            // ③ 编码必须全部非空且租户内唯一（应为 0 行）：
            //    SELECT tenant_id, code FROM bas_organizations GROUP BY tenant_id, code HAVING COUNT(*) > 1;
            //    SELECT COUNT(*) FROM bas_organizations WHERE code IS NULL OR code = '';
            // ④ 版本号应全部 >= 1：SELECT COUNT(*) FROM bas_organizations WHERE version < 1;
            // ⑤ 生效日期应全部非空：SELECT COUNT(*) FROM bas_organizations WHERE effect_date IS NULL;
            // ⑥ 岗位总数应与迁移前一致：SELECT COUNT(*) FROM bas_positions;
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ===== 回滚说明：不可无损回滚 =====
            // 1) D2 类型映射不可逆：Up 将「旧 2=子公司」并入「新 3=部门」，回滚会统一还原为新库的 3。
            //    已通过 Up 写入的 bas_organizations_type_backup 精确还原「旧 2」（仅当当前 type 仍为 3），
            //    无法区分的是「Up 之后新建成 3=部门」的行（将被一并还原为旧 3=部门，语义上仍是部门，可接受）。
            // 2) leader_person_id（自然人）与旧 manager_member_id（租户成员）语义不同：Up 已统一置空，
            //    回滚后旧负责人一律为 NULL，原关联不可恢复。
            // 3) 组织编码 code 由统一发号器生成，回滚 DROP 该列后不可恢复；如需留档请在回滚前导出：
            //    SELECT id, tenant_id, code FROM bas_organizations;
            // 4) 版本快照 bas_org_node_histories / 审批挂起请求 bas_approval_requests / 编码规则 bas_code_rules
            //    均为阶段三新增数据，回滚 DROP 后不可恢复；如需留档请先导出。

            // 旧类型还原（先于删列，避免受后续结构变更影响）
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'bas_organizations_type_backup', N'U') IS NOT NULL
                BEGIN
                    UPDATE o
                    SET o.type = 2
                    FROM bas_organizations AS o
                    INNER JOIN bas_organizations_type_backup AS b
                        ON b.id = o.id AND b.tenant_id = o.tenant_id
                    WHERE b.old_type = 2 AND o.type = 3;

                    DROP TABLE bas_organizations_type_backup;
                END
                """);

            migrationBuilder.DropTable(
                name: "bas_approval_requests");

            migrationBuilder.DropTable(
                name: "bas_code_rules");

            migrationBuilder.DropTable(
                name: "bas_org_node_histories");

            migrationBuilder.DropIndex(
                name: "IX_bas_organizations_tenant_id_code",
                table: "bas_organizations");

            migrationBuilder.DropColumn(
                name: "duty",
                table: "bas_positions");

            migrationBuilder.DropColumn(
                name: "code",
                table: "bas_organizations");

            migrationBuilder.DropColumn(
                name: "effect_date",
                table: "bas_organizations");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                table: "bas_organizations");

            migrationBuilder.DropColumn(
                name: "version",
                table: "bas_organizations");

            // 列名改回旧语义（值已置空，见上方说明 2）
            migrationBuilder.RenameColumn(
                name: "leader_person_id",
                table: "bas_organizations",
                newName: "manager_member_id");
        }
    }
}
