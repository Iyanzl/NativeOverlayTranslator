namespace NativeOverlayTranslator;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            System.Windows.MessageBox.Show(
                args.Exception.Message,
                "Native Overlay Translator error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogCrash(exception);
            }
        };

        base.OnStartup(e);
    }

    private static void LogCrash(Exception exception)
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "crash.log");
            System.IO.File.AppendAllText(path, $"[{DateTimeOffset.Now:O}]\n{exception}\n\n");
        }
        catch
        {
            // Avoid recursive crash handling.
        }
    }
}
