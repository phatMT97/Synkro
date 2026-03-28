using System.Drawing;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Synkro.ViewModels;

namespace Synkro;

public enum CloseAction { Minimize, Exit, Cancel }

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
                switch (ShowCloseDialog())
                {
                    case CloseAction.Minimize:
                        _mainWindow.Hide();
                        break;
                    case CloseAction.Exit:
                        args.Cancel = false;
                        ExitApp();
                        break;
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

    private CloseAction ShowCloseDialog()
    {
        var result = CloseAction.Cancel;

        // Reuse color palette from MainWindow.xaml resources
        var res = _mainWindow!.Resources;
        var bg = (System.Windows.Media.SolidColorBrush)res["BgBrush"];
        var surface = (System.Windows.Media.SolidColorBrush)res["SurfaceBrush"];
        var accent = (System.Windows.Media.SolidColorBrush)res["AccentBrush"];
        var text = (System.Windows.Media.SolidColorBrush)res["TextBrush"];
        var border = (System.Windows.Media.SolidColorBrush)res["BorderBrush"];

        var dialog = new Window
        {
            Width = 320, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            Background = bg,
        };

        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Minimize to tray or exit?",
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        System.Windows.Controls.Button MakeBtn(string label, bool isPrimary = false)
        {
            var normalBg = isPrimary ? accent : surface;
            var hoverBg = isPrimary
                ? (System.Windows.Media.SolidColorBrush)res["AccentHoverBrush"]
                : border;

            var btn = new System.Windows.Controls.Button
            {
                Content = label,
                Width = 75, Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12,
            };

            var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var bdFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            bdFactory.Name = "bd";
            bdFactory.SetValue(System.Windows.Controls.Border.BackgroundProperty, normalBg);
            bdFactory.SetValue(System.Windows.Controls.Border.BorderBrushProperty,
                isPrimary ? accent : border);
            bdFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            bdFactory.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            bdFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(0));

            var cpFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            cpFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cpFactory.SetValue(System.Windows.Controls.TextBlock.ForegroundProperty, text);
            bdFactory.AppendChild(cpFactory);

            template.VisualTree = bdFactory;

            var hoverTrigger = new System.Windows.Trigger
            {
                Property = System.Windows.Controls.Button.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(
                System.Windows.Controls.Border.BackgroundProperty, hoverBg, "bd"));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        var yesBtn = MakeBtn("Yes", true);
        yesBtn.Click += (_, _) => { result = CloseAction.Minimize; dialog.Close(); };

        var exitBtn = MakeBtn("Exit");
        exitBtn.Click += (_, _) => { result = CloseAction.Exit; dialog.Close(); };

        var cancelBtn = MakeBtn("Cancel");
        cancelBtn.Margin = new Thickness(0);
        cancelBtn.Click += (_, _) => dialog.Close();

        buttons.Children.Add(yesBtn);
        buttons.Children.Add(exitBtn);
        buttons.Children.Add(cancelBtn);
        stack.Children.Add(buttons);

        dialog.Content = stack;
        dialog.ShowDialog();
        return result;
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
