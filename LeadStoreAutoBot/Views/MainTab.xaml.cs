using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LeadStoreAutoBot.ViewModels;

namespace LeadStoreAutoBot.Views;

public partial class MainTab : UserControl
{
    private TextRange? _lastMatch;

    /// <summary>ScrollViewer внутри RichTextBox — нужен чтобы понимать где сейчас пользователь.</summary>
    private ScrollViewer? _logScroller;

    /// <summary>
    /// Умный автоскролл: держим лог у низа пока пользователь сам не отмотал вверх.
    /// Если отмотал — перестаём прокручивать до тех пор пока он снова не спустится.
    /// </summary>
    private bool _logAutoScroll = true;

    public MainTab()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        // Глобальный Ctrl+F на этой вкладке
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchPanel.Visibility == Visibility.Visible)
            {
                HideSearch();
                e.Handled = true;
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ScrollViewer рендерится шаблоном RichTextBox — добираемся до него через visual tree
        _logScroller = FindDescendant<ScrollViewer>(LogBox);
        if (_logScroller != null)
        {
            _logScroller.ScrollChanged += LogScroller_ScrollChanged;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            LogBox.Document = vm.LogDocument;
        }
    }

    /// <summary>
    /// Поведение как в Telegram:
    ///  — если пользователь внизу и пришёл новый лог → автоматически скроллим к концу;
    ///  — если он прокрутил вверх → автоскролл выключается, показываем кнопку «вниз»;
    ///  — как только юзер сам докрутил до самого низа → снова включаем автоскролл и прячем кнопку.
    /// </summary>
    private void LogScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_logScroller == null) return;

        // порог в 4 пикселя — чтобы мелкие колебания/округления не сбивали состояние
        bool atBottom = _logScroller.VerticalOffset
                        >= _logScroller.ScrollableHeight - 4;

        if (e.ExtentHeightChange > 0)
        {
            // добавилось содержимое (новый лог) — если были внизу, держимся внизу
            if (_logAutoScroll)
            {
                _logScroller.ScrollToEnd();
                return;
            }
        }
        else if (e.VerticalChange != 0)
        {
            // пользователь крутит колесом / тянет ползунок — пересчитываем режим
            _logAutoScroll = atBottom;
        }

        ScrollToEndButton.Visibility = _logAutoScroll
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ScrollToEndButton_Click(object sender, RoutedEventArgs e)
    {
        _logAutoScroll = true;
        _logScroller?.ScrollToEnd();
        ScrollToEndButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Рекурсивный поиск дочернего визуального элемента заданного типа.</summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            var deeper = FindDescendant<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SearchPanel.Visibility == Visibility.Visible) HideSearch();
        else ShowSearch();
    }

    private void CloseSearch_Click(object sender, RoutedEventArgs e) => HideSearch();

    private void ShowSearch()
    {
        SearchPanel.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void HideSearch()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        ClearHighlights();
        _lastMatch = null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _lastMatch = null;
        HighlightAll(SearchBox.Text);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift) FindNext(reverse: true);
            else FindNext(reverse: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideSearch();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext(false);
    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindNext(true);

    private void FindNext(bool reverse)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrEmpty(query)) return;
        var doc = LogBox.Document;

        TextPointer? start = _lastMatch?.End ?? doc.ContentStart;
        if (reverse) start = _lastMatch?.Start ?? doc.ContentEnd;

        var match = FindText(start, query, reverse);
        if (match == null)
        {
            // wrap
            match = FindText(reverse ? doc.ContentEnd : doc.ContentStart, query, reverse);
        }
        if (match != null)
        {
            _lastMatch = match;
            LogBox.Selection.Select(match.Start, match.End);
            match.Start.Paragraph?.BringIntoView();
        }
    }

    private static TextRange? FindText(TextPointer? start, string query, bool reverse)
    {
        if (start == null) return null;
        var current = start;
        var dir = reverse ? LogicalDirection.Backward : LogicalDirection.Forward;

        while (current != null)
        {
            if (current.GetPointerContext(dir) == TextPointerContext.Text)
            {
                var text = current.GetTextInRun(dir);
                int idx = reverse
                    ? text.LastIndexOf(query, System.StringComparison.OrdinalIgnoreCase)
                    : text.IndexOf(query, System.StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var matchStart = reverse
                        ? current.GetPositionAtOffset(-(text.Length - idx), LogicalDirection.Forward)
                        : current.GetPositionAtOffset(idx, LogicalDirection.Forward);
                    var matchEnd = matchStart?.GetPositionAtOffset(query.Length, LogicalDirection.Forward);
                    if (matchStart != null && matchEnd != null)
                        return new TextRange(matchStart, matchEnd);
                }
            }
            current = current.GetNextContextPosition(dir);
        }
        return null;
    }

    private void HighlightAll(string query)
    {
        ClearHighlights();
        if (string.IsNullOrEmpty(query)) return;

        var highlight = new SolidColorBrush(Color.FromArgb(140, 255, 230, 0));
        highlight.Freeze();

        var current = LogBox.Document.ContentStart;
        while (current != null && current.CompareTo(LogBox.Document.ContentEnd) < 0)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var text = current.GetTextInRun(LogicalDirection.Forward);
                int idx = 0;
                while ((idx = text.IndexOf(query, idx, System.StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    var s = current.GetPositionAtOffset(idx);
                    var e = s?.GetPositionAtOffset(query.Length);
                    if (s != null && e != null)
                        new TextRange(s, e).ApplyPropertyValue(TextElement.BackgroundProperty, highlight);
                    idx += query.Length;
                }
            }
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }
    }

    private void ClearHighlights()
    {
        var range = new TextRange(LogBox.Document.ContentStart, LogBox.Document.ContentEnd);
        range.ApplyPropertyValue(TextElement.BackgroundProperty, null);
    }
}
