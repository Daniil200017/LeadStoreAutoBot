using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using LeadStoreAutoBot.ViewModels;

namespace LeadStoreAutoBot.Views;

public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && string.IsNullOrEmpty(PwdBox.Password))
            PwdBox.Password = vm.Password;
    }

    private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Password = PwdBox.Password;
    }

    private void RegionsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void SelectAllRegions_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.AllRegions)
        {
            ShowToast("Сначала выключите «Все регионы»");
            return;
        }

        if (vm.SelectAllRegionsCommand.CanExecute(null))
            vm.SelectAllRegionsCommand.Execute(null);
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        if (Resources["ToastFade"] is Storyboard sb)
        {
            sb.Stop(this);
            sb.Begin(this, true);
        }
    }
}
