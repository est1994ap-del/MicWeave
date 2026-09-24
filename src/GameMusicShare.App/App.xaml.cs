using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameMusicShare.Services;

namespace GameMusicShare;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--verify-live"))
        {
            Shutdown(await OutputVerification.LiveAsync(e.Args));
            return;
        }
        if (e.Args.Contains("--verify-output"))
        {
            Shutdown(await OutputVerification.RunAsync(e.Args));
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            Shutdown(SelfTests.Run(e.Args));
            return;
        }
        if (e.Args.Contains("--diagnostics"))
        {
            Shutdown(await Diagnostics.RunAsync(e.Args));
            return;
        }
        if (e.Args.Contains("--render-preview"))
        {
            var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            MainWindow = window;
            window.Show();
            await Task.Delay(4500);
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var index = Array.IndexOf(e.Args, "--output");
            var path = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "ui-preview.png");
            using (var stream = File.Create(path)) encoder.Save(stream);
            window.Close();
            Shutdown();
            return;
        }
        instance = new Mutex(true, "Local\\GameMusicShare.Standalone", out var first);
        if (!first) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write(args.Exception);
            MessageBox.Show($"{ProductInfo.Name} could not complete that action. " + args.Exception.Message,
                ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
