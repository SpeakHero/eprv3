/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas 模块端点授权规则基线——原控制器硬编码 [Authorize] 标注的 1:1 快照
 *       （TenantAdmin 26 条动作级权限码 + 控制器级租户上下文；TenantMenu 仅控制器级），
 *       阶段三补充 /api/bas/* 主数据控制器规则（控制器级租户上下文 + 动作级权限点）。
 *       规则调整优先改 config/common/authorization.json 覆盖，勿改本基线。
 */

using Qlx.Abstractions.Authorization;

namespace Qlx.Modules.Bas;

/// <summary>Bas 模块端点授权规则映射提供方。</summary>
public sealed class BasEndpointPolicyMap : IEndpointPolicyMapProvider
{
    /// <inheritdoc />
    public IReadOnlyList<EndpointPolicyRule> GetRules() =>
    [
        // TenantAdmin：全部端点要求租户级 Token（类级 QlxTenantContext）
        new() { Controller = "TenantAdmin", RequireTenantContext = true },

        // 组织
        new() { Controller = "TenantAdmin", Action = "GetOrgTree", PermissionCode = "bas.admin.org.menu" },
        new() { Controller = "TenantAdmin", Action = "CreateOrg", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "TenantAdmin", Action = "UpdateOrg", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "TenantAdmin", Action = "ToggleOrgStatus", PermissionCode = "bas.admin.org.action" },

        // 岗位
        new() { Controller = "TenantAdmin", Action = "GetPositions", PermissionCode = "bas.admin.org.menu" },
        new() { Controller = "TenantAdmin", Action = "SavePosition", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "TenantAdmin", Action = "TogglePositionStatus", PermissionCode = "bas.admin.org.action" },

        // 成员
        new() { Controller = "TenantAdmin", Action = "GetMembers", PermissionCode = "bas.admin.member.menu" },
        new() { Controller = "TenantAdmin", Action = "InviteMember", PermissionCode = "bas.admin.member.action" },
        new() { Controller = "TenantAdmin", Action = "GetMemberDetail", PermissionCode = "bas.admin.member.menu" },
        new() { Controller = "TenantAdmin", Action = "ToggleMemberStatus", PermissionCode = "bas.admin.member.action" },
        new() { Controller = "TenantAdmin", Action = "ResetMemberPassword", PermissionCode = "bas.admin.member.action" },
        new() { Controller = "TenantAdmin", Action = "RemoveMember", PermissionCode = "bas.admin.member.action" },
        new() { Controller = "TenantAdmin", Action = "ResendMemberInvite", PermissionCode = "bas.admin.member.action" },
        new() { Controller = "TenantAdmin", Action = "ImportMembers", PermissionCode = "bas.admin.member.action" },
        new() { Controller = "TenantAdmin", Action = "GetMemberLoginLogs", PermissionCode = "bas.admin.member.menu" },

        // 角色与权限
        new() { Controller = "TenantAdmin", Action = "GetRoles", PermissionCode = "bas.admin.role.menu" },
        new() { Controller = "TenantAdmin", Action = "CreateRole", PermissionCode = "bas.admin.role.action" },
        new() { Controller = "TenantAdmin", Action = "UpdateRole", PermissionCode = "bas.admin.role.action" },
        new() { Controller = "TenantAdmin", Action = "DeleteRole", PermissionCode = "bas.admin.role.action" },
        new() { Controller = "TenantAdmin", Action = "SaveRolePermissions", PermissionCode = "bas.admin.role.action" },
        new() { Controller = "TenantAdmin", Action = "GetRolePermissions", PermissionCode = "bas.admin.role.menu" },
        new() { Controller = "TenantAdmin", Action = "GetRoleMembers", PermissionCode = "bas.admin.role.menu" },
        new() { Controller = "TenantAdmin", Action = "SaveRoleMembers", PermissionCode = "bas.admin.role.action" },
        new() { Controller = "TenantAdmin", Action = "GetPermissions", PermissionCode = "bas.admin.role.menu" },
        new() { Controller = "TenantAdmin", Action = "GetTenantModules", PermissionCode = "bas.admin.role.menu" },

        // TenantMenu：仅控制器级租户上下文（无动作级权限点）
        new() { Controller = "TenantMenu", RequireTenantContext = true },

        // ======= 阶段三 /api/bas/* 主数据端点（控制器本批尚未创建，规则先行登记，控制器落地即生效）=======

        // 组织架构（/api/bas/orgs）：复用租户自管理域组织权限点
        new() { Controller = "Org", RequireTenantContext = true },
        new() { Controller = "Org", Action = "GetList", PermissionCode = "bas.admin.org.menu" },
        new() { Controller = "Org", Action = "GetDetail", PermissionCode = "bas.admin.org.menu" },
        new() { Controller = "Org", Action = "History", PermissionCode = "bas.admin.org.menu" },
        new() { Controller = "Org", Action = "Create", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "Org", Action = "Update", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "Org", Action = "Toggle", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "Org", Action = "Merge", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "Org", Action = "CreateRequest", PermissionCode = "bas.admin.org.action" },

        // 岗位（/api/bas/positions）：复用租户自管理域组织权限点
        new() { Controller = "Position", RequireTenantContext = true },
        new() { Controller = "Position", Action = "GetList", PermissionCode = "bas.admin.org.menu" },
        new() { Controller = "Position", Action = "Save", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "Position", Action = "Toggle", PermissionCode = "bas.admin.org.action" },
        new() { Controller = "Position", Action = "CreateOverstaffRequest", PermissionCode = "bas.admin.org.action" },

        // 商品分类（/api/bas/categories）
        new() { Controller = "Category", RequireTenantContext = true },
        new() { Controller = "Category", Action = "GetList", PermissionCode = "bas.category.menu" },
        new() { Controller = "Category", Action = "GetDetail", PermissionCode = "bas.category.menu" },
        new() { Controller = "Category", Action = "Create", PermissionCode = "bas.category.action" },
        new() { Controller = "Category", Action = "Update", PermissionCode = "bas.category.action" },
        new() { Controller = "Category", Action = "Toggle", PermissionCode = "bas.category.action" },
        new() { Controller = "Category", Action = "CreateDisableRequest", PermissionCode = "bas.category.action" },

        // 计量单位（/api/bas/unit-groups、/api/bas/units、/api/bas/units/conversions）
        new() { Controller = "Unit", RequireTenantContext = true },
        new() { Controller = "Unit", Action = "GetGroups", PermissionCode = "bas.unit.menu" },
        new() { Controller = "Unit", Action = "SaveGroup", PermissionCode = "bas.unit.action" },
        new() { Controller = "Unit", Action = "ToggleGroup", PermissionCode = "bas.unit.action" },
        new() { Controller = "Unit", Action = "GetList", PermissionCode = "bas.unit.menu" },
        new() { Controller = "Unit", Action = "Save", PermissionCode = "bas.unit.action" },
        new() { Controller = "Unit", Action = "Toggle", PermissionCode = "bas.unit.action" },
        new() { Controller = "Unit", Action = "GetConversions", PermissionCode = "bas.unit.menu" },
        new() { Controller = "Unit", Action = "SaveConversion", PermissionCode = "bas.unit.action" },

        // 供应商（/api/bas/suppliers）
        new() { Controller = "Supplier", RequireTenantContext = true },
        new() { Controller = "Supplier", Action = "GetList", PermissionCode = "bas.supplier.menu" },
        new() { Controller = "Supplier", Action = "GetDetail", PermissionCode = "bas.supplier.menu" },
        new() { Controller = "Supplier", Action = "GetCerts", PermissionCode = "bas.supplier.menu" },
        new() { Controller = "Supplier", Action = "GetBanks", PermissionCode = "bas.supplier.menu" },
        new() { Controller = "Supplier", Action = "GetInvoices", PermissionCode = "bas.supplier.menu" },
        new() { Controller = "Supplier", Action = "Create", PermissionCode = "bas.supplier.action" },
        new() { Controller = "Supplier", Action = "Update", PermissionCode = "bas.supplier.action" },
        new() { Controller = "Supplier", Action = "ChangeGrade", PermissionCode = "bas.supplier.action" },
        new() { Controller = "Supplier", Action = "SubmitAdmission", PermissionCode = "bas.supplier.action" },
        new() { Controller = "Supplier", Action = "SaveCert", PermissionCode = "bas.supplier.action" },
        new() { Controller = "Supplier", Action = "SaveBank", PermissionCode = "bas.supplier.action" },
        new() { Controller = "Supplier", Action = "SubmitBankChange", PermissionCode = "bas.supplier.action" },
        new() { Controller = "Supplier", Action = "SaveInvoice", PermissionCode = "bas.supplier.action" },

        // 客户（/api/bas/customers）
        new() { Controller = "Customer", RequireTenantContext = true },
        new() { Controller = "Customer", Action = "GetList", PermissionCode = "bas.customer.menu" },
        new() { Controller = "Customer", Action = "GetDetail", PermissionCode = "bas.customer.menu" },
        new() { Controller = "Customer", Action = "GetCredit", PermissionCode = "bas.customer.menu" },
        new() { Controller = "Customer", Action = "GetBanks", PermissionCode = "bas.customer.menu" },
        new() { Controller = "Customer", Action = "GetInvoices", PermissionCode = "bas.customer.menu" },
        new() { Controller = "Customer", Action = "Create", PermissionCode = "bas.customer.action" },
        new() { Controller = "Customer", Action = "Update", PermissionCode = "bas.customer.action" },
        new() { Controller = "Customer", Action = "SubmitAdmission", PermissionCode = "bas.customer.action" },
        new() { Controller = "Customer", Action = "SubmitCreditAdjust", PermissionCode = "bas.customer.action" },
        new() { Controller = "Customer", Action = "SaveBank", PermissionCode = "bas.customer.action" },
        new() { Controller = "Customer", Action = "SaveInvoice", PermissionCode = "bas.customer.action" },

        // 人员主数据（/api/bas/persons；明文端点独立字段级权限点，D5）
        new() { Controller = "Person", RequireTenantContext = true },
        new() { Controller = "Person", Action = "GetList", PermissionCode = "bas.person.menu" },
        new() { Controller = "Person", Action = "GetDetail", PermissionCode = "bas.person.menu" },
        new() { Controller = "Person", Action = "Create", PermissionCode = "bas.person.action" },
        new() { Controller = "Person", Action = "Update", PermissionCode = "bas.person.action" },
        new() { Controller = "Person", Action = "Leave", PermissionCode = "bas.person.action" },
        new() { Controller = "Person", Action = "Rehire", PermissionCode = "bas.person.action" },
        new() { Controller = "Person", Action = "GetPlaintext", PermissionCode = "bas.person.field.plaintext" },
        new() { Controller = "Person", Action = "GetCerts", PermissionCode = "bas.person.menu" },
        new() { Controller = "Person", Action = "SaveCert", PermissionCode = "bas.person.action" },
        new() { Controller = "Person", Action = "UpdateCert", PermissionCode = "bas.person.action" },
        new() { Controller = "Person", Action = "DeleteCert", PermissionCode = "bas.person.action" },

        // 数据字典（/api/bas/dictionaries）：类型与字典项两级维护
        new() { Controller = "Dictionary", RequireTenantContext = true },
        new() { Controller = "Dictionary", Action = "GetList", PermissionCode = "bas.dict.menu" },
        new() { Controller = "Dictionary", Action = "GetDetail", PermissionCode = "bas.dict.menu" },
        new() { Controller = "Dictionary", Action = "GetItems", PermissionCode = "bas.dict.menu" },
        new() { Controller = "Dictionary", Action = "Create", PermissionCode = "bas.dict.action" },
        new() { Controller = "Dictionary", Action = "Update", PermissionCode = "bas.dict.action" },
        new() { Controller = "Dictionary", Action = "Toggle", PermissionCode = "bas.dict.action" },
        new() { Controller = "Dictionary", Action = "CreateItem", PermissionCode = "bas.dict.action" },
        new() { Controller = "Dictionary", Action = "UpdateItem", PermissionCode = "bas.dict.action" },
        new() { Controller = "Dictionary", Action = "ToggleItem", PermissionCode = "bas.dict.action" },
        new() { Controller = "Dictionary", Action = "DeleteItem", PermissionCode = "bas.dict.action" },

        // 编码规则（/api/bas/code-rules）：仅查询与启停，规则本身不可修改
        new() { Controller = "CodeRule", RequireTenantContext = true },
        new() { Controller = "CodeRule", Action = "GetList", PermissionCode = "bas.coderule.menu" },
        new() { Controller = "CodeRule", Action = "Enable", PermissionCode = "bas.coderule.action" },
        new() { Controller = "CodeRule", Action = "Disable", PermissionCode = "bas.coderule.action" },

        // 变更日志（/api/bas/changelogs）：只读查询
        new() { Controller = "ChangeLog", RequireTenantContext = true },
        new() { Controller = "ChangeLog", Action = "GetList", PermissionCode = "bas.changelog.menu" },

        // 导入导出（/api/bas/import-export）：模板/资源维度权限在服务层按模板编码二次校验，
        // 静态策略仅登记租户上下文（同一动作服务多资源模板，无法以单一权限码表达）
        new() { Controller = "ImportExport", RequireTenantContext = true },
    ];
}
