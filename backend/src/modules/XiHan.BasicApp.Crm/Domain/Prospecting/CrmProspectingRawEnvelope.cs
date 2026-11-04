namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Provider result retained with its source adapter identity.</summary>
public sealed record CrmProspectingRawEnvelope(string ProviderCode, CrmProspectingRawRecord Record);
