using System.Runtime.CompilerServices;

// 单元测试需要按种子服务同样方式组装租户聚合（AddModule / SetDatabase）以驱动服务层逻辑
[assembly: InternalsVisibleTo("Qlx.BuildingBlocks.Tests")]
