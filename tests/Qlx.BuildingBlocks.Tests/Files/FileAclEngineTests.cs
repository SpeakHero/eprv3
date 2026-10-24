/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 03:45:00 +08:00 中国标准时间 星期五
 * 描述：文件 ACL 计算引擎单元测试（规则 R5：所有者/管理员恒 All → DENY 优先 → ALLOW 并集 → 默认拒绝 + 继承链）
 */

using Qlx.Abstractions.Files;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Files;

namespace Qlx.BuildingBlocks.Tests.Files;

public sealed class FileAclEngineTests
{
    private const long TargetId = 100L;
    private const long ParentId = 10L;
    private const long GrandparentId = 1L;
    private const string Owner = "owner-1";
    private const string User = "user-9";

    private static readonly TenantSubjects PlainUser = new([], [], [], IsTenantAdmin: false);
    private static readonly TenantSubjects AdminUser = new([], [], [], IsTenantAdmin: true);

    private static AclEngineEntry Entry(
        long objectId,
        FileSubjectType subjectType,
        string subjectId,
        FilePermission permission,
        AceType aceType = AceType.Allow,
        bool propagate = true)
        => new(objectId, $"/p/{objectId}", subjectType, subjectId, permission, aceType, propagate);

    [Fact]
    public void OwnerAlwaysGetsAll()
    {
        var result = FileAclEngine.ComputeEffective([], TargetId, Owner, PlainUser, Owner, isTenantAdmin: false, inheritsFromParent: true);
        Assert.Equal(FilePermission.All, result);
    }

    [Fact]
    public void TenantAdminAlwaysGetsAll()
    {
        var result = FileAclEngine.ComputeEffective([], TargetId, Owner, PlainUser, User, isTenantAdmin: true, inheritsFromParent: true);
        Assert.Equal(FilePermission.All, result);
    }

    [Fact]
    public void NoEntriesDeniesByDefault()
    {
        var result = FileAclEngine.ComputeEffective([], TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: true);
        Assert.Equal(FilePermission.None, result);
    }

    [Fact]
    public void WildcardAllowGrantsRead()
    {
        var entries = new[] { Entry(TargetId, FileSubjectType.User, FileAclEngine.WildcardSubjectId, FilePermission.Read) };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: false);
        Assert.Equal(FilePermission.Read, result);
    }

    [Fact]
    public void AllowUnionsAcrossEntries()
    {
        var entries = new[]
        {
            Entry(TargetId, FileSubjectType.User, User, FilePermission.Read),
            Entry(TargetId, FileSubjectType.User, User, FilePermission.Write),
        };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: false);
        Assert.Equal(FilePermission.Read | FilePermission.Write, result);
    }

    [Fact]
    public void DenyOverridesAllow()
    {
        var entries = new[]
        {
            Entry(TargetId, FileSubjectType.User, User, FilePermission.All),
            Entry(TargetId, FileSubjectType.User, User, FilePermission.Write | FilePermission.Delete, AceType.Deny),
        };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: false);
        Assert.Equal(FilePermission.Read | FilePermission.Manage, result);
    }

    [Fact]
    public void UserRoleSubjectMatchesById()
    {
        var subjects = new TenantSubjects([], ["42"], [], IsTenantAdmin: false);
        var entries = new[] { Entry(TargetId, FileSubjectType.Role, "42", FilePermission.Read) };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, subjects, User, isTenantAdmin: false, inheritsFromParent: false);
        Assert.Equal(FilePermission.Read, result);
    }

    [Fact]
    public void OrgSubjectMatchesById()
    {
        var subjects = new TenantSubjects([], [], ["7"], IsTenantAdmin: false);
        var entries = new[] { Entry(TargetId, FileSubjectType.Org, "7", FilePermission.Read) };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, subjects, User, isTenantAdmin: false, inheritsFromParent: false);
        Assert.Equal(FilePermission.Read, result);
    }

    [Fact]
    public void NonMatchingUserDenied()
    {
        var entries = new[] { Entry(TargetId, FileSubjectType.User, "someone-else", FilePermission.Read) };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: false);
        Assert.Equal(FilePermission.None, result);
    }

    [Fact]
    public void ParentPropagatedEntryAppliesWhenInheriting()
    {
        var entries = new[] { Entry(ParentId, FileSubjectType.User, User, FilePermission.Read) };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: true);
        Assert.Equal(FilePermission.Read, result);
    }

    [Fact]
    public void ParentEntryIgnoredWhenInheritDisabled()
    {
        var entries = new[] { Entry(ParentId, FileSubjectType.User, User, FilePermission.Read) };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: false);
        Assert.Equal(FilePermission.None, result);
    }

    [Fact]
    public void ParentNonPropagatedEntryIgnoredEvenWhenInheriting()
    {
        var entries = new[] { Entry(ParentId, FileSubjectType.User, User, FilePermission.Read, propagate: false) };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: true);
        Assert.Equal(FilePermission.None, result);
    }

    [Fact]
    public void InheritedDenyFromGrandparentStillWins()
    {
        var entries = new[]
        {
            Entry(GrandparentId, FileSubjectType.User, User, FilePermission.Write, AceType.Deny),
            Entry(TargetId, FileSubjectType.User, User, FilePermission.All),
        };
        var result = FileAclEngine.ComputeEffective(entries, TargetId, Owner, PlainUser, User, isTenantAdmin: false, inheritsFromParent: true);
        Assert.Equal(FilePermission.Read | FilePermission.Delete | FilePermission.Manage, result);
    }
}
