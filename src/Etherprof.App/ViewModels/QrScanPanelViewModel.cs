using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Etherprof.Core;
using ZXing;
using ZXing.Windows.Compatibility;

namespace Etherprof.App.ViewModels;

/// <summary>
/// ViewModel for the QR Scanner section.
/// Captures camera preview images, decodes QR codes with ZXing.Net,
/// automatically copies payload text to clipboard, and parses Wi-Fi
/// credentials for easy copy and ingestion into Saved Wi-Fi Profiles.
/// </summary>
public sealed class QrScanPanelViewModel : ViewModelBase
{
    // ----- Status & Display Properties -----

    private string _statusMessage = "Ready — position QR code in front of camera and click Capture";
    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
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

    public QrScanPanelViewModel()
    {
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
            () => HasCapturedImage || HasDecodedText);
    }

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
        LogAction?.Invoke("QR Scan", $"Ingested Wi-Fi network '{ParsedSsid}' into Saved Profiles");
    }

    public async Task CaptureAndDecodeAsync()
    {
        IsCapturing = true;
        StatusMessage = "📷 Accessing camera…";

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

            // Load captured image for preview
            LoadImagePreview(tempJpg);

            // Decode image
            StatusMessage = "🔍 Scanning preview frame for QR code…";
            string? decoded = await Task.Run(() => DecodeQrFromFile(tempJpg));

            ProcessDecodeResult(decoded);
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

            StatusMessage = "🔍 Decoding QR code from image…";
            string? decoded = await Task.Run(() => DecodeQrFromFile(filePath));

            ProcessDecodeResult(decoded);
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

    private void ProcessDecodeResult(string? decoded)
    {
        LastScanTime = DateTime.Now.ToString("HH:mm:ss");

        if (string.IsNullOrWhiteSpace(decoded))
        {
            StatusMessage = "⚠️ No QR code detected in the frame. Hold code closer and centered.";
            LogAction?.Invoke("QR Scan", "No QR code detected in captured frame.");
            return;
        }

        DecodedText = decoded.Trim();

        // Automatically put payload text into clipboard as requested
        CopyToClipboard(DecodedText, "QR Payload");

        if (IsWifiPayload)
        {
            StatusMessage = $"✅ Decoded Wi-Fi: '{ParsedSsid}' (Password copied to clipboard)";
            LogAction?.Invoke("QR Scan", $"Decoded Wi-Fi network: '{ParsedSsid}', Security: {ParsedAuthType}");
            if (HasPassword)
            {
                // Also copy the password specifically to clipboard
                CopyToClipboard(ParsedPassword!, "Wi-Fi Password");
            }
        }
        else
        {
            StatusMessage = "✅ Decoded QR code! (Payload copied to clipboard)";
            LogAction?.Invoke("QR Scan", $"Decoded QR text: {Truncate(DecodedText, 60)}");
        }
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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
        DecodedText = string.Empty;
        StatusMessage = "Ready — position QR code in front of camera and click Capture";
        LastScanTime = string.Empty;
    }

    private static string Truncate(string str, int maxLen) =>
        str.Length <= maxLen ? str : str[..maxLen] + "…";
}
