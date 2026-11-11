using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aea.Core.Authentication;
using Aea.Core.Downloads;
using Aea.Core.Minecraft;
using Aea.Core.Releases;
using Aea.Core.Storage;

namespace Aea.App;

public partial class MainWindow : Window
{
    private readonly AppStorage storage;
    private readonly Diagnostics diagnostics;
    private readonly VerifiedDownloads downloads = new();
    private readonly bool smoke;
    private Brand brand = null!;
    private MicrosoftAccount? account;
    private ReleaseManager? manager;
    private VerifiedRelease? current, pending;
    private Preferences preferences;
    private bool busy, ready;
    private CancellationTokenSource? operation;
    private Process? game;
    private string? playerName;

    public MainWindow(AppStorage storage, bool smoke)
    {
        this.storage = storage;
        this.smoke = smoke;
        diagnostics = new(storage);
        preferences = Preferences.Load(storage);
        InitializeComponent();
        RamSlider.Maximum = Preferences.MaximumAllowedMb / 1024.0;
        RamSlider.Value = preferences.MaximumRamMb / 1024.0;
        RamLabel.Text = $"{RamSlider.Value:0.##} GB";
        ready = true;
        Loaded += async (_, _) => await Initialize();
        Closing += (_, e) =>
        {
            if (!busy) return;
            operation?.Cancel();
            Status.Text = "Cancelando. Espera a que termine antes de cerrar.";
            e.Cancel = true;
        };
        Closed += (_, _) => { account?.Dispose(); downloads.Dispose(); operation?.Dispose(); };
    }

    private async Task Initialize()
    {
        await Run(async ct =>
        {
            brand = Resource<Brand>("brand.json");
            Title = brand.Name;
            manager = new(storage, downloads, brand.PublicKey);
            current = manager.Load(Resource<SignedRelease>("release.json"));
            PackName.Text = $"{current.Pack.Name} · {current.Pack.Version}";
            account = new(storage, brand.MicrosoftClientId);
            if (!smoke)
            {
                game = GameProcess.Running(storage);
                if (game != null) _ = WatchGame(game);
            }
            if (!smoke && account.HasSavedSession)
            {
                AccountName.Text = "Sesión guardada";
                AccountButton.Content = "Cerrar sesión";
            }
            if (!smoke)
            {
                try { pending = await manager.Check(current, brand.UpdateUrl, ct); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    diagnostics.Failure(error);
                    Status.Text = "No se pudieron comprobar actualizaciones. Puedes jugar con la versión instalada.";
                    return;
                }
            }
            Status.Text = game != null ? "Minecraft está abierto." : pending != null ? $"Disponible la versión {pending.Pack.Version}. Pulsa Actualizar."
                : string.IsNullOrEmpty(brand.MicrosoftClientId) ? "El acceso Microsoft de este proyecto aún necesita configurarse." : "Listo para jugar.";
        });
        if (smoke) await Smoke();
    }

    private async void PrimaryClick(object sender, RoutedEventArgs e)
    {
        if (busy || current == null || manager == null || account == null || game != null) return;
        await Run(async ct =>
        {
            var progress = new Progress<string>(message => Status.Text = message);
            if (pending != null)
            {
                await manager.Install(pending, progress, ct);
                current = pending;
                pending = null;
                PackName.Text = $"{current.Pack.Name} · {current.Pack.Version}";
                Status.Text = "Actualizado. Ya puedes jugar.";
                return;
            }
            // Account validation is repeated for every launch; a cached UI name is not authorization.
            var session = await account.SignIn(true, progress, ct);
            playerName = session.Name;
            AccountName.Text = session.Name;
            AccountButton.Content = "Cerrar sesión";
            var prepared = await manager.Install(current, progress, ct);
            ct.ThrowIfCancellationRequested();
            Status.Text = "Abriendo Minecraft…";
            game = GameLaunch.Start(prepared, session, preferences.MaximumRamMb);
            GameProcess.Remember(storage, game);
            _ = WatchGame(game);
        });
    }

    private async Task WatchGame(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                diagnostics.Failure(new LauncherException("Minecraft", "Game process exited."));
                Status.Text = "Minecraft se cerró con un error. Revisa latest.log en la carpeta de esta instancia.";
            }
            else Status.Text = "Minecraft se ha cerrado.";
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { diagnostics.Failure(error); Status.Text = "No se pudo consultar el estado de Minecraft."; }
        finally { process.Dispose(); game = null; Controls(); }
    }

    private async void AccountClick(object sender, RoutedEventArgs e)
    {
        if (busy || account == null || game != null) return;
        await Run(async ct =>
        {
            if (account.HasSavedSession)
            {
                await account.SignOut();
                playerName = null;
                AccountName.Text = "Sin iniciar sesión";
                AccountButton.Content = "Iniciar sesión";
                Status.Text = "Sesión cerrada en este launcher.";
            }
            else
            {
                var session = await account.SignIn(true, new Progress<string>(message => Status.Text = message), ct);
                playerName = session.Name;
                AccountName.Text = session.Name;
                AccountButton.Content = "Cerrar sesión";
                Status.Text = "Cuenta verificada. Ya puedes jugar.";
            }
        });
    }

    private void RamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready || busy) return;
        try
        {
            preferences = new(Preferences.Validate((int)Math.Round(RamSlider.Value * 1024)));
            preferences.Save(storage);
            RamLabel.Text = $"{RamSlider.Value:0.##} GB";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { diagnostics.Failure(error); Status.Text = "No se pudo guardar el ajuste de RAM."; }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();

    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true;
        operation?.Dispose();
        operation = new CancellationTokenSource();
        Controls();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { Status.Text = "Operación cancelada. La versión instalada se conserva."; }
        catch (Exception error)
        {
            diagnostics.Failure(error);
            Status.Text = error switch
            {
                LauncherException failure => failure.Message,
                InvalidDataException => "Los archivos o la firma del pack no son válidos. La versión anterior se conserva; solicita una publicación corregida.",
                IOException => "No se pudo escribir o leer un archivo. Comprueba el espacio libre y los permisos e inténtalo de nuevo.",
                UnauthorizedAccessException => "Windows bloqueó el acceso a una carpeta. Revisa sus permisos.",
                HttpRequestException => "No se pudo completar la descarga. Revisa la conexión y reintenta.",
                _ => "No se pudo completar la operación. El código del fallo está en launcher.log."
            };
        }
        finally { busy = false; Controls(); }
    }

    private void Controls()
    {
        bool idle = !busy && game == null;
        PrimaryButton.IsEnabled = idle && current != null;
        PrimaryButton.Content = game != null ? "Jugando" : busy ? "Espera…" : pending != null ? "Actualizar" : "Jugar";
        AccountButton.IsEnabled = idle && account != null;
        if (account != null)
        {
            AccountName.Text = playerName ?? (account.HasSavedSession ? "Sesión Microsoft guardada" : "Sin iniciar sesión");
            AccountButton.Content = account.HasSavedSession ? "Cerrar sesión" : "Iniciar sesión";
            System.Windows.Automation.AutomationProperties.SetName(AccountButton, account.HasSavedSession ? "Cerrar sesión de este launcher" : "Iniciar sesión con Microsoft");
        }
        RamSlider.IsEnabled = idle;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        Progress.IsIndeterminate = busy;
    }

    private static T Resource<T>(string name)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aea.App.Resources." + name)
            ?? throw new InvalidDataException("Falta una configuración incluida en el launcher.");
        return JsonSerializer.Deserialize<T>(input, AppStorage.Json) ?? throw new InvalidDataException("Configuración inválida.");
    }

    private async Task Smoke()
    {
        try
        {
            string initialStatus = Status.Text;
            if (current == null || PrimaryButton.Content?.ToString() != "Jugar" || !PrimaryButton.IsEnabled || !RamSlider.IsEnabled)
                throw new InvalidOperationException("No se inició la interfaz.");
            RamSlider.Value = 3.5;
            if (Preferences.Load(storage).MaximumRamMb != 3584) throw new InvalidOperationException("La RAM no persiste.");
            await Run(_ => throw new LauncherException("Minecraft", "Minecraft rechazó el acceso de este launcher (HTTP 403). El administrador debe comprobar la aprobación de la aplicación.", 403));
            if (!Status.Text.Contains("403", StringComparison.Ordinal) || !PrimaryButton.IsEnabled) throw new InvalidOperationException("El error no permite reintentar.");
            Capture("error.png");
            Status.Text = initialStatus;
            Capture("launcher.png");
            AppStorage.AtomicWrite(Path.Combine(storage.Root, "smoke.json"), JsonSerializer.SerializeToUtf8Bytes(new
            { passed = true, ramPersisted = true, failureActionable = true, uiRendered = true, apiFailureSimulated = true, authenticatedGame = false }, AppStorage.Json));
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            diagnostics.Failure(error);
            File.WriteAllText(Path.Combine(storage.Root, "smoke-failed.txt"), error.GetType().Name);
            Application.Current.Shutdown(1);
        }
    }

    private void Capture(string name)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(storage.Root, name));
        encoder.Save(output);
    }
}
