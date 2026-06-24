using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeadStoreAutoBot.Models;
using LeadStoreAutoBot.ViewModels;

namespace LeadStoreAutoBot.Views;

public partial class TableTab : UserControl
{
    private MainViewModel? _vm;

    public TableTab()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = e.NewValue as MainViewModel;
        if (_vm != null) _vm.PropertyChanged += Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Когда бот меняет ActiveRecord — скроллим DataGrid к этой строке,
        // чтобы пользователь всегда видел "где сейчас идёт обработка".
        if (e.PropertyName != nameof(MainViewModel.ActiveRecord)) return;
        var rec = _vm?.ActiveRecord;
        if (rec == null) return;

        Dispatcher.BeginInvoke(() =>
        {
            try { Grid.ScrollIntoView(rec); } catch { /* DataGrid ещё не отрисован — ничего */ }
        });
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (PasteFromClipboard(grid)) e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && grid.SelectedCells.Count > 0)
        {
            ClearSelectedCells(grid);
            e.Handled = true;
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteFromClipboard(Grid);

    /// <summary>
    /// Парсит TSV из буфера и вставляет начиная с текущей выделенной ячейки.
    /// Заполняет существующие пустые строки, а если их не хватает — добавляет новые.
    /// </summary>
    private bool PasteFromClipboard(DataGrid grid)
    {
        if (DataContext is not MainViewModel vm) return false;
        if (!Clipboard.ContainsText()) return false;

        var text = Clipboard.GetText();
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            .Where(l => !string.IsNullOrEmpty(l)).ToArray();
        if (lines.Length == 0) return false;

        // Стартовая позиция
        int startRow = 0;
        int startCol = 1; // 1 = Сайт (0 = №, не редактируется)

        if (grid.CurrentCell.IsValid && grid.CurrentItem is PhoneRecord curRec)
        {
            startRow = vm.Records.IndexOf(curRec);
            if (startRow < 0) startRow = 0;
            var colIdx = grid.CurrentCell.Column?.DisplayIndex ?? 1;
            if (colIdx >= 1 && colIdx <= 3) startCol = colIdx;
        }
        else
        {
            // Найдём первую пустую строку
            for (int i = 0; i < vm.Records.Count; i++)
            {
                if (!vm.Records[i].HasData) { startRow = i; break; }
                if (i == vm.Records.Count - 1) startRow = vm.Records.Count;
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var cells = lines[i].Split('\t');

            // Если первая колонка — число (как номер строки), пропускаем
            if (cells.Length > 1 && int.TryParse(cells[0].Trim(), out _))
                cells = cells.Skip(1).ToArray();

            int targetIdx = startRow + i;

            PhoneRecord rec;
            if (targetIdx < vm.Records.Count)
            {
                rec = vm.Records[targetIdx];
            }
            else
            {
                rec = new PhoneRecord { RowNum = vm.Records.Count + 1 };
                vm.Records.Add(rec);
            }

            // Распределение по колонкам (startCol: 1=Сайт, 2=Тел.О, 3=Тел.М)
            for (int j = 0; j < cells.Length; j++)
            {
                int colIdx = startCol + j;
                var v = cells[j].Trim();
                switch (colIdx)
                {
                    case 1: rec.Site = v; break;
                    case 2: rec.Op   = v; break;
                    case 3: rec.Mg   = v; break;
                    // лишнее игнорируем
                }
            }
        }

        // Перенумеровать
        for (int i = 0; i < vm.Records.Count; i++) vm.Records[i].RowNum = i + 1;
        // Догенерировать пустые
        vm.EnsureEmptyRows();
        return true;
    }

    private void ClearSelectedCells(DataGrid grid)
    {
        foreach (var cell in grid.SelectedCells)
        {
            if (cell.Item is not PhoneRecord rec) continue;
            switch (cell.Column?.DisplayIndex)
            {
                case 1: rec.Site = ""; break;
                case 2: rec.Op = ""; break;
                case 3: rec.Mg = ""; break;
            }
        }
    }
}
