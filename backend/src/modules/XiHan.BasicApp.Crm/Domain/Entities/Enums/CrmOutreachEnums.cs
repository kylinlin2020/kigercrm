namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

public enum CrmOutreachSequenceStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Archived = 3,
}

public enum CrmOutreachEnrollmentStatus
{
    Active = 0,
    Paused = 1,
    Completed = 2,
    Suppressed = 3,
}

public enum CrmOutreachTouchpointState
{
    Planned = 0,
    Approved = 1,
    Sent = 2,
    Replied = 3,
    Bounced = 4,
    Suppressed = 5,
}

public enum CrmOutreachChannel
{
    Email = 0,
    WhatsApp = 1,
    WebsiteComment = 2,
    ManualCall = 3,
}

public enum CrmOutreachReplyClass
{
    None = 0,
    Interested = 1,
    NotInterested = 2,
    OutOfOffice = 3,
    Unknown = 4,
}
