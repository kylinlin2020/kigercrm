namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Lifecycle of a persisted Super Search mission. A mission is Planned until its Run is created.</summary>
public enum CrmProspectingSuperSearchMissionStatus
{
    /// <summary>The frozen plan is saved but no Run has been created from it.</summary>
    Planned = 0,

    /// <summary>A Run was created from this mission; RunId is the durable link.</summary>
    Executed = 1,
}
