using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Etherprof.App.Services;
using Etherprof.Core;
using ZXing;
using ZXing.Windows.Compatibility;

namespace Etherprof.App.ViewModels;

/// <summary>
/// ViewModel for QR code scanning, supporting:
/// 1. Detached live video bar with real-time on-the-fly QR decoding
/// 2. Pasting images directly from clipboard (screenshots / copied images / image files)
/// 3. Manual camera snapshot capture
/// 4. Opening image files from disk
/// 5. Automatic clipboard copying of QR payloads and Wi-Fi credentials
/// 6. Direct ingestion into Saved Wi-Fi Profiles
/// </summary>
public sealed class QrScanPanelViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly LiveCameraCapture _liveCamera = new();
    private readonly BarcodeReader _barcodeReader;
    private int _isDecodingFlag;
    private string? _lastDecodedCode;
    private DateTimeOffset _lastDecodeTime = DateTimeOffset.MinValue;

    // ----- Status & Display Properties -----

    private string _statusMessage = "Ready — start live scan or paste image from clipboard";
    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    private string? _liveDecodeNotification;
    public string? LiveDecodeNotification
    {
        get => _liveDecodeNotification;
        set
        {
            if (_liveDecodeNotification != value)
            {
                _liveDecodeNotification = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLiveDecodeNotification));
            }
        }
    }

    public bool HasLiveDecodeNotification => !string.IsNullOrEmpty(LiveDecodeNotification);

    private bool _isLiveScanning;
    public bool IsLiveScanning
    {
        get => _isLiveScanning;
        set
        {
            if (_isLiveScanning != value)
            {
                _isLiveScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LiveScanButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string LiveScanButtonText => IsLiveScanning ? "⏹ Stop Live Scan" : "🎥 Live Video Bar";

    private bool _isDetachedBarOpen;
    public bool IsDetachedBarOpen
    {
        get => _isDetachedBarOpen;
        set
        {
            if (_isDetachedBarOpen != value)
            {
                _isDetachedBarOpen = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isDetachedBarTopmost = true;
    public bool IsDetachedBarTopmost
    {
        get => _isDetachedBarTopmost;
        set
        {
            if (_isDetachedBarTopmost != value)
            {
                _isDetachedBarTopmost = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (_isCapturing != value)
            {
                _isCapturing = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private BitmapSource? _capturedImage;
    public BitmapSource? CapturedImage
    {
        get => _capturedImage;
        set
        {
            if (!ReferenceEquals(_capturedImage, value))
            {
                _capturedImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCapturedImage));
            }
        }
    }

    public bool HasCapturedImage => CapturedImage != null;

    private BitmapSource? _liveVideoFrame;
    public BitmapSource? LiveVideoFrame
    {
        get => _liveVideoFrame;
        set
        {
            if (!ReferenceEquals(_liveVideoFrame, value))
            {
                _liveVideoFrame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLiveVideoFrame));
            }
        }
    }

    public bool HasLiveVideoFrame => LiveVideoFrame != null;

    private string _decodedText = string.Empty;
    public string DecodedText
    {
        get => _decodedText;
        set
        {
            if (_decodedText != value)
            {
                _decodedText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDecodedText));
                UpdateWifiInfo();
            }
        }
    }

    public bool HasDecodedText => !string.IsNullOrWhiteSpace(DecodedText);

    // ----- Wi-Fi Payload Details -----

    private WifiQrInfo? _wifiInfo;
    public WifiQrInfo? WifiInfo
    {
        get => _wifiInfo;
        private set
        {
            if (_wifiInfo != value)
            {
                _wifiInfo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWifiPayload));
                OnPropertyChanged(nameof(ParsedSsid));
                OnPropertyChanged(nameof(ParsedPassword));
                OnPropertyChanged(nameof(HasPassword));
                OnPropertyChanged(nameof(ParsedAuthType));
                OnPropertyChanged(nameof(IsHidden));
                OnPropertyChanged(nameof(DisplayPassword));
            }
        }
    }

    public bool IsWifiPayload => WifiInfo != null;
    public string? ParsedSsid => WifiInfo?.Ssid;
    public string? ParsedPassword => WifiInfo?.Password;
    public bool HasPassword => !string.IsNullOrEmpty(ParsedPassword);
    public string ParsedAuthType => WifiInfo?.AuthType ?? "WPA";
    public bool IsHidden => WifiInfo?.IsHidden ?? false;

    private bool _isPasswordRevealed;
    public bool IsPasswordRevealed
    {
        get => _isPasswordRevealed;
        set
        {
            if (_isPasswordRevealed != value)
            {
                _isPasswordRevealed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayPassword));
                OnPropertyChanged(nameof(PasswordToggleIcon));
            }
        }
    }

    public string PasswordToggleIcon => IsPasswordRevealed ? "🙈 Hide" : "👁 Reveal";

    public string DisplayPassword
    {
        get
        {
            if (!HasPassword) return "(Open / No password)";
            if (IsPasswordRevealed) return ParsedPassword!;
            return new string('●', Math.Min(ParsedPassword!.Length, 12));
        }
    }

    private string _lastScanTime = string.Empty;
    public string LastScanTime
    {
        get => _lastScanTime;
        set { if (_lastScanTime != value) { _lastScanTime = value; OnPropertyChanged(); } }
    }

    // ----- Commands -----

    public ICommand ToggleLiveScanCommand { get; }
    public ICommand OpenDetachedBarCommand { get; }
    public ICommand CloseDetachedBarCommand { get; }
    public ICommand PasteImageAndDecodeCommand { get; }
    public ICommand CaptureAndDecodeCommand { get; }
    public ICommand OpenFileAndDecodeCommand { get; }
    public ICommand CopyPayloadCommand { get; }
    public ICommand CopySsidCommand { get; }
    public ICommand CopyPasswordCommand { get; }
    public ICommand TogglePasswordVisibilityCommand { get; }
    public ICommand IngestWifiProfileCommand { get; }
    public ICommand ClearCommand { get; }

    // ----- Callbacks -----

    public Action<string, string>? LogAction { get; set; }
    public Action<string>? OnIngestWifiProfile { get; set; }
    public Action? OpenDetachedBarRequested { get; set; }
    public Action? CloseDetachedBarRequested { get; set; }

    public QrScanPanelViewModel()
    {
        _barcodeReader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };

        _liveCamera.FrameArrived += OnLiveCameraFrameArrived;
        _liveCamera.ErrorOccurred += OnLiveCameraError;

        ToggleLiveScanCommand = new RelayCommand(
            () => _ = ToggleLiveScanAsync(),
            () => !IsCapturing);

        OpenDetachedBarCommand = new RelayCommand(
            () =>
            {
                OpenDetachedBarRequested?.Invoke();
                if (!IsLiveScanning) _ = StartLiveScanAsync();
            });

        CloseDetachedBarCommand = new RelayCommand(
            () =>
            {
                CloseDetachedBarRequested?.Invoke();
                if (IsLiveScanning) _ = StopLiveScanAsync();
            });

        PasteImageAndDecodeCommand = new RelayCommand(
            () => PasteImageAndDecode(),
            () => !IsCapturing);

        CaptureAndDecodeCommand = new RelayCommand(
            () => _ = CaptureAndDecodeAsync(),
            () => !IsCapturing);

        OpenFileAndDecodeCommand = new RelayCommand(
            () => _ = OpenFileAndDecodeAsync(),
            () => !IsCapturing);

        CopyPayloadCommand = new RelayCommand(
            () => CopyToClipboard(DecodedText, "QR Payload"),
            () => HasDecodedText);

        CopySsidCommand = new RelayCommand(
            () => { if (!string.IsNullOrEmpty(ParsedSsid)) CopyToClipboard(ParsedSsid, "SSID"); },
            () => !string.IsNullOrEmpty(ParsedSsid));

        CopyPasswordCommand = new RelayCommand(
            () => { if (!string.IsNullOrEmpty(ParsedPassword)) CopyToClipboard(ParsedPassword, "Wi-Fi Password"); },
            () => HasPassword);

        TogglePasswordVisibilityCommand = new RelayCommand(
            () => IsPasswordRevealed = !IsPasswordRevealed,
            () => HasPassword);

        IngestWifiProfileCommand = new RelayCommand(
            () => IngestProfile(),
            () => IsWifiPayload && !string.IsNullOrEmpty(ParsedSsid));

        ClearCommand = new RelayCommand(
            () => Clear(),
            () => HasCapturedImage || HasLiveVideoFrame || HasDecodedText);
    }

    // ----- Live Camera Streaming & On-the-Fly Decoding -----

    public async Task ToggleLiveScanAsync()
    {
        if (IsLiveScanning)
        {
            await StopLiveScanAsync();
        }
        else
        {
            OpenDetachedBarRequested?.Invoke();
            await StartLiveScanAsync();
        }
    }

    public async Task StartLiveScanAsync()
    {
        if (IsLiveScanning) return;
        StatusMessage = "🎥 Starting live camera feed…";

        bool ok = await _liveCamera.StartAsync();
        if (ok)
        {
            IsLiveScanning = true;
            StatusMessage = "🎥 Live scanning active — hold QR code in front of camera";
            LogAction?.Invoke("QR Scan", "Started live camera QR scanner.");
        }
        else
        {
            StatusMessage = "❌ Failed to start live camera feed.";
        }
    }

    public async Task StopLiveScanAsync()
    {
        if (!IsLiveScanning) return;
        await _liveCamera.StopAsync();
        IsLiveScanning = false;
        LiveVideoFrame = null;
        StatusMessage = "Live camera scanning stopped.";
        LogAction?.Invoke("QR Scan", "Stopped live camera QR scanner.");
    }

    private void OnLiveCameraFrameArrived(byte[] imageBytes)
    {
        // 1. Update UI Video Frame
        try
        {
            using var ms = new MemoryStream(imageBytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                LiveVideoFrame = bi;
                CapturedImage = bi;
            });
        }
        catch { }

        // 2. Decode QR on-the-fly (skip frame if previous decode is still in flight)
        if (Interlocked.CompareExchange(ref _isDecodingFlag, 1, 0) == 0)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using var decodeMs = new MemoryStream(imageBytes);
                    using var bmp = new Bitmap(decodeMs);
                    var result = _barcodeReader.Decode(bmp);

                    if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    {
                        string text = result.Text.Trim();
                        var now = DateTimeOffset.UtcNow;

                        // Avoid repeated clobbering for identical code within 3 seconds
                        if (!string.Equals(text, _lastDecodedCode, StringComparison.Ordinal) ||
                            (now - _lastDecodeTime).TotalSeconds > 3)
                        {
                            _lastDecodedCode = text;
                            _lastDecodeTime = now;

                            Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                ProcessDecodeResult(text, isLive: true);
                            });
                        }
                    }
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _isDecodingFlag, 0);
                }
            });
        }
    }

    private void OnLiveCameraError(string err)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            StatusMessage = $"❌ Camera error: {err}";
            LogAction?.Invoke("QR Scan", $"Camera error: {err}");
        });
    }

    // ----- Paste Image from Clipboard -----

    public void PasteImageAndDecode()
    {
        try
        {
            BitmapSource? bitmapSource = null;

            if (Clipboard.ContainsImage())
            {
                bitmapSource = Clipboard.GetImage();
            }
            else if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                foreach (var file in files)
                {
                    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
                        {
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.UriSource = new Uri(file);
                            bi.EndInit();
                            bi.Freeze();
                            bitmapSource = bi;
                            break;
                        }
                    }
                }
            }

            if (bitmapSource == null)
            {
                StatusMessage = "⚠️ No image found in clipboard. Copy an image or screenshot first.";
                return;
            }

            CapturedImage = bitmapSource;
            LiveVideoFrame = bitmapSource;
            StatusMessage = "🔍 Scanning pasted image for QR code…";

            // Convert BitmapSource to System.Drawing.Bitmap for decoding
            using var memoryStream = new MemoryStream();
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            encoder.Save(memoryStream);
            memoryStream.Position = 0;

            using var sysBmp = new Bitmap(memoryStream);
            var result = _barcodeReader.Decode(sysBmp);

            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
            {
                ProcessDecodeResult(result.Text.Trim(), isLive: false);
                StatusMessage = $"✅ Decoded from clipboard image! {(IsWifiPayload ? $"(Wi-Fi: '{ParsedSsid}')" : "")}";
            }
            else
            {
                StatusMessage = "⚠️ Pasted image loaded, but no QR code was detected.";
                LogAction?.Invoke("QR Scan", "Pasted image loaded, no QR code detected.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to read clipboard image: {ex.Message}";
        }
    }

    // ----- Snapshot / File Decoding -----

    private void UpdateWifiInfo()
    {
        if (WifiQrParser.TryParse(DecodedText, out var info))
        {
            WifiInfo = info;
            IsPasswordRevealed = false;
        }
        else
        {
            WifiInfo = null;
        }
    }

    private void IngestProfile()
    {
        if (string.IsNullOrEmpty(ParsedSsid)) return;

        if (HasPassword)
        {
            CopyToClipboard(ParsedPassword!, "Wi-Fi Password");
        }

        OnIngestWifiProfile?.Invoke(ParsedSsid);
        StatusMessage = $"💾 Saved '{ParsedSsid}' to Wi-Fi Profiles & copied password!";
        LiveDecodeNotification = $"💾 Ingested '{ParsedSsid}' into Saved Profiles";
        LogAction?.Invoke("QR Scan", $"Ingested Wi-Fi network '{ParsedSsid}' into Saved Profiles");
    }

    public async Task CaptureAndDecodeAsync()
    {
        IsCapturing = true;
        StatusMessage = "📷 Snapping single frame from camera…";

        string tempJpg = Path.Combine(Path.GetTempPath(), $"etherprof_scan_{Guid.NewGuid():N}.jpg");
        string tempPs1 = Path.Combine(Path.GetTempPath(), $"etherprof_cam_{Guid.NewGuid():N}.ps1");

        try
        {
            bool captured = await Task.Run(() => CaptureFrameViaWinRtScript(tempPs1, tempJpg));
            if (!captured || !File.Exists(tempJpg))
            {
                StatusMessage = "❌ Camera capture failed. Verify camera permissions and availability.";
                LogAction?.Invoke("QR Scan", "Camera capture failed or device unavailable.");
                return;
            }

            LoadImagePreview(tempJpg);

            StatusMessage = "🔍 Scanning preview frame for QR code…";
            string? decoded = await Task.Run(() => DecodeQrFromFile(tempJpg));

            ProcessDecodeResult(decoded, isLive: false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
            LogAction?.Invoke("QR Scan", $"Error during capture: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempJpg)) File.Delete(tempJpg); } catch { }
            try { if (File.Exists(tempPs1)) File.Delete(tempPs1); } catch { }
            IsCapturing = false;
        }
    }

    public async Task OpenFileAndDecodeAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files (*.*)|*.*",
            Title = "Select QR Code Image File"
        };

        if (dialog.ShowDialog() != true) return;

        IsCapturing = true;
        StatusMessage = "📂 Loading image file…";

        try
        {
            string filePath = dialog.FileName;
            LoadImagePreview(filePath);

            StatusMessage = "🔍 Decoding QR code from file…";
            string? decoded = await Task.Run(() => DecodeQrFromFile(filePath));

            ProcessDecodeResult(decoded, isLive: false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed reading file: {ex.Message}";
        }
        finally
        {
            IsCapturing = false;
        }
    }

    private void ProcessDecodeResult(string? decoded, bool isLive)
    {
        LastScanTime = DateTime.Now.ToString("HH:mm:ss");

        if (string.IsNullOrWhiteSpace(decoded))
        {
            StatusMessage = "⚠️ No QR code detected in the frame. Hold code closer and centered.";
            LogAction?.Invoke("QR Scan", "No QR code detected in captured frame.");
            return;
        }

        DecodedText = decoded.Trim();

        // Always put raw code payload text into clipboard as requested
        CopyToClipboard(DecodedText, "QR Payload");

        if (IsWifiPayload)
        {
            StatusMessage = $"✅ Decoded Wi-Fi: '{ParsedSsid}' (Password copied to clipboard)";
            LiveDecodeNotification = $"✅ Wi-Fi: '{ParsedSsid}' | Password copied!";
            LogAction?.Invoke("QR Scan", $"Decoded Wi-Fi network: '{ParsedSsid}', Security: {ParsedAuthType}");

            if (HasPassword)
            {
                // Specifically copy Wi-Fi password for immediate connection
                CopyToClipboard(ParsedPassword!, "Wi-Fi Password");
            }
        }
        else
        {
            StatusMessage = "✅ Decoded QR code! (Payload copied to clipboard)";
            LiveDecodeNotification = $"✅ Decoded QR (Copied to clipboard)";
            LogAction?.Invoke("QR Scan", $"Decoded QR text: {Truncate(DecodedText, 60)}");
        }

        // Auditory feedback on successful live/paste decode
        try { SystemSounds.Asterisk.Play(); } catch { }
    }

    private void LoadImagePreview(string imagePath)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(imagePath);
            bi.EndInit();
            bi.Freeze();
            CapturedImage = bi;
            LiveVideoFrame = bi;
        }
        catch { }
    }

    public static string? DecodeQrFromFile(string imagePath)
    {
        try
        {
            using var bmp = new Bitmap(imagePath);
            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };
            var result = reader.Decode(bmp);
            return result?.Text;
        }
        catch
        {
            return null;
        }
    }

    private static bool CaptureFrameViaWinRtScript(string scriptPath, string outputPath)
    {
        string escapedOutPath = outputPath.Replace("'", "''");
        string psContent = @"
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTaskGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1

function AwaitOperation($op, $type) {
    $m = $asTaskGeneric.MakeGenericMethod($type)
    $task = $m.Invoke($null, @($op))
    $task.Wait(12000)
    return $task.Result
}

$asTaskAction = [System.WindowsRuntimeSystemExtensions].GetMethod('AsTask', [Type[]]@([Windows.Foundation.IAsyncAction]))
function AwaitAction($act) {
    $task = $asTaskAction.Invoke($null, @($act))
    $task.Wait(12000)
}

[Windows.Media.Capture.MediaCapture, Windows.Media, ContentType = WindowsRuntime] | Out-Null
[Windows.Media.Capture.MediaCaptureInitializationSettings, Windows.Media, ContentType = WindowsRuntime] | Out-Null
[Windows.Media.MediaProperties.ImageEncodingProperties, Windows.Media, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime] | Out-Null

$mc = [Windows.Media.Capture.MediaCapture]::new()
$settings = [Windows.Media.Capture.MediaCaptureInitializationSettings]::new()
$settings.StreamingCaptureMode = [Windows.Media.Capture.StreamingCaptureMode]::Video
AwaitAction ($mc.InitializeAsync($settings))

$stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
$enc = [Windows.Media.MediaProperties.ImageEncodingProperties]::CreateJpeg()
AwaitAction ($mc.CapturePhotoToStreamAsync($enc, $stream))

$stream.Seek(0)
$reader = [System.IO.BinaryReader]::new([System.IO.WindowsRuntimeStreamExtensions]::AsStream($stream))
$bytes = $reader.ReadBytes([int]$stream.Size)
[System.IO.File]::WriteAllBytes('" + escapedOutPath + @"', $bytes)
$mc.Dispose()
";

        File.WriteAllText(scriptPath, psContent);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return false;

        bool finished = proc.WaitForExit(15_000);
        if (!finished)
        {
            try { proc.Kill(); } catch { }
            return false;
        }

        return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
    }

    private void CopyToClipboard(string text, string label)
    {
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
            });
            StatusMessage = $"📋 Copied {label} to clipboard!";
        }
        catch
        {
            StatusMessage = $"⚠️ Could not access clipboard.";
        }
    }

    public void Clear()
    {
        CapturedImage = null;
        LiveVideoFrame = null;
        DecodedText = string.Empty;
        LiveDecodeNotification = null;
        StatusMessage = "Ready — start live scan or paste image from clipboard";
        LastScanTime = string.Empty;
    }

    private static string Truncate(string str, int maxLen) =>
        str.Length <= maxLen ? str : str[..maxLen] + "…";

    public async ValueTask DisposeAsync()
    {
        await StopLiveScanAsync();
        await _liveCamera.DisposeAsync();
    }
}
