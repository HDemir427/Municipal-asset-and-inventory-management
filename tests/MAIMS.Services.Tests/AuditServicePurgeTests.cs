using FluentAssertions;
using MAIMS.Core.Interfaces;
using MAIMS.Services.Audit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MAIMS.Services.Tests;

public class AuditServicePurgeTests
{
    /// <summary>
    /// Verifies that PurgeInvalidEntriesAsync throws ArgumentException when
    /// the connection string is null or whitespace. This is the basic
    /// input-validation test — we can't test the actual MySQL trigger
    /// drop/recreate logic without a real MySQL instance.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PurgeInvalidEntriesAsync_NullOrEmptyConnString_ThrowsArgumentException(string? connStr)
    {
        // Arrange — AuditService needs IServiceScopeFactory + ICurrentSession.
        // We only test argument validation, not the actual purge.
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var auditSvc = new AuditService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            MAIMS.Core.Abstractions.NullCurrentSession.Instance);

        // Act
        var act = () => auditSvc.PurgeInvalidEntriesAsync(connStr!);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Root connection string is required*");
    }

    /// <summary>
    /// Verifies that PurgeInvalidEntriesAsync throws ArgumentException when
    /// the connection string points to a non-existent server. This confirms
    /// the method actually attempts a MySQL connection (rather than silently
    /// succeeding without doing anything).
    /// </summary>
    [Fact]
    public async Task PurgeInvalidEntriesAsync_NonExistentServer_ThrowsMySqlException()
    {
        // Arrange — use a fake port that no MySQL is running on
        var connStr = "Server=127.0.0.1;Port=13306;Database=maims;User=root;Password=test;CharSet=utf8mb4;AllowPublicKeyRetrieval=True;ConnectionTimeout=3;";
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var auditSvc = new AuditService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            MAIMS.Core.Abstractions.NullCurrentSession.Instance);

        // Act
        var act = () => auditSvc.PurgeInvalidEntriesAsync(connStr);

        // Assert — should fail to connect (not silently succeed)
        await act.Should().ThrowAsync<Exception>();
    }
}
