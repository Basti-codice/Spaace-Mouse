namespace SpaceMouse.Core.Hid;

/// <summary>
/// Known 3Dconnexion / Logitech 3D-mouse USB ids. Source: spacenavd/libspnav device tables and public USB id lists.
/// The list is advisory: the app additionally accepts ANY device with HID usage page 0x01 / usage 0x08 (Multi-axis Controller).
/// </summary>
public static class KnownDevices
{
    public const ushort Vendor3Dx = 0x256F;
    public const ushort VendorLogitech = 0x046D;
    public const ushort UsagePageGenericDesktop = 0x01;
    public const ushort UsageMultiAxis = 0x08;

    private static readonly Dictionary<(ushort, ushort), string> Table = new()
    {
        [(VendorLogitech, 0xC603)] = "SpaceMouse Plus XT",
        [(VendorLogitech, 0xC605)] = "CADman",
        [(VendorLogitech, 0xC606)] = "SpaceMouse Classic",
        [(VendorLogitech, 0xC623)] = "SpaceTraveler",
        [(VendorLogitech, 0xC625)] = "SpacePilot",
        [(VendorLogitech, 0xC626)] = "SpaceNavigator",
        [(VendorLogitech, 0xC627)] = "SpaceExplorer",
        [(VendorLogitech, 0xC628)] = "SpaceNavigator for Notebooks",
        [(VendorLogitech, 0xC629)] = "SpacePilot Pro",
        [(VendorLogitech, 0xC62B)] = "SpaceMouse Pro",
        [(Vendor3Dx, 0xC62E)] = "SpaceMouse Wireless (cabled)",
        [(Vendor3Dx, 0xC62F)] = "SpaceMouse Wireless Receiver",
        [(Vendor3Dx, 0xC631)] = "SpaceMouse Pro Wireless (cabled)",
        [(Vendor3Dx, 0xC632)] = "SpaceMouse Pro Wireless Receiver",
        [(Vendor3Dx, 0xC633)] = "SpaceMouse Enterprise",
        [(Vendor3Dx, 0xC635)] = "SpaceMouse Compact",
        [(Vendor3Dx, 0xC636)] = "SpaceMouse Module",
    };

    public static bool IsKnown(ushort vid, ushort pid) => Table.ContainsKey((vid, pid));

    public static string NameOf(ushort vid, ushort pid) =>
        Table.TryGetValue((vid, pid), out var n) ? n : vid == Vendor3Dx || vid == VendorLogitech ? "3Dconnexion device" : "Multi-axis controller";

    public static bool IsCompact(ushort vid, ushort pid) => vid == Vendor3Dx && pid == 0xC635;
}
