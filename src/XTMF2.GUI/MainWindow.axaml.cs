/*
    Copyright 2026 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using ActiveDockableChangedEventArgs = Dock.Model.Core.Events.ActiveDockableChangedEventArgs;
using DockableClosedEventArgs = Dock.Model.Core.Events.DockableClosedEventArgs;
using DockableClosingEventArgs = Dock.Model.Core.Events.DockableClosingEventArgs;
using DockableRemovedEventArgs = Dock.Model.Core.Events.DockableRemovedEventArgs;
using DockWindowClosingEventArgs = Dock.Model.Core.Events.WindowClosingEventArgs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTMF2;
using XTMF2.AI;
using XTMF2.Editing;
using XTMF2.GUI.AI;
using XTMF2.GUI.Controls;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Views;

namespace XTMF2.GUI;

public partial class MainWindow : Window
{
    private XTMFRuntime? _runtime;
    private User? _currentUser;
    private bool _isLoading = true;
    private bool _allowClose;
    private bool _closeCheckInProgress;
    private bool _allowDocumentClose;
    private bool _documentCloseInProgress;
    private SettingsWindow? _settingsWindow;
    private RunServersWindow? _runServersWindow;
    private readonly HttpClient _aiHttpClient = new();
    private readonly AiProviderRegistry _aiProviders = new();

    /// <summary>
    /// The single RunController instance for this GUI session.
    /// Initialized when the runtime loads; null until then.
    /// </summary>
    private RunController? _runController;

    /// <summary>
    /// The collection of document view models displayed in the dock.
    /// Starts empty; the Projects tab is added once the runtime is loaded.
    /// </summary>
    public ObservableCollection<object> Documents { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        _aiHttpClient.Timeout = TimeSpan.FromMinutes(10);
        var endpoint = Uri.TryCreate(
            Properties.Settings.Default.OllamaEndpoint,
            UriKind.Absolute,
            out var parsedEndpoint)
            && parsedEndpoint.Scheme is "http" or "https"
            ? parsedEndpoint
            : new Uri("http://localhost:11434");
        _aiProviders.Register(new OllamaProvider(
            _aiHttpClient,
            endpoint));
        DataContext = this;
        InitializeDock();
        UpdateLoadingState();
    }

    private Factory? _factory;
    private IDockable? _activeDockable;
    /// <summary>The <see cref="ModelSystemEditorViewModel"/> in the currently active dock tab, or null.</summary>
    private ModelSystemEditorViewModel? _activeEditorVm;

    private void InitializeDock()
    {
        _factory = DockControl.Factory as Factory
            ?? throw new InvalidOperationException("DockControl factory was not initialized.");
        _factory.DockableClosing += OnDockableClosing;
        _factory.DockableClosed += OnDockableClosed;
        _factory.DockableRemoved += OnDockableRemoved;
        _factory.WindowClosing += OnFloatingWindowClosing;
        _factory.ActiveDockableChanged += OnActiveDockableChanged;

        var documentDock = DockControl.Layout?.VisibleDockables
            ?.OfType<DocumentDock>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Document dock was not initialized.");

        // The source collection owns view models; Dock owns generated containers.
        Documents.CollectionChanged += OnDocumentsSourceChanged;

        // Track activation at the factory level so floating windows participate too.
    }

    private void OnDocumentsSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
                (item as IDisposable)?.Dispose();
        }
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable is not IDocument document || document.Context is not object context)
            return;

        RemoveClosedDocument(context);
    }

    private void OnDockableRemoved(object? sender, DockableRemovedEventArgs e)
    {
        if (e.Dockable is not IDocument document || document.Context is not object context)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsDocumentInLayout(document))
                RemoveClosedDocument(context);
        }, DispatcherPriority.Background);
    }

    private void RemoveClosedDocument(object context)
    {
        if (Documents.Contains(context))
        {
            Documents.Remove(context);
            CloseEmptyFloatingWindows();
        }
    }

    private bool IsDocumentInLayout(IDocument document)
    {
        if (DockControl.Layout is not IRootDock root)
            return false;

        if (ContainsDocument(root, document))
            return true;

        foreach (var window in root.Windows ?? Enumerable.Empty<IDockWindow>())
        {
            if (window.Layout is not null && ContainsDocument(window.Layout, document))
                return true;
        }

        return false;
    }

    private static bool ContainsDocument(IDock dock, IDocument document)
    {
        foreach (var dockable in dock.VisibleDockables ?? Enumerable.Empty<IDockable>())
        {
            if (ReferenceEquals(dockable, document))
                return true;

            if (dockable is IDock childDock && ContainsDocument(childDock, document))
                return true;
        }

        return false;
    }

    private void CloseEmptyFloatingWindows()
    {
        if (DockControl.Layout is not IRootDock root)
            return;

        foreach (var window in (root.Windows ?? Enumerable.Empty<IDockWindow>()).ToList())
        {
            if (window.Layout is not null && !EnumerateDocuments(window.Layout).Any())
                window.OnClose();
        }
    }

    private async void OnFloatingWindowClosing(object? sender, DockWindowClosingEventArgs e)
    {
        if (e.Window?.Layout is not { } layout)
            return;

        var dirtyEditors = GetDirtyEditors(layout).ToList();

        if (dirtyEditors.Count == 0)
            return;

        e.Cancel = true;
        foreach (var editor in dirtyEditors)
        {
            await PromptToCloseEditorAsync(editor);
            if (Documents.Contains(editor))
                return;
        }

        // The original close event was canceled while the dialog was open.
        // Clear that cancellation so Dock can finish closing this same window.
        e.Cancel = false;
    }

    private static IEnumerable<IDocument> EnumerateDocuments(IDock? dock)
    {
        foreach (var dockable in dock?.VisibleDockables ?? Enumerable.Empty<IDockable>())
        {
            if (dockable is IDocument document)
                yield return document;

            if (dockable is IDock childDock)
            {
                foreach (var childDocument in EnumerateDocuments(childDock))
                    yield return childDocument;
            }
        }
    }

    private IEnumerable<ModelSystemEditorViewModel> GetDirtyEditors(IDockable dockable)
    {
        var closingDocuments = dockable is IDocument document
            ? new[] { document }
            : dockable is IDock dock
                ? EnumerateDocuments(dock)
                : Enumerable.Empty<IDocument>();

        var closingDocumentSet = closingDocuments.ToHashSet();
        return closingDocumentSet
            .Select(document => document.Context)
            .OfType<ModelSystemEditorViewModel>()
            .Where(editor => editor.IsDirty)
            .Where(editor => IsOnlyOpenEditorForModelSystem(editor, closingDocumentSet))
            .Distinct();
    }

    private bool IsOnlyOpenEditorForModelSystem(
        ModelSystemEditorViewModel editor,
        HashSet<IDocument> closingDocuments)
    {
        return !EnumerateOpenDocuments()
            .Any(document => !closingDocuments.Contains(document)
                && document.Context is ModelSystemEditorViewModel other
                && other.ModelSystemHeader == editor.ModelSystemHeader);
    }

    private IEnumerable<IDocument> EnumerateOpenDocuments()
    {
        if (DockControl.Layout is not IRootDock root)
            yield break;

        foreach (var document in EnumerateDocuments(root))
            yield return document;

        foreach (var window in root.Windows ?? Enumerable.Empty<IDockWindow>())
        {
            if (window.Layout is null)
                continue;

            foreach (var document in EnumerateDocuments(window.Layout))
                yield return document;
        }
    }

    /// <summary>
    /// Initialize the window with the runtime once it's loaded.
    /// </summary>
    /// <param name="runtime">The fully initialised XTMF runtime.</param>
    /// <param name="runController">
    /// The already-initialised <see cref="RunController"/>.
    /// Pass <c>null</c> when the controller could not be created; the Run button
    /// will be disabled but all other functionality remains available.
    /// </param>
    /// <param name="user">The currently logged-in user.</param>
    public void InitializeWithRuntime(XTMFRuntime runtime, RunController? runController, User user)
    {
        _runtime = runtime;
        _runController = runController;
        _currentUser = user;
        _isLoading = false;
        UpdateLoadingState();

        foreach (var editor in EnumerateOpenDocuments()
            .Select(document => document.Context)
            .OfType<ModelSystemEditorViewModel>())
            editor.SetRunController(_runController);

        // Add the Runs tab so it is always visible.
        if (_runController is not null)
            Documents.Add(_runController.RunsViewModel);

        // Add the Projects tab as the permanent first document
        Documents.Add(new ProjectsViewModel(runtime, user));
    }

    private void UpdateLoadingState()
    {
        LoadingPanel?.IsVisible = _isLoading;
    }

    /// <summary>
    /// Open a new tab for a project, showing its model systems.
    /// If a tab for this project is already open it is activated instead.
    /// </summary>
    public void OpenProjectTab(Project project)
    {
        if (_runtime is null)
            return;

        // Check if a tab for this project already exists
        var existing = Documents
            .OfType<ModelSystemsViewModel>()
            .FirstOrDefault(vm => vm.Project == project);

        if (existing is not null)
        {
            if (ActivateDocument(existing))
                return;

            Documents.Remove(existing);
        }
        // No existing tab, create a new one
        var modelSystems = new ModelSystemsViewModel(_runtime, _currentUser!, project, this);
        Documents.Add(modelSystems);
        ActivateDocument(modelSystems);
    }

    private bool ActivateDocument(object viewModel)
    {
        if (_factory?.GetContainerFromItem(viewModel) is IDocument document
            && document.Owner is IDock dock)
        {
            dock.ActiveDockable = document;
            return true;
        }

        return false;
    }


    /// <summary>
    /// Opens a new tab for the given model system editing session.
    /// </summary>
    /// <param name="session">The model system editing session to open.</param>
    /// <param name="user">The user who opened the session (forwarded to the editor VM).</param>
    public void OpenModelSystemTab(ModelSystemSession session, User user)
    {
        var editor = CreateEditorViewModel(session, user);
        editor.RunStarted = SwitchToRunsDocument;
        Documents.Add(editor);
        ActivateDocument(editor);
    }

    /// <summary>
    /// Opens a new model-system editor tab and returns its VM.
    /// </summary>
    public ModelSystemEditorViewModel OpenModelSystemTabAndGet(ModelSystemSession session, User user)
    {
        var editor = CreateEditorViewModel(session, user);
        editor.RunStarted = SwitchToRunsDocument;
        Documents.Add(editor);
        ActivateDocument(editor);
        return editor;
    }

    private ModelSystemEditorViewModel CreateEditorViewModel(ModelSystemSession session, User user)
    {
        var service = new AiAssistantService(
            _aiProviders,
            new ModelSystemActionApplier(session, user));
        return new ModelSystemEditorViewModel(
            session,
            user,
            _runController,
            service,
            Properties.Settings.Default.AiModel,
            Properties.Settings.Default.AiProvider,
            Properties.Settings.Default.AiMaxCompactionCycles);
    }

    /// <summary>
    /// Brings the tab for the given editor VM to the front.
    /// </summary>
    public void FocusEditorTab(ModelSystemEditorViewModel editorVm)
    {
        ActivateDocument(editorVm);
    }

    /// <summary>
    /// Switches the active document to the Runs tab.
    /// </summary>
    private void SwitchToRunsDocument()
    {
        if (_runController is null) return;
        ActivateDocument(_runController.RunsViewModel);
    }

    /// <summary>
    /// Opens a new tab that computes and displays the diff between two model systems.
    /// The <paramref name="diffFactory"/> is invoked on a background thread so the UI
    /// remains responsive while the diff is being computed.
    /// </summary>
    /// <param name="diffFactory">A synchronous factory that loads the snapshots and runs the comparison.
    /// Receives <c>true</c> when the sides are swapped (the user clicked ⟷).</param>
    /// <param name="leftHeader">Header for the base model system. Null when loaded from a file.</param>
    /// <param name="rightHeader">Header for the compare model system. Null when loaded from a file.</param>
    /// <param name="openOrFocusEditor">Callback to open or focus an editor tab for a given header.</param>
    public void OpenDiffTab(Func<bool, XTMF2.Diff.ModelSystemDiff> diffFactory,
        XTMF2.ModelSystemHeader? leftHeader = null,
        XTMF2.ModelSystemHeader? rightHeader = null,
        Func<XTMF2.ModelSystemHeader, System.Threading.Tasks.Task<ModelSystemEditorViewModel?>>? openOrFocusEditor = null)
    {
        var vm = new ModelSystemDiffViewModel(diffFactory, leftHeader, rightHeader);
        vm.OpenOrFocusEditorCallback = openOrFocusEditor;
        Documents.Add(vm);
        ActivateDocument(vm);
    }

    /// <summary>
    /// Close the tab for a given project.
    /// </summary>
    public void CloseProjectTab(Project project)
    {
        var vm = Documents
            .OfType<ModelSystemsViewModel>()
            .FirstOrDefault(vm => vm.Project == project);

        if (vm is not null)
            Documents.Remove(vm);
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SettingsWindow(_runController);
            _settingsWindow.SettingsSaved += () => _runController?.RefreshConfiguredRunServers();
            _settingsWindow.Closed += (s, _) => _settingsWindow = null;
            _settingsWindow.ShowDialog(this);
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void ShowRunServersWindow()
    {
        var settingsOwner = _settingsWindow;
        if (settingsOwner is null)
            return;

        if (_runServersWindow is null || !_runServersWindow.IsVisible)
        {
            _runServersWindow = new RunServersWindow(_runController);
            _runServersWindow.RunServersSaved += () => _runController?.RefreshConfiguredRunServers();
            _runServersWindow.ShowDialog(settingsOwner).GetAwaiter().GetResult();
        }
    }

    [RelayCommand]
    private void LaunchAboutDialog()
    {
        var aboutDialog = new AboutDialog();
        aboutDialog.ShowDialog(this);
    }

    /// <summary>Updates the active editor when any dock, including a floating dock, activates a document.</summary>
    private void OnActiveDockableChanged(object? sender, ActiveDockableChangedEventArgs e)
    {
        _activeDockable = e.Dockable;
        var newVm = (e.Dockable as IDocument)?.Context as ModelSystemEditorViewModel;
        if (ReferenceEquals(_activeEditorVm, newVm)) return;

        if (_activeEditorVm is INotifyPropertyChanged oldNpc)
            oldNpc.PropertyChanged -= OnActiveEditorPropertyChanged;

        _activeEditorVm = newVm;

        if (_activeEditorVm is INotifyPropertyChanged newNpc)
            newNpc.PropertyChanged += OnActiveEditorPropertyChanged;

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        NavigateBoundaryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Refreshes undo/redo can-execute state when the active editor's state changes.</summary>
    private void OnActiveEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelSystemEditorViewModel.CanUndo)
                           or nameof(ModelSystemEditorViewModel.CanRedo))
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteUndo))]
    private void Undo() => _activeEditorVm?.UndoCommand.Execute(null);
    private bool CanExecuteUndo() => _activeEditorVm?.CanUndo ?? false;

    [RelayCommand(CanExecute = nameof(CanExecuteRedo))]
    private void Redo() => _activeEditorVm?.RedoCommand.Execute(null);
    private bool CanExecuteRedo() => _activeEditorVm?.CanRedo ?? false;

    [RelayCommand(CanExecute = nameof(CanExecuteNavigateBoundary))]
    private void NavigateBoundary() => _activeEditorVm?.BrowseBoundariesCommand.Execute(null);
    private bool CanExecuteNavigateBoundary() => _activeEditorVm is not null;

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private async void OnDockableClosing(object? sender, DockableClosingEventArgs e)
    {
        if (_allowDocumentClose)
            return;

        if (e.Dockable is not { } closingDockable)
            return;

        var dirtyEditors = GetDirtyEditors(closingDockable).ToList();
        if (dirtyEditors.Count == 0)
        {
            if (closingDockable is IDocument { Context: object context }
                && Documents.Contains(context))
            {
                _allowDocumentClose = true;
                try
                {
                    Documents.Remove(context);
                }
                finally
                {
                    _allowDocumentClose = false;
                }
            }

            return;
        }

        e.Cancel = true;
        foreach (var editor in dirtyEditors)
        {
            await PromptToCloseEditorAsync(editor);
            if (Documents.Contains(editor))
                return;
        }
    }

    private async Task PromptToCloseEditorAsync(ModelSystemEditorViewModel editor)
    {
        if (_documentCloseInProgress) return;
        _documentCloseInProgress = true;
        try
        {
            FocusEditorTab(editor);
            var dialog = new SaveChangesDialog(
                "Save Model System",
                $"Save changes to '{editor.ModelSystemHeader.Name ?? "Model System"}' before closing?");
            await dialog.ShowDialog(this);

            if (dialog.Result == SaveChangesDialog.DialogResult.Cancel)
                return;
            if (dialog.Result == SaveChangesDialog.DialogResult.Yes
                && !await editor.SaveModelSystemAsync())
                return;

            _allowDocumentClose = true;
            Documents.Remove(editor);
        }
        finally
        {
            _allowDocumentClose = false;
            _documentCloseInProgress = false;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_closeCheckInProgress) return;
        _closeCheckInProgress = true;
        try
        {
            foreach (var editor in Documents
                .OfType<ModelSystemEditorViewModel>()
                .Where(vm => vm.IsDirty)
                .GroupBy(vm => vm.Session)
                .Select(group => group.First())
                .ToList())
            {
                FocusEditorTab(editor);
                var dialog = new SaveChangesDialog(
                    "Save Model System",
                    $"Save changes to '{editor.ModelSystemHeader.Name ?? "Model System"}' before closing?");
                await dialog.ShowDialog(this);

                if (dialog.Result == SaveChangesDialog.DialogResult.Cancel)
                    return;
                if (dialog.Result == SaveChangesDialog.DialogResult.Yes
                    && !await editor.SaveModelSystemAsync())
                    return;
            }

            _allowDocumentClose = true;
            _allowClose = true;
            Close();
        }
        finally
        {
            _closeCheckInProgress = false;
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control)
        {
            CloseCurrentDocument();
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
        {
            FocusNearestSearchBox();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Focuses the SearchBox with the fewest visual hops from the current keyboard focus.
    /// Walks up from the focused element looking for a containing view that owns a SearchBox.
    /// Falls back to the first visible SearchBox in the DockControl if nothing is focused or
    /// the focused element is not inside a known view.
    /// </summary>
    private void FocusNearestSearchBox()
    {
        // Walk up from the currently focused element to find the nearest containing view.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Avalonia.Visual;
        var current = focused;
        while (current is not null)
        {
            if (current is ProjectsView or ModelSystemsView)
            {
                var searchBox = FindFirstDescendantSearchBox(current);
                if (searchBox is not null)
                {
                    searchBox.FocusSearchBox();
                    return;
                }
            }
            current = current.GetVisualParent();
        }

        // Fallback: focus the first SearchBox visible in the dock.
        FindFirstDescendantSearchBox(DockControl)?.FocusSearchBox();
    }

    /// <summary>Recursively finds the first <see cref="SearchBox"/> in a visual subtree.</summary>
    private static SearchBox? FindFirstDescendantSearchBox(Avalonia.Visual root)
    {
        if (root is SearchBox sb) return sb;
        foreach (var child in root.GetVisualChildren())
        {
            var found = FindFirstDescendantSearchBox(child);
            if (found is not null) return found;
        }
        return null;
    }

    private void CloseCurrentDocument()
    {
        // Find the currently active document and remove it from the collection, which will close the tab.
        if (_activeDockable is IDocument activeDoc
            && activeDoc.CanClose
            && activeDoc.Context is object context)
        {
            if (activeDoc.Context is ModelSystemEditorViewModel editor && editor.IsDirty)
            {
                _ = PromptToCloseEditorAsync(editor);
                return;
            }

            // Now that we tried to move back, we can remove the active document.
            Documents.Remove(context);
        }
    }
}
