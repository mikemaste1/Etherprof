using System.Windows;
using Etherprof.App.Logging;
using Etherprof.App.ViewModels;
using Etherprof.Contracts.Interfaces;
using Etherprof.Core;
using Etherprof.Network.Windows;
using Etherprof.Storage;
using Etherprof.Testing;
using Microsoft.Extensions.Logging;

namespace Etherprof.App;

public partial class App : Application
{
    private INetworkAdapterProvider? _adapterProvider;
    private ITestRunner? _testRunner;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Set up logging
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new FileLoggerProvider());
        });

        try
        {
            // Create services
            var psRunner = new PowerShellRunner(loggerFactory.CreateLogger<PowerShellRunner>());

            _adapterProvider = new WindowsAdapterProvider(psRunner, loggerFactory.CreateLogger<WindowsAdapterProvider>());

            var configService = new PowerShellNetworkConfigurationService(
                psRunner, _adapterProvider, loggerFactory.CreateLogger<PowerShellNetworkConfigurationService>());

            var settingsRepo = new JsonSettingsRepository(loggerFactory.CreateLogger<JsonSettingsRepository>());
            var profileRepo = new JsonProfileRepository(loggerFactory.CreateLogger<JsonProfileRepository>());
            var testRepo = new JsonTestRepository(loggerFactory.CreateLogger<JsonTestRepository>());

            var profileManager = new ProfileManager(profileRepo);
            var testSetManager = new TestSetManager(testRepo);

            var pingTest = new PingConnectivityTest(loggerFactory.CreateLogger<PingConnectivityTest>());
            _testRunner = new TestRunner(pingTest, loggerFactory.CreateLogger<TestRunner>());

            // Wi-Fi Services (v0.2)
            var bssidRepo = new JsonWifiBssidRepository(loggerFactory.CreateLogger<JsonWifiBssidRepository>());
            var wifiMonitor = new Etherprof.Wifi.Windows.NativeWifiMonitor(loggerFactory.CreateLogger<Etherprof.Wifi.Windows.NativeWifiMonitor>());
            var wifiPanelVm = new WifiPanelViewModel(wifiMonitor, bssidRepo, loggerFactory.CreateLogger<WifiPanelViewModel>());

            // Speedtest Services (v0.3)
            var speedTestProvider = new Etherprof.SpeedTest.CloudflareSpeedTestProvider(loggerFactory.CreateLogger<Etherprof.SpeedTest.CloudflareSpeedTestProvider>());
            var speedTestResultLog = new BoundedSpeedTestResultLog();
            var speedTestRunner = new Etherprof.SpeedTest.SpeedTestRunner(
                speedTestProvider,
                _adapterProvider,
                wifiMonitor,
                speedTestResultLog,
                loggerFactory.CreateLogger<Etherprof.SpeedTest.SpeedTestRunner>());
            var speedTestPanelVm = new SpeedTestPanelViewModel(
                speedTestRunner,
                wifiPanelVm,
                loggerFactory.CreateLogger<SpeedTestPanelViewModel>());

            // Load persisted data
            var settings = await settingsRepo.LoadAsync();
            await profileManager.LoadAsync();
            await testSetManager.LoadAsync();

            // Create main view model
            var mainVm = new MainViewModel(
                _adapterProvider,
                configService,
                profileManager,
                testSetManager,
                _testRunner,
                settingsRepo,
                settings,
                wifiPanelVm,
                speedTestPanelVm,
                loggerFactory.CreateLogger<MainViewModel>());

            // Show main window
            var mainWindow = new Views.MainWindow { DataContext = mainVm };
            mainWindow.Show();

            // Start adapter monitoring
            _adapterProvider.StartMonitoring();

            // Initialize adapter state
            await mainVm.InitializeAsync();
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger<App>().LogCritical(ex, "Fatal error during startup");
            MessageBox.Show($"Failed to start Etherprof:\n\n{ex.Message}", "Etherprof", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_testRunner as IDisposable)?.Dispose();
        _adapterProvider?.Dispose();
        base.OnExit(e);
    }
}
