using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "SmartEnum 是行业通用命名模式，与 System.Enum 语义不同；Abstractions 层约定业务枚举统一继承 SmartEnum<TSelf>")]
[assembly: SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "ApiResponse<T> 的 Ok/Fail 工厂方法是泛型类型上的行业通用做法；此处禁用")]

// Portal 模块的种子服务需要手动调用 Entity.AssignId 分配雪花 Id
[assembly: InternalsVisibleTo("Qlx.Modules.Portal")]
// 单元测试需要构造带 Id 的实体以驱动服务层逻辑
[assembly: InternalsVisibleTo("Qlx.BuildingBlocks.Tests")]
