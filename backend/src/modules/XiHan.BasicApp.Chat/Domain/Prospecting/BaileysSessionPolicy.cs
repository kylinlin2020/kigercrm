using XiHan.BasicApp.Chat.Domain.Entities.Enums;

namespace XiHan.BasicApp.Chat.Domain.Prospecting;

/// <summary>
/// Deterministic Baileys session state machine. The real protocol client is intentionally absent:
/// an operator explicitly confirms QR issuance/connection, and a revoked (device-logout) session
/// is terminal so it can never be silently resumed.
/// </summary>
public static class BaileysSessionPolicy
{
    public static readonly TimeSpan QrTimeToLive = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DegradedAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(30);

    public static (bool Allowed, string Reason) CanTransition(
        BaileysSessionStatus from,
        BaileysSessionStatus to,
        DateTimeOffset nowUtc,
        DateTimeOffset? qrIssuedAtUtc)
    {
        if (from == BaileysSessionStatus.Revoked)
            return (false, "revoked_is_terminal");
        if (from == to)
            return (false, "same_state");

        if (to == BaileysSessionStatus.Revoked)
            return (true, "device_logout");
        if (from == BaileysSessionStatus.Disconnected && to == BaileysSessionStatus.QrPending)
            return (true, "qr_issued");
        if (from == BaileysSessionStatus.QrPending && to == BaileysSessionStatus.Disconnected)
            return (true, "qr_expired_or_cancelled");
        if (from == BaileysSessionStatus.QrPending && to == BaileysSessionStatus.Connected)
        {
            if (qrIssuedAtUtc is null) return (false, "qr_not_issued");
            if (nowUtc - qrIssuedAtUtc.Value > QrTimeToLive) return (false, "qr_expired");
            return (true, "operator_confirmed");
        }
        if (from == BaileysSessionStatus.Connected && to == BaileysSessionStatus.Disconnected)
            return (true, "disconnected");
        return (false, "invalid_transition");
    }

    public static TimeSpan ReconnectDelay(int attempt)
    {
        if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt));
        var seconds = Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)));
        return TimeSpan.FromSeconds(seconds);
    }

    public static BaileysHealthState ClassifyHealth(DateTimeOffset? lastConnectedAtUtc, DateTimeOffset nowUtc)
    {
        if (lastConnectedAtUtc is null) return BaileysHealthState.Unknown;
        var age = nowUtc - lastConnectedAtUtc.Value;
        if (age < TimeSpan.Zero) return BaileysHealthState.Unknown;
        if (age > OfflineAfter) return BaileysHealthState.Offline;
        if (age > DegradedAfter) return BaileysHealthState.Degraded;
        return BaileysHealthState.Healthy;
    }
}
