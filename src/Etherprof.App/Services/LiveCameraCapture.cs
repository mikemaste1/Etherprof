using System.IO;
using FlashCap;

namespace Etherprof.App.Services;

/// <summary>
/// Lightweight service for live webcam video capture using FlashCap.
/// Delivers extracted frame bytes for live preview rendering and on-the-fly QR decoding.
/// </summary>
public sealed class LiveCameraCapture : IAsyncDisposable
{
    private CaptureDevice? _device;
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;

    public event Action<byte[]>? FrameArrived;
    public event Action<string>? ErrorOccurred;

    public static List<string> GetAvailableDeviceNames()
    {
        try
        {
            var devices = new CaptureDevices();
            return devices.EnumerateDescriptors().Select(d => d.Name).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<bool> StartAsync(int deviceIndex = 0)
    {
        if (_isRunning) return true;

        try
        {
            var devices = new CaptureDevices();
            var descriptors = devices.EnumerateDescriptors().ToList();
            if (descriptors.Count == 0)
            {
                ErrorOccurred?.Invoke("No camera devices detected on system.");
                return false;
            }

            int index = Math.Clamp(deviceIndex, 0, descriptors.Count - 1);
            var descriptor = descriptors[index];

            // Pick a moderate resolution (around 640x480 or 1280x720) for optimal QR detection speed and low CPU
            var characteristic = descriptor.Characteristics
                .OrderBy(c => Math.Abs(c.Width - 640))
                .ThenBy(c => Math.Abs(c.Height - 480))
                .FirstOrDefault() ?? descriptor.Characteristics.FirstOrDefault();

            if (characteristic == null)
            {
                ErrorOccurred?.Invoke("No supported video format for selected camera.");
                return false;
            }

            _device = await descriptor.OpenAsync(
                characteristic,
                bufferScope =>
                {
                    if (!_isRunning) return;
                    try
                    {
                        byte[] imageBytes = bufferScope.Buffer.ExtractImage();
                        FrameArrived?.Invoke(imageBytes);
                    }
                    catch
                    {
                        // drop frame on transient error
                    }
                });

            await _device.StartAsync();
            _isRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to open camera: {ex.Message}");
            await StopAsync();
            return false;
        }
    }

    public async Task StopAsync()
    {
        _isRunning = false;
        if (_device != null)
        {
            try
            {
                await _device.StopAsync();
                _device.Dispose();
            }
            catch { }
            _device = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
