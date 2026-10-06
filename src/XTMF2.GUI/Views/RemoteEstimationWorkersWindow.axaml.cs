using Avalonia.Controls;
using Avalonia.Interactivity;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

/// <summary>
/// Modeless window for managing the RunServers assigned to a shared estimation.
/// </summary>
public partial class RemoteEstimationWorkersWindow : Window
{
    public RemoteEstimationWorkersWindow()
    {
        InitializeComponent();
    }

    public RemoteEstimationWorkersWindow(RunViewModel run) : this()
    {
        DataContext = run;
        Title = $"Estimation Workers - {run.RunName}";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}