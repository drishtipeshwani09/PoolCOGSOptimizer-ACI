namespace PoolCOGSOptimizer;

/// <summary>
/// Represents the available tenant/region names for ACI pools
/// </summary>
public enum TenantName
{
    None,
    CBN,
    CDM,
    MXC,
    AM,
    ILC,
    BL,
    CDN
}

/// <summary>
/// Represents the available pool names for ACI clusters
/// </summary>
public enum PoolName
{
    None,
    ACI,
    ACIBYOVNET,
    ACIBYOVNET2,
    ACI_Zone1,
    ACI_Zone2,
    ACI_Zone3,
    ACIBYOVNETSingleTenant,
    ACIBYOVNET_Zone1,
    ACIBYOVNET_Zone2,
    ACIBYOVNET_Zone3,
    ACIBYOVNET2_Zone1,
    ACIBYOVNET2_Zone2,
    ACIBYOVNET2_Zone3,
}