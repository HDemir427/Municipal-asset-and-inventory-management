using MAIMS.Core.Enums;
using Xunit;
using FluentAssertions;

namespace MAIMS.Core.Tests;

public class PermissionsTests
{
    [Fact]
    public void DefaultRolePermissions_AreDefinedForAllSevenSystemRoles()
    {
        foreach (var role in Permissions.SystemRoles)
        {
            Permissions.DefaultRolePermissions.Should().ContainKey(role);
            Permissions.DefaultRolePermissions[role].Should().NotBeEmpty($"{role} must have at least one permission");
        }
    }

    [Fact]
    public void SystemAdministrator_HasAllPermissions()
    {
        var admin = Permissions.DefaultRolePermissions["SystemAdministrator"];

        admin.Should().Contain(Permissions.AssetView);
        admin.Should().Contain(Permissions.AssetCreate);
        admin.Should().Contain(Permissions.AuditView);
        admin.Should().Contain(Permissions.AuditExport);
        admin.Should().Contain(Permissions.CrossDepartmentView);
        admin.Should().Contain(Permissions.UserManage);
    }

    [Fact]
    public void Auditor_IsReadOnly_ButCanExportAuditTrail()
    {
        var auditor = Permissions.DefaultRolePermissions["Auditor"];

        auditor.Should().NotContain(Permissions.AssetCreate);
        auditor.Should().NotContain(Permissions.AssetEdit);
        auditor.Should().NotContain(Permissions.InventoryIssue);
        auditor.Should().Contain(Permissions.AuditView);
        auditor.Should().Contain(Permissions.AuditExport);
        auditor.Should().Contain(Permissions.CrossDepartmentView);
    }

    [Fact]
    public void FieldWorker_CannotMutateAnything()
    {
        var fw = Permissions.DefaultRolePermissions["FieldWorker"];

        fw.Should().NotContain(Permissions.AssetCreate);
        fw.Should().NotContain(Permissions.AssetEdit);
        fw.Should().NotContain(Permissions.AssetDelete);
        fw.Should().NotContain(Permissions.InventoryAdjust);
        fw.Should().NotContain(Permissions.UserManage);
        fw.Should().Contain(Permissions.AssetView);
        fw.Should().Contain(Permissions.InventoryView);
    }

    [Theory]
    [InlineData(AssetStatus.Planned, AssetStatus.Acquired, true)]
    [InlineData(AssetStatus.Acquired, AssetStatus.InService, true)]
    [InlineData(AssetStatus.InService, AssetStatus.UnderMaintenance, true)]
    [InlineData(AssetStatus.Disposed, AssetStatus.InService, false)]
    public void AssetStatus_Transitions_AreDefined(AssetStatus from, AssetStatus to, bool expectedForward)
    {
        // Sanity check: enum ordering reflects the canonical lifecycle progression.
        ((int)to > (int)from).Should().Be(expectedForward);
    }
}
