/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.2.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：BasTenantDbContext 的 DbSet 集合（按业务域分组：Org / Goods / Supplier / Customer /
 *       Person / Dictionary / Numbering；阶段三新增实体随对应任务落地时按域追加）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Dictionary;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Domain.Numbering;

namespace Qlx.Modules.Bas;

public sealed partial class BasTenantDbContext
{
    // ======= Org 域（租户自管理：组织 / 岗位 / 角色 / 成员 / 组织版本快照 / 审批挂起请求）=======
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<OrgNodeHistory> OrgNodeHistories => Set<OrgNodeHistory>();

    public DbSet<BasApprovalRequest> ApprovalRequests => Set<BasApprovalRequest>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<MemberRole> MemberRoles => Set<MemberRole>();

    // ======= Goods 域（商品分类 / 计量单位 / 单位换算）=======
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();

    public DbSet<UnitGroup> UnitGroups => Set<UnitGroup>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();

    // ======= Supplier 域（供应商主表 / 资质 / 银行 / 开票 / 评级历史）=======
    public DbSet<Domain.Supplier.Supplier> Suppliers => Set<Domain.Supplier.Supplier>();

    public DbSet<Domain.Supplier.SupplierCert> SupplierCerts => Set<Domain.Supplier.SupplierCert>();

    public DbSet<Domain.Supplier.SupplierBank> SupplierBanks => Set<Domain.Supplier.SupplierBank>();

    public DbSet<Domain.Supplier.SupplierInvoice> SupplierInvoices => Set<Domain.Supplier.SupplierInvoice>();

    public DbSet<Domain.Supplier.SupplierGradeLog> SupplierGradeLogs => Set<Domain.Supplier.SupplierGradeLog>();

    // ======= Customer 域（客户主表 / 银行 / 开票 / 信用变更历史）=======
    public DbSet<Domain.Customer.Customer> Customers => Set<Domain.Customer.Customer>();

    public DbSet<Domain.Customer.CustomerBank> CustomerBanks => Set<Domain.Customer.CustomerBank>();

    public DbSet<Domain.Customer.CustomerInvoice> CustomerInvoices => Set<Domain.Customer.CustomerInvoice>();

    public DbSet<Domain.Customer.CustomerCreditLog> CustomerCreditLogs => Set<Domain.Customer.CustomerCreditLog>();

    // ======= Person 域（人员主数据 / 任职记录 / 证件档案；阶段三落地）=======
    public DbSet<Domain.Person.Person> Persons => Set<Domain.Person.Person>();

    public DbSet<Domain.Person.PersonAssignment> PersonAssignments => Set<Domain.Person.PersonAssignment>();

    public DbSet<Domain.Person.PersonCert> PersonCerts => Set<Domain.Person.PersonCert>();

    // ======= Dictionary 域（字典类型 / 字典项）=======
    public DbSet<DictType> DictTypes => Set<DictType>();

    public DbSet<DictItem> DictItems => Set<DictItem>();

    // ======= Numbering 域（编码规则）=======
    public DbSet<CodeRule> CodeRules => Set<CodeRule>();
}
