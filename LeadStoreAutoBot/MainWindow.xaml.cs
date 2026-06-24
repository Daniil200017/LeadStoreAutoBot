using LeadStoreAutoBot.ViewModels;
using Wpf.Ui.Controls;

namespace LeadStoreAutoBot;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
