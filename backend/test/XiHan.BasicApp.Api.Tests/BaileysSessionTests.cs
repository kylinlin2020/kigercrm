using System.Runtime.CompilerServices;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Baileys session contract: bounded state machine, terminal device logout, no protocol client.</summary>
public sealed class BaileysSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Policy_ShouldEnforceQrExpiryAndTerminalRevocation()
    {
        var issued = Now.AddMinutes(-2);
        Assert.True(BaileysSessionPolicy.CanTransition(
            BaileysSessionStatus.Disconnected, BaileysSessionStatus.QrPending, Now, null).Allowed);
        Assert.True(BaileysSessionPolicy.CanTransition(
            BaileysSessionStatus.QrPending, BaileysSessionStatus.Connected, Now, issued).Allowed);
        var expired = BaileysSessionPolicy.CanTransition(
            BaileysSessionStatus.QrPending, BaileysSessionStatus.Connected, Now, Now.AddMinutes(-6));
        Assert.False(expired.Allowed);
        Assert.Equal("qr_expired", expired.Reason);
        Assert.Equal("device_logout", BaileysSessionPolicy.CanTransition(
            BaileysSessionStatus.Connected, BaileysSessionStatus.Revoked, Now, issued).Reason);
        Assert.False(BaileysSessionPolicy.CanTransition(
            BaileysSessionStatus.Revoked, BaileysSessionStatus.Disconnected, Now, issued).Allowed);
    }

    [Fact]
    public void Policy_ShouldBoundReconnectBackoffAndClassifyHealth()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), BaileysSessionPolicy.ReconnectDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(30), BaileysSessionPolicy.ReconnectDelay(99));
        Assert.Equal(BaileysHealthState.Unknown, BaileysSessionPolicy.ClassifyHealth(null, Now));
        Assert.Equal(BaileysHealthState.Healthy, BaileysSessionPolicy.ClassifyHealth(Now.AddMinutes(-2), Now));
        Assert.Equal(BaileysHealthState.Degraded, BaileysSessionPolicy.ClassifyHealth(Now.AddMinutes(-20), Now));
        Assert.Equal(BaileysHealthState.Offline, BaileysSessionPolicy.ClassifyHealth(Now.AddMinutes(-40), Now));
    }

    [Fact]
    public void Contract_ShouldKeepProtocolClientOutOfTheApplicationLayer()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Application", "AppServices", "BaileysSessionAppService.cs");
        var policy = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Domain", "Prospecting", "BaileysSessionPolicy.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.50", "5.9.50.sql");
        Assert.Contains("[PermissionAuthorize(ChatPermissionCodes.Manage)]", app, StringComparison.Ordinal);
        Assert.DoesNotContain("WhatsMeow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("BaileysClient", app, StringComparison.Ordinal);
        Assert.DoesNotContain("QrPayload", app, StringComparison.Ordinal);
        Assert.Contains("revoked_is_terminal", policy, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS sys_baileys_session", migration, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
