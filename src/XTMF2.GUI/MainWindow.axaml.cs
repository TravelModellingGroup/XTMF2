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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTMF2;
using XTMF2.AI;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.Editing;
using XTMF2.GUI.AI;
using XTMF2.GUI.Controls;
using XTMF2.GUI.Resources;
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
    private RunServerActivityWindow? _runServerActivityWindow;
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
    private readonly List<object> _documentActivationHistory = new();
    private readonly Dictionary<ModelSystemHeader, PendingEditorLoad> _pendingEditorLoads = new();
    private object? _lastActiveDocumentContext;
    private bool _suppressDocumentActivationHistory;
    private bool _documentFocusRestoreScheduled;
    private int _documentFocusRestoreGeneration;
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
        if (e.Action is not (NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
            || e.OldItems is null)
        {
            return;
        }

        var removedActiveDocument = false;
        for (var index = 0; index < e.OldItems.Count; index++)
        {
            if (e.OldItems[index] is not object oldItem)
                continue;

            var replacement = e.Action == NotifyCollectionChangedAction.Replace
                && e.NewItems is not null
                && index < e.NewItems.Count
                    ? e.NewItems[index]
                    : null;
            var historyIndex = _documentActivationHistory.IndexOf(oldItem);
            if (replacement is object newItem)
            {
                if (historyIndex >= 0)
                    _documentActivationHistory[historyIndex] = newItem;
                if (ReferenceEquals(oldItem, _lastActiveDocumentContext))
                    _lastActiveDocumentContext = newItem;
            }
            else
            {
                if (ReferenceEquals(oldItem, _lastActiveDocumentContext))
                    removedActiveDocument = true;
                _documentActivationHistory.Remove(oldItem);
            }

            (oldItem as IDisposable)?.Dispose();
        }

        if (removedActiveDocument)
        {
            _lastActiveDocumentContext = null;
            ScheduleDocumentFocusRestore();
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
            RemoveDocumentFromCollection(context);
            CloseEmptyFloatingWindows();
        }
    }

    private void RemoveDocumentFromCollection(object context)
    {
        if (!Documents.Contains(context))
            return;

        if (ReferenceEquals(context, _lastActiveDocumentContext))
            _suppressDocumentActivationHistory = true;

        Documents.Remove(context);
    }

    private void ScheduleDocumentFocusRestore()
    {
        _suppressDocumentActivationHistory = true;
        _documentFocusRestoreScheduled = true;
        var generation = ++_documentFocusRestoreGeneration;
        Dispatcher.UIThread.Post(
            () => RestoreMostRecentDocument(generation),
            DispatcherPriority.Background);
    }

    private void RestoreMostRecentDocument(int generation)
    {
        if (generation != _documentFocusRestoreGeneration)
            return;

        _documentFocusRestoreScheduled = false;
        object? restoredContext = null;
        foreach (var context in _documentActivationHistory.ToArray())
        {
            if (Documents.Contains(context) && ActivateDocument(context, isFocusRestore: true))
            {
                restoredContext = context;
                break;
            }
        }

        if (restoredContext is not null)
        {
            RecordActiveDocument(restoredContext);
        }
        else if (_activeDockable is IDocument { Context: object activeContext }
            && Documents.Contains(activeContext))
        {
            RecordActiveDocument(activeContext);
        }
        else
        {
            _lastActiveDocumentContext = null;
        }

        _suppressDocumentActivationHistory = false;
    }

    private void CancelPendingDocumentFocusRestore()
    {
        if (!_documentFocusRestoreScheduled)
            return;

        _documentFocusRestoreScheduled = false;
        _documentFocusRestoreGeneration++;
        _suppressDocumentActivationHistory = false;
    }

    private void RecordActiveDocument(object context)
    {
        if (!Documents.Contains(context))
            return;

        _documentActivationHistory.Remove(context);
        _documentActivationHistory.Insert(0, context);
        _lastActiveDocumentContext = context;
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
            if (IsEditorOpen(editor))
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
            .Select(GetEditorViewModel)
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
                && GetEditorViewModel(document.Context) is { } other
                && other.ModelSystemHeader == editor.ModelSystemHeader);
    }

    private static ModelSystemEditorViewModel? GetEditorViewModel(object? documentContext)
        => documentContext switch
        {
            ModelSystemEditorViewModel editor when editor.IsLoadingHost => editor.LoadedEditorViewModel,
            ModelSystemEditorViewModel editor => editor,
            _ => null
        };

    private object? FindEditorDocumentContext(ModelSystemEditorViewModel editor)
        => EnumerateOpenDocuments()
            .Select(document => document.Context)
            .FirstOrDefault(context => ReferenceEquals(GetEditorViewModel(context), editor));

    private bool IsEditorOpen(ModelSystemEditorViewModel editor)
        => FindEditorDocumentContext(editor) is not null;

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
            .Select(GetEditorViewModel)
            .OfType<ModelSystemEditorViewModel>())
            editor.SetRunController(_runController);

        // Add the Runs tab so it is always visible.
        if (_runController is not null)
        {
            Documents.Add(_runController.RunsViewModel);
            _runController.EnableAutomaticRecoveredRunBinding(user);
        }

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
    public async Task OpenProjectTab(Project project)
    {
        if (_runtime is null || _currentUser is null)
            return;

        var runtime = _runtime;
        var user = _currentUser;
        var existingDocument = Documents
            .OfType<ModelSystemsViewModel>()
            .FirstOrDefault(document => document.Project == project);

        if (existingDocument is not null)
        {
            if (existingDocument.LoadErrorMessage is null)
            {
                ActivateDocumentWhenAvailable(existingDocument);
                return;
            }

            RemoveDocumentFromCollection(existingDocument);
        }

        var loadingViewModel = ModelSystemsViewModel.CreateLoading(runtime, user, project, this);
        Documents.Add(loadingViewModel);
        ActivateDocumentWhenAvailable(loadingViewModel);

        var cancellationToken = loadingViewModel.LoadCancellationToken;
        ModelSystemsViewModel? modelSystemsViewModel = null;
        try
        {
            modelSystemsViewModel = await Task.Run(
                () => new ModelSystemsViewModel(runtime, user, project, this),
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || !Documents.Contains(loadingViewModel))
            {
                modelSystemsViewModel.Dispose();
                modelSystemsViewModel = null;
                return;
            }

            loadingViewModel.SetLoadedViewModel(modelSystemsViewModel);
            modelSystemsViewModel = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedDocumentLoadFailure(exception))
        {
            if (Documents.Contains(loadingViewModel))
                loadingViewModel.ShowLoadError(exception.Message);
        }
        finally
        {
            modelSystemsViewModel?.Dispose();
            loadingViewModel.CompleteLoading();
        }
    }

    private bool ActivateDocument(object viewModel)
        => ActivateDocument(viewModel, isFocusRestore: false);

    private bool ActivateDocument(object viewModel, bool isFocusRestore)
    {
        if (_factory?.GetContainerFromItem(viewModel) is not IDocument document
            || document.Owner is not IDock dock)
            return false;

        if (!isFocusRestore)
            CancelPendingDocumentFocusRestore();

        dock.ActiveDockable = document;
        if (!isFocusRestore)
            RecordActiveDocument(viewModel);

        return true;
    }

    private void ActivateDocumentWhenAvailable(object document)
    {
        if (ActivateDocument(document))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (Documents.Contains(document))
                ActivateDocument(document);
        }, DispatcherPriority.Loaded);
    }


    /// <summary>
    /// Opens a model system editor after immediately showing a loading tab.
    /// </summary>
    public Task<ModelSystemEditorViewModel?> OpenModelSystemTabAsync(
        ModelSystemsViewModel modelSystemsViewModel,
        ModelSystemHeader header)
    {
        if (FindOpenEditor(header) is { } existing)
        {
            FocusEditorTab(existing);
            return Task.FromResult<ModelSystemEditorViewModel?>(existing);
        }

        if (_pendingEditorLoads.TryGetValue(header, out var existingLoad))
        {
            if (Documents.Contains(existingLoad.Document))
            {
                ActivateDocumentWhenAvailable(existingLoad.Document);
                return existingLoad.Task;
            }

            _pendingEditorLoads.Remove(header);
        }

        RemoveFailedEditorLoad(header);

        if (_runtime is null)
            return Task.FromResult<ModelSystemEditorViewModel?>(null);

        var runtime = _runtime;
        var project = modelSystemsViewModel.Project;
        var loadingDocument = CreateEditorLoadingViewModel(header, modelSystemsViewModel.CurrentUser);
        var user = modelSystemsViewModel.CurrentUser;
        return StartEditorLoad(
            loadingDocument,
            header,
            user,
            cancellationToken => Task.Run(
                () => LoadModelSystemSession(runtime, project, user, header),
                cancellationToken));
    }

    private static ModelSystemSessionLoadResult LoadModelSystemSession(
        XTMFRuntime runtime,
        Project project,
        User user,
        ModelSystemHeader header)
    {
        if (!runtime.ProjectController.GetProjectSession(user, project, out var projectSession, out var projectError)
            || projectSession is null)
        {
            return new(null, projectError?.Message, null);
        }

        using (projectSession)
        {
            if (!projectSession.EditModelSystem(user, header, out var session, out var error, out var warnings))
                return new(null, error?.Message, warnings);

            return new(session, null, warnings);
        }
    }

    public Task<ModelSystemEditorViewModel?> OpenModelSystemTabAndGetAsync(ModelSystemSession session, User user)
    {
        var header = session.ModelSystemHeader;
        var existing = FindOpenEditor(header);
        if (existing is not null)
        {
            FocusEditorTab(existing);
            return Task.FromResult<ModelSystemEditorViewModel?>(existing);
        }

        if (_pendingEditorLoads.TryGetValue(header, out var existingLoad))
        {
            if (Documents.Contains(existingLoad.Document))
            {
                ActivateDocumentWhenAvailable(existingLoad.Document);
                return existingLoad.Task;
            }

            _pendingEditorLoads.Remove(header);
        }

        RemoveFailedEditorLoad(header);

        if (_runtime is null)
            return Task.FromResult<ModelSystemEditorViewModel?>(null);

        var runtime = _runtime;
        var project = session.Project;
        var loadingDocument = CreateEditorLoadingViewModel(header, user);
        return StartEditorLoad(
            loadingDocument,
            header,
            user,
            cancellationToken => Task.Run(
                () => LoadModelSystemSession(runtime, project, user, header),
                cancellationToken));
    }

    private ModelSystemEditorViewModel? FindOpenEditor(ModelSystemHeader header)
        => EnumerateOpenDocuments()
            .Select(document => document.Context)
            .Select(GetEditorViewModel)
            .OfType<ModelSystemEditorViewModel>()
            .FirstOrDefault(editor => editor.ModelSystemHeader == header);

    public ModelSystemEditorViewModel? FindOpenEditorTab(ModelSystemHeader header)
        => FindOpenEditor(header);

    private void RemoveFailedEditorLoad(ModelSystemHeader header)
    {
        var failedLoad = Documents
            .OfType<ModelSystemEditorViewModel>()
            .FirstOrDefault(document => document.IsLoadingHost
                && document.HasLoadError
                && document.ModelSystemHeader == header);
        if (failedLoad is not null)
            RemoveDocumentFromCollection(failedLoad);
    }

    private ModelSystemEditorViewModel CreateEditorLoadingViewModel(ModelSystemHeader header, User user)
    {
        var loadingDocument = new ModelSystemEditorViewModel(header, user, _runController);
        Documents.Add(loadingDocument);
        ActivateDocumentWhenAvailable(loadingDocument);
        return loadingDocument;
    }

    private Task<ModelSystemEditorViewModel?> StartEditorLoad(
        ModelSystemEditorViewModel loadingDocument,
        ModelSystemHeader header,
        User user,
        Func<CancellationToken, Task<ModelSystemSessionLoadResult>> loadSession)
    {
        var task = LoadModelSystemEditorAsync(loadingDocument, header, user, loadSession);
        _pendingEditorLoads.Add(header, new PendingEditorLoad(loadingDocument, task));
        return task;
    }

    private async Task<ModelSystemEditorViewModel?> LoadModelSystemEditorAsync(
        ModelSystemEditorViewModel loadingDocument,
        ModelSystemHeader header,
        User user,
        Func<CancellationToken, Task<ModelSystemSessionLoadResult>> loadSession)
    {
        var cancellationToken = loadingDocument.LoadCancellationToken;
        ModelSystemSession? session = null;
        ModelSystemEditorViewModel? editor = null;
        try
        {
            var result = await loadSession(cancellationToken);
            session = result.Session;
            if (session is null)
            {
                if (Documents.Contains(loadingDocument))
                    loadingDocument.ShowLoadError(result.ErrorMessage ?? Strings.ModelSystems_UnknownError);
                return null;
            }

            if (cancellationToken.IsCancellationRequested || !Documents.Contains(loadingDocument))
                return null;

            if (result.Warnings is { Count: > 0 })
            {
                loadingDocument.LoadingStatus = Strings.DocumentLoad_CheckingStatus;
                var warningDialog = new ValidationIssueDialog(
                    Strings.ModelSystems_OpenFailedTitle,
                    string.Join(Environment.NewLine, result.Warnings));
                await warningDialog.ShowDialog(this);
                if (!warningDialog.Result)
                {
                    RemoveDocumentFromCollection(loadingDocument);
                    return null;
                }
            }

            if (cancellationToken.IsCancellationRequested || !Documents.Contains(loadingDocument))
                return null;

            loadingDocument.LoadingStatus = Strings.DocumentLoad_PreparingEditorStatus;
            editor = await Task.Run(() => CreateEditorViewModel(session, user), cancellationToken);
            session = null;
            editor.RunStarted = SwitchToRunsDocument;

            if (cancellationToken.IsCancellationRequested || !Documents.Contains(loadingDocument))
            {
                return null;
            }

            var loadedEditor = editor;
            loadingDocument.SetLoadedEditorViewModel(editor);
            if (_activeDockable is IDocument activeDocument
                && ReferenceEquals(activeDocument.Context, loadingDocument))
            {
                SetActiveEditorViewModel(editor);
            }

            editor = null;
            return loadedEditor;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (IsExpectedDocumentLoadFailure(exception))
        {
            if (Documents.Contains(loadingDocument))
                loadingDocument.ShowLoadError(exception.Message);
            return null;
        }
        finally
        {
            editor?.Dispose();
            session?.Dispose();
            loadingDocument.CompleteLoading();
            if (_pendingEditorLoads.TryGetValue(header, out var pendingLoad)
                && ReferenceEquals(pendingLoad.Document, loadingDocument))
            {
                _pendingEditorLoads.Remove(header);
            }
        }
    }

    private static bool IsExpectedDocumentLoadFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or FormatException
            or NotSupportedException
            or System.Text.Json.JsonException
            or System.Reflection.ReflectionTypeLoadException
            or TypeLoadException;

    private sealed record ModelSystemSessionLoadResult(
        ModelSystemSession? Session,
        string? ErrorMessage,
        List<string>? Warnings);

    private sealed record PendingEditorLoad(
        ModelSystemEditorViewModel Document,
        Task<ModelSystemEditorViewModel?> Task);

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
        if (FindEditorDocumentContext(editorVm) is { } documentContext)
            ActivateDocument(documentContext);
    }

    public bool BindRecoveredRunToActiveModelSystem(string runId, out string? error)
    {
        if (_activeEditorVm is null || _currentUser is null || _runController is null)
        {
            error = "This run could not be matched automatically. Open its model system, make it active, and bind it manually.";
            return false;
        }
        return _runController.BindRecoveredRemoteRun(runId, _activeEditorVm.Session,
            _activeEditorVm.User, out error);
    }

    public bool RetryRemoteRunReceipt(string runId, out string? error)
    {
        if (_runController is null)
        {
            error = "The run controller is unavailable.";
            return false;
        }
        return _runController.RetryRemoteRunReceipt(runId, out error);
    }

    public bool RequestRemoteRunOutputTransfer(string runId, out string? error)
    {
        if (_runController is null)
        {
            error = "The run controller is unavailable.";
            return false;
        }
        return _runController.RequestRemoteRunOutputTransfer(runId, out error);
    }

    public Task<RemoteRunDeletionResponse> DeleteRemoteRunAsync(string runId)
        => _runController is null
            ? Task.FromResult(new RemoteRunDeletionResponse(string.Empty, runId, false,
                "The run controller is unavailable."))
            : _runController.DeleteRemoteRunAsync(runId);

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
        var documentContext = Documents
            .OfType<ModelSystemsViewModel>()
            .FirstOrDefault(document => ReferenceEquals(document.Project, project));

        if (documentContext is not null)
            RemoveDocumentFromCollection(documentContext);
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

    private void RunServerActivity_Click(object? sender, RoutedEventArgs e)
    {
        if (_runServerActivityWindow is not null && _runServerActivityWindow.IsVisible)
        {
            _runServerActivityWindow.Activate();
            return;
        }

        _runServerActivityWindow = new RunServerActivityWindow(_runController);
        _runServerActivityWindow.Closed += (_, _) => _runServerActivityWindow = null;
        _runServerActivityWindow.Show(this);
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
        if (!_suppressDocumentActivationHistory
            && e.Dockable is IDocument { Context: object documentContext })
        {
            RecordActiveDocument(documentContext);
        }

        SetActiveEditorViewModel(GetEditorViewModel((e.Dockable as IDocument)?.Context));
    }

    private void SetActiveEditorViewModel(ModelSystemEditorViewModel? newVm)
    {
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
                    RemoveDocumentFromCollection(context);
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
            if (IsEditorOpen(editor))
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

            var documentContext = FindEditorDocumentContext(editor);
            if (documentContext is null)
                return;

            _allowDocumentClose = true;
            RemoveDocumentFromCollection(documentContext);
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
            foreach (var editor in EnumerateOpenDocuments()
                .Select(document => GetEditorViewModel(document.Context))
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

            if (_runController is not null)
            {
                IReadOnlyList<RunServerActivity> localActivities;
                try
                {
                    localActivities = await _runController.QueryLocalRunServerActivityAsync();
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or
                    ObjectDisposedException or InvalidOperationException)
                {
                    var confirmation = new ConfirmDialog("Local RunServer Status Unavailable",
                        $"The local RunServer's workload could not be checked ({exception.Message}). " +
                        "Closing may terminate a running model system. Close the program anyway?");
                    await confirmation.ShowDialog(this);
                    if (!confirmation.Result)
                        return;
                    localActivities = Array.Empty<RunServerActivity>();
                }

                if (localActivities.Count > 0)
                {
                    var runningCount = localActivities.Count(activity => activity.State == RunServerActivityState.Running);
                    var queuedCount = localActivities.Count(activity => activity.State == RunServerActivityState.Queued);
                    var workload = new List<string>();
                    if (runningCount > 0)
                        workload.Add($"{runningCount} running");
                    if (queuedCount > 0)
                        workload.Add($"{queuedCount} queued");
                    var confirmation = new ConfirmDialog("Local RunServer Is Busy",
                        $"The local RunServer has {string.Join(" and ", workload)} model system(s). " +
                        "Closing the program will stop the Local RunServer and terminate this work. Close anyway?");
                    await confirmation.ShowDialog(this);
                    if (!confirmation.Result)
                        return;
                }
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
            if (GetEditorViewModel(activeDoc.Context) is { IsDirty: true } editor)
            {
                _ = PromptToCloseEditorAsync(editor);
                return;
            }

            // Now that we tried to move back, we can remove the active document.
            RemoveDocumentFromCollection(context);
        }
    }
}
