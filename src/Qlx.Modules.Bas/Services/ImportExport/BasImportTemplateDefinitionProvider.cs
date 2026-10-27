/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:05:00 +08:00 中国标准时间 星期一
 * 描述：Bas 导入导出模板定义提供方（Singleton）——供应商 / 客户 / 人员 / 分类 的导入模板（不含编码列）
 *       与导出模板（含编码列）共 8 份代码态定义
 */

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.ImportExport;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// Bas 导入导出模板定义提供方。<para>
/// 导入模板不含编码列（编码由后端统一发号器生成，N-1）；导出模板含编码列（只读回显），
/// 因此导入 / 导出各声明一套模板定义，模板编码以 <c>.import</c> / <c>.export</c> 区分。</para>
/// </summary>
internal sealed class BasImportTemplateDefinitionProvider : IImportTemplateDefinitionProvider
{
    /// <summary>供应商导入模板编码。</summary>
    internal const string SupplierImport = "bas.supplier.import";

    /// <summary>客户导入模板编码。</summary>
    internal const string CustomerImport = "bas.customer.import";

    /// <summary>人员导入模板编码。</summary>
    internal const string PersonImport = "bas.person.import";

    /// <summary>商品分类导入模板编码。</summary>
    internal const string CategoryImport = "bas.category.import";

    /// <summary>供应商导出模板编码。</summary>
    internal const string SupplierExport = "bas.supplier.export";

    /// <summary>客户导出模板编码。</summary>
    internal const string CustomerExport = "bas.customer.export";

    /// <summary>人员导出模板编码。</summary>
    internal const string PersonExport = "bas.person.export";

    /// <summary>商品分类导出模板编码。</summary>
    internal const string CategoryExport = "bas.category.export";

    private static readonly ImportTemplateDefinition[] s_definitions =
    [
        // ======== 导入模板（不含编码列）========
        new(
            SupplierImport,
            "供应商导入",
            QlxModuleCodes.Bas,
            [
                new("name", "供应商名称", true, ImportColumnDataTypes.Text, 128, ExampleValue: "成都示例科技有限公司"),
                new("uscc", "统一社会信用代码", true, ImportColumnDataTypes.Text, 18, ExampleValue: "91510000MA00000001"),
                new(
                    "supplierType", "供应商类型", true, ImportColumnDataTypes.Text, 1,
                    ExampleValue: "2", Description: "1=生产厂家 / 2=经销商 / 3=劳务分包商 / 4=服务商 / 5=其他"),
                new("contact", "联系人", false, ImportColumnDataTypes.Text, 64),
                new("phone", "联系电话", false, ImportColumnDataTypes.Text, 32),
            ]),
        new(
            CustomerImport,
            "客户导入",
            QlxModuleCodes.Bas,
            [
                new("name", "客户名称", true, ImportColumnDataTypes.Text, 128, ExampleValue: "成都示例商贸有限公司"),
                new("uscc", "统一社会信用代码", true, ImportColumnDataTypes.Text, 18, ExampleValue: "91510000MA00000002"),
                new(
                    "customerType", "客户类型", true, ImportColumnDataTypes.Text, 1,
                    ExampleValue: "2", Description: "1=生产厂家 / 2=经销商 / 3=劳务分包商 / 4=服务商 / 5=其他"),
                new("contact", "联系人", false, ImportColumnDataTypes.Text, 64),
                new("phone", "联系电话", false, ImportColumnDataTypes.Text, 32),
                new("paymentTerms", "回款条款", false, ImportColumnDataTypes.Text, 256),
            ]),
        new(
            PersonImport,
            "人员导入",
            QlxModuleCodes.Bas,
            [
                new("name", "姓名", true, ImportColumnDataTypes.Text, 64, ExampleValue: "张三"),
                new(
                    "personType", "人员类型", true, ImportColumnDataTypes.Text, 1,
                    ExampleValue: "1", Description: "1=员工 / 2=劳务人员"),
                new(
                    "idType", "证件类型", true, ImportColumnDataTypes.Text, 1,
                    ExampleValue: "1", Description: "1=身份证 / 2=护照 / 3=其他"),
                new("idNo", "证件号", true, ImportColumnDataTypes.Text, 32, ExampleValue: "510100199001010011"),
                new("mobile", "手机号", true, ImportColumnDataTypes.Text, 16, ExampleValue: "13800000001"),
                new("orgId", "所属组织Id", false, ImportColumnDataTypes.Number, Description: "员工必填，劳务人员可空"),
                new("positionId", "所属岗位Id", false, ImportColumnDataTypes.Number, Description: "员工必填，劳务人员可空"),
                new("entryDate", "入职日期", false, ImportColumnDataTypes.Date, ExampleValue: "2026-01-01"),
                new("bankCardNo", "银行卡号", false, ImportColumnDataTypes.Text, 32),
            ]),
        new(
            CategoryImport,
            "商品分类导入",
            QlxModuleCodes.Bas,
            [
                new("name", "分类名称", true, ImportColumnDataTypes.Text, 64, ExampleValue: "办公用品"),
                new("parentCode", "上级分类编码", false, ImportColumnDataTypes.Text, 20, Description: "留空表示顶级分类"),
                new("unitGroupId", "默认计量单位组Id", false, ImportColumnDataTypes.Number),
            ]),

        // ======== 导出模板（含编码列）========
        new(
            SupplierExport,
            "供应商导出",
            QlxModuleCodes.Bas,
            [
                new("code", "供应商编码", true, ImportColumnDataTypes.Text),
                new("name", "供应商名称", true, ImportColumnDataTypes.Text),
                new("uscc", "统一社会信用代码", true, ImportColumnDataTypes.Text),
                new("supplierType", "供应商类型", true, ImportColumnDataTypes.Text),
                new("contact", "联系人", false, ImportColumnDataTypes.Text),
                new("phone", "联系电话", false, ImportColumnDataTypes.Text),
                new("grade", "评级", true, ImportColumnDataTypes.Text),
                new("status", "合作状态", true, ImportColumnDataTypes.Text),
            ]),
        new(
            CustomerExport,
            "客户导出",
            QlxModuleCodes.Bas,
            [
                new("code", "客户编码", true, ImportColumnDataTypes.Text),
                new("name", "客户名称", true, ImportColumnDataTypes.Text),
                new("uscc", "统一社会信用代码", true, ImportColumnDataTypes.Text),
                new("customerType", "客户类型", true, ImportColumnDataTypes.Text),
                new("contact", "联系人", false, ImportColumnDataTypes.Text),
                new("phone", "联系电话", false, ImportColumnDataTypes.Text),
                new("creditGrade", "信用等级", true, ImportColumnDataTypes.Text),
                new("creditLimit", "信用额度", true, ImportColumnDataTypes.Number),
                new("creditUsed", "已用额度", true, ImportColumnDataTypes.Number),
                new("paymentTerms", "回款条款", false, ImportColumnDataTypes.Text),
                new("status", "合作状态", true, ImportColumnDataTypes.Text),
            ]),
        new(
            PersonExport,
            "人员导出",
            QlxModuleCodes.Bas,
            [
                new("code", "人员编码", true, ImportColumnDataTypes.Text),
                new("name", "姓名", true, ImportColumnDataTypes.Text),
                new("personType", "人员类型", true, ImportColumnDataTypes.Text),
                new("idType", "证件类型", true, ImportColumnDataTypes.Text),
                new("idNoMasked", "证件号（脱敏）", true, ImportColumnDataTypes.Text),
                new("mobileMasked", "手机号（脱敏）", true, ImportColumnDataTypes.Text),
                new("orgId", "所属组织Id", false, ImportColumnDataTypes.Text),
                new("positionId", "所属岗位Id", false, ImportColumnDataTypes.Text),
                new("employStatus", "在职状态", true, ImportColumnDataTypes.Text),
                new("entryDate", "入职日期", true, ImportColumnDataTypes.Date),
                new("leaveDate", "离职日期", false, ImportColumnDataTypes.Date),
            ]),
        new(
            CategoryExport,
            "商品分类导出",
            QlxModuleCodes.Bas,
            [
                new("code", "分类编码", true, ImportColumnDataTypes.Text),
                new("name", "分类名称", true, ImportColumnDataTypes.Text),
                new("parentCode", "上级分类编码", false, ImportColumnDataTypes.Text),
                new("levelNo", "分类层级", true, ImportColumnDataTypes.Number),
                new("unitGroupId", "默认计量单位组Id", false, ImportColumnDataTypes.Text),
                new("status", "启停状态", true, ImportColumnDataTypes.Text),
            ]),
    ];

    /// <inheritdoc />
    public IReadOnlyList<ImportTemplateDefinition> GetDefinitions() => s_definitions;
}
