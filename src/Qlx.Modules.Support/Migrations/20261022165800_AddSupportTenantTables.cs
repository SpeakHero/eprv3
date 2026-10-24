using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Support.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportTenantTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apr_delegates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    delegator_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    agent_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    biz_types_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    start_time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    end_time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_delegates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "apr_flow_branches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    def_id = table.Column<long>(type: "bigint", nullable: false),
                    from_node_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    to_node_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expr_json = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_flow_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "apr_flow_definitions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    def_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    def_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    biz_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    org_scope_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    design_json = table.Column<string>(type: "nvarchar(max)", maxLength: 200000, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_flow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "apr_flow_nodes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    def_id = table.Column<long>(type: "bigint", nullable: false),
                    node_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    node_type = table.Column<int>(type: "int", nullable: false),
                    node_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    approver_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approver_ref = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    sign_mode = table.Column<int>(type: "int", nullable: false),
                    timeout_hours = table.Column<int>(type: "int", nullable: false),
                    timeout_action = table.Column<int>(type: "int", nullable: false),
                    allow_add_sign = table.Column<bool>(type: "bit", nullable: false),
                    reject_mode = table.Column<int>(type: "int", nullable: false),
                    sort_no = table.Column<int>(type: "int", nullable: false),
                    skip_if_initiator = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_flow_nodes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "apr_instances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    inst_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    def_id = table.Column<long>(type: "bigint", nullable: false),
                    def_version = table.Column<int>(type: "int", nullable: false),
                    def_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    biz_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    biz_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    biz_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    initiator_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    org_id = table.Column<long>(type: "bigint", nullable: false),
                    current_node_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "apr_op_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    inst_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    operator_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_op_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "apr_tasks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    inst_id = table.Column<long>(type: "bigint", nullable: false),
                    node_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    node_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    approver_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_from = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    opinion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    handled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    urge_count = table.Column<int>(type: "int", nullable: false),
                    urge_last_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fil_acl_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tpl_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    biz_domain = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    entries_json = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_acl_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fil_classify_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    biz_module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    doc_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    target_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    acl_tpl_id = table.Column<long>(type: "bigint", nullable: true),
                    protect_source = table.Column<bool>(type: "bit", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_classify_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fil_file_acl_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    object_id = table.Column<long>(type: "bigint", nullable: false),
                    subject_type = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    perm_mask = table.Column<int>(type: "int", nullable: false),
                    ace_type = table.Column<int>(type: "int", nullable: false),
                    inherit_src = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    propagate = table.Column<bool>(type: "bit", nullable: false),
                    granted_by = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_file_acl_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fil_file_op_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    object_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    operator_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_file_op_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fil_file_versions",
                columns: table => new
                {
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    object_id = table.Column<long>(type: "bigint", nullable: false),
                    ver_no = table.Column<int>(type: "int", nullable: false),
                    hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_file_versions", x => new { x.tenant_id, x.object_id, x.ver_no });
                });

            migrationBuilder.CreateTable(
                name: "fil_share_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    object_id = table.Column<long>(type: "bigint", nullable: false),
                    token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expire_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    access_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_share_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fil_storage_objects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    object_type = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    biz_domain = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    path = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    mime = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    current_version = table.Column<int>(type: "int", nullable: false),
                    owner_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    inherit_acl = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    ref_count = table.Column<int>(type: "int", nullable: false),
                    recycled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_storage_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fil_temp_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    object_id = table.Column<long>(type: "bigint", nullable: false),
                    exec_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    owner_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expire_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fil_temp_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    task_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    cron_override = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    timeout_seconds_override = table.Column<int>(type: "int", nullable: true),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_definitions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    task_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    module_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    handler_type_name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    default_cron = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    default_mode = table.Column<int>(type: "int", nullable: false),
                    default_timeout_seconds = table.Column<int>(type: "int", nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    paused = table.Column<bool>(type: "bit", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_executions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    mode = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    trigger_source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    initiator_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    parameters_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    result_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    timeout_seconds = table.Column<int>(type: "int", nullable: false),
                    retry_policy_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    hangfire_job_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    level = table.Column<int>(type: "int", nullable: false),
                    message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    stack_trace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    log_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msg_message_global_states",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    global_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    state_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_message_global_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msg_message_inboxes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    recipient_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    is_read = table.Column<bool>(type: "bit", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_message_inboxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msg_message_send_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<int>(type: "int", nullable: false),
                    recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    fail_reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_message_send_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msg_message_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    msg_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    channels_mask = table.Column<int>(type: "int", nullable: false),
                    locked = table.Column<bool>(type: "bit", nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_message_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msg_message_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    tpl_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    msg_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    channel = table.Column<int>(type: "int", nullable: false),
                    subject_template = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    content_template = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    var_schema_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_message_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msg_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    msg_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tpl_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    channel_mask = table.Column<int>(type: "int", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    sender_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    recipient_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    biz_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    biz_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    biz_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    idempotency_ref = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prt_print_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    template_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    biz_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    content_html = table.Column<string>(type: "nvarchar(max)", maxLength: 200000, nullable: false),
                    css = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prt_print_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_apr_delegates_tenant_id_agent_id",
                table: "apr_delegates",
                columns: new[] { "tenant_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_delegates_tenant_id_delegator_id_status_start_time",
                table: "apr_delegates",
                columns: new[] { "tenant_id", "delegator_id", "status", "start_time" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_flow_branches_tenant_id_def_id_from_node_key",
                table: "apr_flow_branches",
                columns: new[] { "tenant_id", "def_id", "from_node_key" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_flow_definitions_tenant_id_biz_type_def_code_version",
                table: "apr_flow_definitions",
                columns: new[] { "tenant_id", "biz_type", "def_code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apr_flow_definitions_tenant_id_biz_type_status",
                table: "apr_flow_definitions",
                columns: new[] { "tenant_id", "biz_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_flow_nodes_tenant_id_def_id_node_key",
                table: "apr_flow_nodes",
                columns: new[] { "tenant_id", "def_id", "node_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apr_instances_tenant_id_biz_type_biz_id",
                table: "apr_instances",
                columns: new[] { "tenant_id", "biz_type", "biz_id" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_instances_tenant_id_initiator_id_status_started_at",
                table: "apr_instances",
                columns: new[] { "tenant_id", "initiator_id", "status", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_instances_tenant_id_inst_no",
                table: "apr_instances",
                columns: new[] { "tenant_id", "inst_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apr_op_logs_tenant_id_inst_id_created_at",
                table: "apr_op_logs",
                columns: new[] { "tenant_id", "inst_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_tasks_tenant_id_approver_id_status",
                table: "apr_tasks",
                columns: new[] { "tenant_id", "approver_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_tasks_tenant_id_inst_id_node_key",
                table: "apr_tasks",
                columns: new[] { "tenant_id", "inst_id", "node_key" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_acl_templates_tenant_id_biz_domain",
                table: "fil_acl_templates",
                columns: new[] { "tenant_id", "biz_domain" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_classify_rules_tenant_id_biz_module_doc_type",
                table: "fil_classify_rules",
                columns: new[] { "tenant_id", "biz_module", "doc_type" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_file_acl_entries_tenant_id_object_id",
                table: "fil_file_acl_entries",
                columns: new[] { "tenant_id", "object_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_file_acl_entries_tenant_id_subject_type_subject_id",
                table: "fil_file_acl_entries",
                columns: new[] { "tenant_id", "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_file_op_logs_tenant_id_created_at",
                table: "fil_file_op_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_file_op_logs_tenant_id_object_id",
                table: "fil_file_op_logs",
                columns: new[] { "tenant_id", "object_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_file_versions_tenant_id_hash",
                table: "fil_file_versions",
                columns: new[] { "tenant_id", "hash" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_share_links_tenant_id_object_id",
                table: "fil_share_links",
                columns: new[] { "tenant_id", "object_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_share_links_tenant_id_token",
                table: "fil_share_links",
                columns: new[] { "tenant_id", "token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fil_storage_objects_tenant_id_hash",
                table: "fil_storage_objects",
                columns: new[] { "tenant_id", "hash" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_storage_objects_tenant_id_parent_id",
                table: "fil_storage_objects",
                columns: new[] { "tenant_id", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_storage_objects_tenant_id_status",
                table: "fil_storage_objects",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_temp_files_tenant_id_expire_at",
                table: "fil_temp_files",
                columns: new[] { "tenant_id", "expire_at" });

            migrationBuilder.CreateIndex(
                name: "IX_fil_temp_files_tenant_id_object_id",
                table: "fil_temp_files",
                columns: new[] { "tenant_id", "object_id" });

            migrationBuilder.CreateIndex(
                name: "IX_job_configs_tenant_id_task_code",
                table: "job_configs",
                columns: new[] { "tenant_id", "task_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_definitions_tenant_id_enabled_next_run_at",
                table: "job_definitions",
                columns: new[] { "tenant_id", "enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_definitions_tenant_id_task_code",
                table: "job_definitions",
                columns: new[] { "tenant_id", "task_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_executions_tenant_id_execution_id",
                table: "job_executions",
                columns: new[] { "tenant_id", "execution_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_executions_tenant_id_idempotency_key",
                table: "job_executions",
                columns: new[] { "tenant_id", "idempotency_key" });

            migrationBuilder.CreateIndex(
                name: "IX_job_executions_tenant_id_task_code_status_queued_at",
                table: "job_executions",
                columns: new[] { "tenant_id", "task_code", "status", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_logs_tenant_id_execution_id_log_at",
                table: "job_logs",
                columns: new[] { "tenant_id", "execution_id", "log_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_logs_tenant_id_task_code_log_at",
                table: "job_logs",
                columns: new[] { "tenant_id", "task_code", "log_at" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_global_states_tenant_id_global_id_user_id",
                table: "msg_message_global_states",
                columns: new[] { "tenant_id", "global_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_inboxes_tenant_id_message_id",
                table: "msg_message_inboxes",
                columns: new[] { "tenant_id", "message_id" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_inboxes_tenant_id_recipient_user_id_is_read",
                table: "msg_message_inboxes",
                columns: new[] { "tenant_id", "recipient_user_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_send_logs_tenant_id_message_id_created_at",
                table: "msg_message_send_logs",
                columns: new[] { "tenant_id", "message_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_subscriptions_tenant_id_user_id_msg_type",
                table: "msg_message_subscriptions",
                columns: new[] { "tenant_id", "user_id", "msg_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_templates_tenant_id_tpl_code_channel_version",
                table: "msg_message_templates",
                columns: new[] { "tenant_id", "tpl_code", "channel", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_msg_messages_tenant_id_idempotency_ref_created_at",
                table: "msg_messages",
                columns: new[] { "tenant_id", "idempotency_ref", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_messages_tenant_id_recipient_user_id_created_at",
                table: "msg_messages",
                columns: new[] { "tenant_id", "recipient_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_messages_tenant_id_status_created_at",
                table: "msg_messages",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_prt_print_templates_tenant_id_template_code_status",
                table: "prt_print_templates",
                columns: new[] { "tenant_id", "template_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_prt_print_templates_tenant_id_template_code_version",
                table: "prt_print_templates",
                columns: new[] { "tenant_id", "template_code", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apr_delegates");

            migrationBuilder.DropTable(
                name: "apr_flow_branches");

            migrationBuilder.DropTable(
                name: "apr_flow_definitions");

            migrationBuilder.DropTable(
                name: "apr_flow_nodes");

            migrationBuilder.DropTable(
                name: "apr_instances");

            migrationBuilder.DropTable(
                name: "apr_op_logs");

            migrationBuilder.DropTable(
                name: "apr_tasks");

            migrationBuilder.DropTable(
                name: "fil_acl_templates");

            migrationBuilder.DropTable(
                name: "fil_classify_rules");

            migrationBuilder.DropTable(
                name: "fil_file_acl_entries");

            migrationBuilder.DropTable(
                name: "fil_file_op_logs");

            migrationBuilder.DropTable(
                name: "fil_file_versions");

            migrationBuilder.DropTable(
                name: "fil_share_links");

            migrationBuilder.DropTable(
                name: "fil_storage_objects");

            migrationBuilder.DropTable(
                name: "fil_temp_files");

            migrationBuilder.DropTable(
                name: "job_configs");

            migrationBuilder.DropTable(
                name: "job_definitions");

            migrationBuilder.DropTable(
                name: "job_executions");

            migrationBuilder.DropTable(
                name: "job_logs");

            migrationBuilder.DropTable(
                name: "msg_message_global_states");

            migrationBuilder.DropTable(
                name: "msg_message_inboxes");

            migrationBuilder.DropTable(
                name: "msg_message_send_logs");

            migrationBuilder.DropTable(
                name: "msg_message_subscriptions");

            migrationBuilder.DropTable(
                name: "msg_message_templates");

            migrationBuilder.DropTable(
                name: "msg_messages");

            migrationBuilder.DropTable(
                name: "prt_print_templates");
        }
    }
}
