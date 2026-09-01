using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace XTMF2.GUI.Views;

public partial class SaveChangesDialog : Window
{
    public enum DialogResult { Yes, No, Cancel }

    public string Message { get; }
    public DialogResult Result { get; private set; } = DialogResult.Cancel;

    public SaveChangesDialog() : this(string.Empty, string.Empty) { }

    public SaveChangesDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        Message = message;
        DataContext = this;
        Opened += (_, _) => YesButton.Focus();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(null, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Left or Key.Right)) return;

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var next = e.Key == Key.Left
            ? focused == NoButton ? YesButton : focused == CancelButton ? NoButton : null
            : focused == YesButton ? NoButton : focused == NoButton ? CancelButton : null;
        if (next is null) return;

        next.Focus();
        e.Handled = true;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e) { Result = DialogResult.Yes; Close(); }
    private void No_Click(object? sender, RoutedEventArgs e) { Result = DialogResult.No; Close(); }
    private void Cancel_Click(object? sender, RoutedEventArgs e) { Result = DialogResult.Cancel; Close(); }
}
