namespace Etherprof.Wifi.Windows.Interop;

using System.Runtime.InteropServices;

public static class NativeWifiMethods
{
    private const string WlanApiDll = "wlanapi.dll";

    public delegate void WlanNotificationCallback(ref WLAN_NOTIFICATION_DATA notificationData, IntPtr context);

    [DllImport(WlanApiDll, EntryPoint = "WlanOpenHandle", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint WlanOpenHandle(
        uint dwClientVersion,
        IntPtr pReserved,
        out uint pdwNegotiatedVersion,
        out WlanSafeHandle phClientHandle);

    [DllImport(WlanApiDll, EntryPoint = "WlanCloseHandle", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint WlanCloseHandle(
        IntPtr hClientHandle,
        IntPtr pReserved);

    [DllImport(WlanApiDll, EntryPoint = "WlanEnumInterfaces", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint WlanEnumInterfaces(
        WlanSafeHandle hClientHandle,
        IntPtr pReserved,
        out IntPtr ppInterfaceList);

    [DllImport(WlanApiDll, EntryPoint = "WlanQueryInterface", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint WlanQueryInterface(
        WlanSafeHandle hClientHandle,
        [In] ref Guid pInterfaceGuid,
        WLAN_INTF_OPCODE OpCode,
        IntPtr pReserved,
        out uint pdwDataSize,
        out IntPtr ppData,
        out WLAN_OPCODE_VALUE_TYPE pWlanOpcodeValueType);

    [DllImport(WlanApiDll, EntryPoint = "WlanRegisterNotification", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint WlanRegisterNotification(
        WlanSafeHandle hClientHandle,
        WLAN_NOTIFICATION_SOURCE dwNotifSource,
        [MarshalAs(UnmanagedType.Bool)] bool bIgnoreDuplicate,
        WlanNotificationCallback? funcCallback,
        IntPtr pCallbackContext,
        IntPtr pReserved,
        out WLAN_NOTIFICATION_SOURCE pdwPrevNotifSource);

    [DllImport(WlanApiDll, EntryPoint = "WlanFreeMemory", SetLastError = true)]
    public static extern void WlanFreeMemory(IntPtr pMemory);

    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_ACCESS_DENIED = 5;
    public const uint ERROR_GEN_FAILURE = 31;
    public const uint ERROR_INVALID_PARAMETER = 87;
}
