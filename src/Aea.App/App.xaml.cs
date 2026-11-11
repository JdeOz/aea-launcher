using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Windows;
using Aea.Core.Storage;

namespace Aea.App;

public partial class App : Application
{
    private Mutex? instance;
    public string? SmokeRoot { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool smoke = e.Args.Length == 2 && e.Args[0] == "--smoke";
        SmokeRoot = smoke ? Path.GetFullPath(e.Args[1]) : null;
        var storage = new AppStorage(SmokeRoot);
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storage.Root)))[..24];
        instance = new Mutex(true, @"Local\AeaLauncher-" + suffix, out bool created);
        if (!created)
        {
            MessageBox.Show("Aea ya está abierto para este perfil.", "Aea", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }
        var diagnostics = new Diagnostics(storage);
        DispatcherUnhandledException += (_, args) =>
        {
            diagnostics.Failure(args.Exception);
            MessageBox.Show("No se pudo completar la operación. Cierra Aea y vuelve a abrirlo. El diagnóstico está en launcher.log.", "Aea", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        MainWindow = new MainWindow(storage, smoke);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
