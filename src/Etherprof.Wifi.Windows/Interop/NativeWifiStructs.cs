namespace Etherprof.Wifi.Windows.Interop;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WLAN_INTERFACE_INFO
{
    public Guid InterfaceGuid;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string strInterfaceDescription;
    public WLAN_INTERFACE_STATE isState;
}

[StructLayout(LayoutKind.Sequential)]
public struct WLAN_INTERFACE_INFO_LIST
{
    public uint dwNumberOfItems;
    public uint dwIndex;
    // Followed by dwNumberOfItems WLAN_INTERFACE_INFO items in memory
}

[StructLayout(LayoutKind.Sequential)]
public struct DOT11_SSID
{
    public uint uSSIDLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] ucSSID;

    public readonly override string ToString()
    {
        if (uSSIDLength == 0 || ucSSID == null)
            return "";
        int len = (int)Math.Min(uSSIDLength, ucSSID.Length);
        return System.Text.Encoding.UTF8.GetString(ucSSID, 0, len);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct WLAN_ASSOCIATION_ATTRIBUTES
{
    public DOT11_SSID dot11Ssid;
    public uint dot11BssType;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] dot11Bssid;
    public DOT11_PHY_TYPE dot11PhyType;
    public uint uDot11PhyIndex;
    public uint wlanSignalQuality; // 0 - 100 %
    public uint ulRxRate;
    public uint ulTxRate;
}

[StructLayout(LayoutKind.Sequential)]
public struct WLAN_SECURITY_ATTRIBUTES
{
    [MarshalAs(UnmanagedType.Bool)]
    public bool bSecurityEnabled;
    [MarshalAs(UnmanagedType.Bool)]
    public bool bOneXEnabled;
    public uint dot11AuthAlgorithm;
    public uint dot11CipherAlgorithm;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WLAN_CONNECTION_ATTRIBUTES
{
    public WLAN_INTERFACE_STATE isState;
    public uint wlanConnectionMode;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string strProfileName;
    public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
    public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
}

[StructLayout(LayoutKind.Sequential)]
public struct WLAN_NOTIFICATION_DATA
{
    public uint NotificationSource;
    public uint NotificationCode;
    public Guid InterfaceGuid;
    public uint dwDataSize;
    public IntPtr pData;
}
