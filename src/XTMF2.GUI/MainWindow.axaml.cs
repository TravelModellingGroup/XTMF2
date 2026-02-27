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
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using XTMF2;
using XTMF2.Editing;
using XTMF2.GUI.Controls;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Views;

namespace XTMF2.GUI;

public partial class MainWindow : Window
{
    private XTMFRuntime? _runtime;
    private bool _isLoading = true;
    private SettingsWindow? _settingsWindow;

    /// <summary>
    /// The collection of document view models displayed in the dock.
    /// Starts empty; the Projects tab is added once the runtime is loaded.
    /// </summary>
    public ObservableCollection<object> Documents { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        InitializeDock();
        UpdateLoadingState();
    }

    private DocumentDock? _documentDock;
    /// <summary>Guard to prevent circular syncing between Documents and VisibleDockables.</summary>
    private bool _suppressDocumentsSync;

    private void InitializeDock()
    {
        var factory = new Factory();
        _documentDock = new DocumentDock
        {
            Id = "DocumentDock",
            CanCreateDocument = false,
            VisibleDockables = new AvaloniaList<IDockable>()
        };
        var rootDock = new RootDock
        {
            Id = "Root",
            IsCollapsable = false,
            ActiveDockable = _documentDock,
            DefaultDockable = _documentDock,
            VisibleDockables = new AvaloniaList<IDockable> { _documentDock }
        };
        factory.InitLayout(rootDock);
        DockControl.Layout = rootDock;
        DockControl.Factory = factory;

        // Sync Documents collection changes to VisibleDockables
        Documents.CollectionChanged += OnDocumentsChanged;

        // Reverse-sync: when the dock itself closes a tab, remove it from Documents
        if (_documentDock.VisibleDockables is INotifyCollectionChanged notifyDockables)
            notifyDockables.CollectionChanged += OnVisibleDockablesChanged;
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_documentDock is null) return;
        _documentDock.VisibleDockables ??= new AvaloniaList<IDockable>();

        _suppressDocumentsSync = true;
        try
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    var doc = CreateDocumentForViewModel(item);
                    _documentDock.VisibleDockables.Add(doc);
                    _documentDock.ActiveDockable = doc;
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    var doc = _documentDock.VisibleDockables
                        .OfType<Document>()
                        .FirstOrDefault(d => d.Context == item);
                    if (doc is not null)
                        _documentDock.VisibleDockables.Remove(doc);
                    (item as IDisposable)?.Dispose();
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _documentDock.VisibleDockables.Clear();
            }
        }
        finally
        {
            _suppressDocumentsSync = false;
        }
    }

    private void OnVisibleDockablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressDocumentsSync) return;
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<Document>())
            {
                var vm = Documents.FirstOrDefault(d => ReferenceEquals(d, item.Context));
                if (vm is not null)
                    Documents.Remove(vm);
            }
        }
    }

    private static Document CreateDocumentForViewModel(object viewModel)
    {
        // Resolve Title and CanClose via duck-typing on the view model
        var title = (viewModel as dynamic)?.Title as string ?? viewModel.ToString() ?? "Document";
        var canClose = (viewModel as dynamic)?.CanClose is bool b ? b : true;

        return new Document
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            CanClose = canClose,
            Context = viewModel,
            Content = new Func<IServiceProvider, object>(_ =>
                new ContentControl { Content = viewModel })
        };
    }

    /// <summary>
    /// Initialize the window with the runtime once it's loaded
    /// </summary>
    public void InitializeWithRuntime(XTMFRuntime runtime)
    {
        _runtime = runtime;
        _isLoading = false;
        UpdateLoadingState();

        // Add the Projects tab as the permanent first document
        Documents.Add(new ProjectsViewModel(runtime));
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
            // Find the corresponding document in the dock and activate it
            var (dock, doc) = GetViewAndDocFromModel(DockControl.Layout!, existing);
            if (doc is not null)
            {
                // Activate the document
                dock?.ActiveDockable = doc;
            }
            return;
        }
        // No existing tab, create a new one
        var user = _runtime.UserController.Users[0];
        Documents.Add(new ModelSystemsViewModel(_runtime, user, project, this));
    }

    /// <summary>
    /// Finds a document within a dock hierarchy that matches the given view model.
    /// </summary>
    /// <param name="dock">The dock to search within.</param>
    /// <param name="viewModel">The view model to match.</param>
    /// <returns>A tuple containing the dock and document if found; otherwise, (null, null).</returns>
    private static (IDock? Dock, IDocument? document) GetViewAndDocFromModel(IDock dock, object viewModel)
    {
        // TODO: Consider moving this into a utility class.
        foreach (var d in dock.VisibleDockables ?? Enumerable.Empty<IDockable>())
        {
            if (d is IDocument doc && doc.Context == viewModel)
                return (dock, doc);

            if (d is IDock childDock)
            {
                var result = GetViewAndDocFromModel(childDock, viewModel);
                if (result != (null, null))
                    return result;
            }
        }

        return (null, null);
    }


    /// <summary>
    /// Opens a new tab for the given model system editing session.
    /// If a tab for the same <see cref="ModelSystemHeader"/> is already open it is
    /// activated instead and <paramref name="session"/> is disposed.
    /// </summary>
    public void OpenModelSystemTab(ModelSystemSession session)
    {
        // Reuse an existing tab for the same model system, if any.
        var existing = Documents
            .OfType<ModelSystemEditorViewModel>()
            .FirstOrDefault(vm => vm.ModelSystemHeader == session.ModelSystemHeader);

        if (existing is not null)
        {
            // Dispose the caller's session; the open tab already owns one.
            session.Dispose();
            var (dock, doc) = GetViewAndDocFromModel(DockControl.Layout!, existing);
            if (doc is not null)
                dock!.ActiveDockable = doc;
            return;
        }

        Documents.Add(new ModelSystemEditorViewModel(session));
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
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (s, _) => _settingsWindow = null;
            _settingsWindow.ShowDialog(this);
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    [RelayCommand]
    private void LaunchAboutDialog()
    {
        var aboutDialog = new AboutDialog();
        aboutDialog.ShowDialog(this);
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

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
        if (_documentDock?.ActiveDockable is IDocument activeDoc
            && activeDoc.CanClose
            && activeDoc.Context is object context)
        {
            if (DockControl.Layout is IDock currentDock
                && currentDock.CanGoBack)
            {
                var goBackCommand = currentDock.GoBack;
                if(goBackCommand.CanExecute(currentDock))
                {
                    goBackCommand.Execute(currentDock);
                }
            }
            // Now that we tried to move back, we can remove the active document.
            Documents.Remove(context);
        }
    }
}
