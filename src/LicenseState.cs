#if DISTRIBUTION
namespace ESPWorkbench
{
    internal enum LicenseState
    {
        NotActivated, Checking, Active, Expired, Revoked, DeviceRemoved,
        UpdateRequired, NetworkLost, Maintenance
    }
}
#endif
