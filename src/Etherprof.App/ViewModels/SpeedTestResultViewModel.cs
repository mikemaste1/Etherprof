namespace Etherprof.App.ViewModels;

using Etherprof.Contracts.Models;
using Etherprof.Core;

public sealed class SpeedTestResultViewModel : INotifyPropertyChangedHelper
{
    private readonly SpeedTestResult _result;
    private readonly Func<string?, string?> _aliasResolver;

    public SpeedTestResult Result => _result;

    public string DirectionSymbol => _result.Direction == SpeedTestDirection.Download ? "↓" : "↑";

    public string MbpsText => SpeedCalculationHelper.FormatMbps(_result.AverageMbps);

    public string SizeText => SpeedCalculationHelper.FormatSizeMiB(_result.BytesTransferred > 0 ? _result.BytesTransferred : 10_485_760L);

    public bool Success => _result.Success;
    public bool Cancelled => _result.Cancelled;

    public string StatusText
    {
        get
        {
            if (_result.Cancelled) return "Cancelled";
            if (!_result.Success) return _result.Error ?? "Failed";
            return $"{DirectionSymbol} {MbpsText}";
        }
    }

    public string ContextText
    {
        get
        {
            var ctx = _result.NetworkContext;
            if (ctx is null) return "Ethernet";

            if (ctx.IsWifi && !string.IsNullOrEmpty(ctx.StartBssid))
            {
                return GetDisplayName(ctx.StartBssid);
            }

            return !string.IsNullOrEmpty(ctx.AdapterName) ? ctx.AdapterName : "Ethernet";
        }
    }

    public string RssiText
    {
        get
        {
            var ctx = _result.NetworkContext;
            if (ctx?.IsWifi == true && ctx.StartRssiDbm.HasValue)
            {
                return $"{ctx.StartRssiDbm.Value} dBm";
            }
            return "";
        }
    }

    public bool HasRoamed => _result.NetworkContext?.RoamedDuringTest ?? false;

    public string RoamWarningText
    {
        get
        {
            var ctx = _result.NetworkContext;
            if (ctx?.RoamedDuringTest == true)
            {
                return ctx.RoamCount > 1 ? $"⚠ {ctx.RoamCount} roams" : "⚠ roam";
            }
            return "";
        }
    }

    public SpeedTestResultViewModel(SpeedTestResult result, Func<string?, string?> aliasResolver)
    {
        _result = result;
        _aliasResolver = aliasResolver;
    }

    public void NotifyAliasChanged()
    {
        OnPropertyChanged(nameof(ContextText));
    }

    private string GetDisplayName(string? bssid)
    {
        if (string.IsNullOrEmpty(bssid))
            return "";

        if (BssidNormalizer.TryNormalize(bssid, out var norm))
        {
            var alias = _aliasResolver(norm);
            if (!string.IsNullOrWhiteSpace(alias))
                return alias;
            return norm;
        }

        return bssid;
    }
}
