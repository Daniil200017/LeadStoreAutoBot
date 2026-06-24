using System.Windows.Controls;
using LeadStoreAutoBot.ViewModels;

namespace LeadStoreAutoBot.Views;

public partial class HistoryTab : UserControl
{
    public HistoryTab()
    {
        InitializeComponent();
        Loaded += (_, __) => (DataContext as MainViewModel)?.RefreshHistory();
    }
}
