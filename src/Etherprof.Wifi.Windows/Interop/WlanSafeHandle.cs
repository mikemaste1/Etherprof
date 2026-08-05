namespace Etherprof.Wifi.Windows.Interop;

using Microsoft.Win32.SafeHandles;

public sealed class WlanSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public WlanSafeHandle() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            var result = NativeWifiMethods.WlanCloseHandle(handle, IntPtr.Zero);
            return result == 0;
        }
        return true;
    }
}
