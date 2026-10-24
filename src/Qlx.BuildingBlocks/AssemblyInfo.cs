using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "ApiResponse<T> 的 Ok/Fail 工厂方法是泛型类型上的行业通用做法；此处禁用")]
[assembly: InternalsVisibleTo("Qlx.BuildingBlocks.Tests")]
[assembly: InternalsVisibleTo("Qlx.ArchitectureTests")]
