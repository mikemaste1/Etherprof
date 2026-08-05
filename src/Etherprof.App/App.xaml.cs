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
    private Etherprof.StreamTest.Abstractions.IStreamTestClient? _streamClient;
    private Etherprof.StreamTest.Abstractions.IStreamTestServer? _streamServer;

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

            // StreamTest Services (v0.4)
            _streamClient = new Etherprof.StreamTest.Client.StreamTestClient(loggerFactory.CreateLogger<Etherprof.StreamTest.Client.StreamTestClient>());
            _streamServer = new Etherprof.StreamTest.Server.StreamTestServer(loggerFactory.CreateLogger<Etherprof.StreamTest.Server.StreamTestServer>());

            // Load persisted data
            var settings = await settingsRepo.LoadAsync();
            await profileManager.LoadAsync();
            await testSetManager.LoadAsync();

            var streamPanelVm = new StreamPanelViewModel(_streamClient, wifiPanelVm, settingsRepo, settings, loggerFactory.CreateLogger<StreamPanelViewModel>());
            var streamServerPanelVm = new StreamServerPanelViewModel(_streamServer, settingsRepo, settings, loggerFactory.CreateLogger<StreamServerPanelViewModel>());

            // Check CLI switch --server
            bool startServerMode = e.Args.Any(arg => string.Equals(arg, "--server", StringComparison.OrdinalIgnoreCase));
            if (startServerMode)
            {
                _ = streamServerPanelVm.StartServerAsync();
            }

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
                streamPanelVm,
                streamServerPanelVm,
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

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_streamClient is not null) await _streamClient.StopAsync();
        if (_streamServer is not null) await _streamServer.StopAsync();
        (_testRunner as IDisposable)?.Dispose();
        _adapterProvider?.Dispose();
        base.OnExit(e);
    }
}
