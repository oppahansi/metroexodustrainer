using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MetroExodusTrainer.Core;

namespace MetroExodusTrainer.Gui;

public partial class MainWindow : Window
{
    private readonly TrainerRunner _runner = new();
    private readonly MainVm _vm;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainVm(_runner);
        DataContext = _vm;

        _refresh.Tick += (_, _) =>
        {
            _vm.Refresh();
            NumLockNote.Visibility = _vm.NumLockOff ? Visibility.Visible : Visibility.Collapsed;
        };
        _refresh.Start();
        _runner.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refresh.Stop();
        _runner.Stop(); // puts the game back to its default values
        base.OnClosed(e);
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FeatureVm feature)
            feature.Toggle();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _vm.Reset();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
