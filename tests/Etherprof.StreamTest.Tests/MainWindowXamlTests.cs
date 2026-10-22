namespace Etherprof.StreamTest.Tests;

using System;
using System.Threading;
using System.Windows;
using Etherprof.App.Views;
using Xunit;

public class MainWindowXamlTests
{
    [Fact]
    public void MainWindow_InstantiatesWithoutXamlException()
    {
        Exception? caughtException = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var app = new Etherprof.App.App();
                    app.InitializeComponent();
                }

                var window = new MainWindow();
                Assert.NotNull(window);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caughtException != null)
        {
            string details = caughtException.ToString();
            if (caughtException.InnerException != null)
            {
                details += "\nInnerException: " + caughtException.InnerException.ToString();
            }
            Assert.Fail($"MainWindow XAML instantiation failed: {details}");
        }
    }
}
