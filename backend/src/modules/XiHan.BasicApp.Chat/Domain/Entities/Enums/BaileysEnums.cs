namespace XiHan.BasicApp.Chat.Domain.Entities.Enums;

public enum BaileysSessionStatus
{
    Disconnected = 0,
    QrPending = 1,
    Connected = 2,
    Revoked = 3,
}

public enum BaileysHealthState
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Offline = 3,
}
