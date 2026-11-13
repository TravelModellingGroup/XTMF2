using Avalonia.Controls;
using Avalonia.Interactivity;

namespace XTMF2.GUI.Views;

public partial class RunDirectoryConflictDialog : Window
{
    private readonly string _runDirectory;

    public string RunDirectory => _runDirectory;
    public bool OverwriteExistingFiles { get; private set; }
    public bool WasCancelled { get; private set; } = true;

    public RunDirectoryConflictDialog()
        : this(string.Empty)
    {
    }

    public RunDirectoryConflictDialog(string runDirectory)
    {
        _runDirectory = runDirectory;
        InitializeComponent();
        DataContext = this;
    }

    private void Overwrite_Click(object? sender, RoutedEventArgs e)
    {
        ChooseOverwriteExistingFiles();
        Close();
    }

    internal void ChooseOverwriteExistingFiles()
    {
        OverwriteExistingFiles = true;
        WasCancelled = false;
    }

    private void KeepFiles_Click(object? sender, RoutedEventArgs e)
    {
        ChooseKeepExistingFiles();
        Close();
    }

    internal void ChooseKeepExistingFiles()
    {
        OverwriteExistingFiles = false;
        WasCancelled = false;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
