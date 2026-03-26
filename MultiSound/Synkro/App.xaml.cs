using System.Drawing;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Synkro.ViewModels;

namespace Synkro;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers to prevent silent crashes
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Error: {args.Exception}", "Synkro Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"Fatal Error: {ex?.Message}\n\n{ex?.StackTrace}",
                "Synkro Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            _viewModel = new MainViewModel();
            _mainWindow = new MainWindow { DataContext = _viewModel };

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Synkro",
                MenuActivation = PopupActivationMode.RightClick
            };

            // Load custom icon from embedded resource
            var iconUri = new Uri("pack://application:,,,/icon.ico");
            using var iconStream = GetResourceStream(iconUri)?.Stream;
            _trayIcon.Icon = iconStream != null
                ? new Icon(iconStream)
                : SystemIcons.Application;
            _trayIcon.TrayLeftMouseDown += (_, _) => ShowWindow();

            var menu = new System.Windows.Controls.ContextMenu();
            var openItem = new System.Windows.Controls.MenuItem { Header = "Open" };
            openItem.Click += (_, _) => ShowWindow();
            var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
            exitItem.Click += (_, _) => ExitApp();
            menu.Items.Add(openItem);
            menu.Items.Add(exitItem);
            _trayIcon.ContextMenu = menu;

            _mainWindow.Closing += (_, args) =>
            {
                args.Cancel = true;
                var result = MessageBox.Show(
                    "Minimize to tray or exit?",
                    "Synkro",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                switch (result)
                {
                    case MessageBoxResult.Yes: // Minimize
                        _mainWindow.Hide();
                        break;
                    case MessageBoxResult.No: // Exit
                        args.Cancel = false;
                        ExitApp();
                        break;
                    // Cancel = do nothing
                }
            };

            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup Error:\n{ex.Message}\n\n{ex.StackTrace}",
                "Synkro Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    private void ExitApp()
    {
        _viewModel?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }
}
