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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XTMF2;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.Tutorials;

public sealed class TutorialCatalog
{
    public IReadOnlyList<TutorialDefinition> Tutorials { get; }
    public IReadOnlyList<TutorialCatalogEntry> Entries { get; }

    private TutorialCatalog(IEnumerable<TutorialProviderRegistration> providers)
    {
        var tutorials = providers
            .SelectMany(registration => registration.Provider.GetTutorials()
                ?? throw new InvalidOperationException(
                    $"Tutorial provider '{registration.Provider.GetType().FullName}' returned no tutorial collection."),
                (registration, tutorial) => new TutorialRegistration(
                    tutorial,
                    registration.LibraryName,
                    registration.LibraryKey,
                    registration.IsProject))
            .ToArray();

        var duplicateId = tutorials
            .GroupBy(registration => registration.Tutorial.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidOperationException($"More than one tutorial provider supplied the ID '{duplicateId.Key}'.");

        var entries = new List<TutorialCatalogEntry>();
        var libraries = tutorials
            .GroupBy(registration => registration.LibraryKey)
            .Select(group => new
            {
                group.Key,
                Tutorials = group.ToArray(),
                IsProject = group.First().IsProject,
                LibraryName = group.First().LibraryName
            })
            .OrderBy(library => !library.IsProject)
            .ThenBy(library => library.LibraryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(library => library.Key, StringComparer.Ordinal);

        foreach (var library in libraries)
        {
            var orderedTutorials = library.Tutorials
                .OrderBy(registration => registration.Tutorial.Order)
                .ThenBy(registration => registration.Tutorial.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(registration => registration.Tutorial.Id, StringComparer.Ordinal)
                .ToArray();

            for (var index = 0; index < orderedTutorials.Length; index++)
            {
                var tutorial = orderedTutorials[index].Tutorial;
                entries.Add(new TutorialCatalogEntry(
                    tutorial,
                    library.LibraryName,
                    index + 1,
                    index == 0));
            }
        }

        Entries = Array.AsReadOnly(entries.ToArray());
        Tutorials = Array.AsReadOnly(entries.Select(entry => entry.Tutorial).ToArray());
    }

    public static TutorialCatalog FromModuleAssemblies(IEnumerable<Assembly> moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);

        var providers = new List<TutorialProviderRegistration>
        {
            new(new GettingStartedTutorialProvider(), "XTMF2", "XTMF2", true),
            new(new BuiltInRuntimeModuleTutorialProvider(), "XTMF2", "XTMF2", true)
        };

        foreach (var assembly in moduleAssemblies.Distinct())
        {
            var isProjectAssembly = assembly == typeof(BuiltInRuntimeModuleTutorialProvider).Assembly
                || assembly == typeof(DirectoryPath).Assembly
                || assembly == typeof(ITutorialProvider).Assembly;
            var libraryName = isProjectAssembly
                ? "XTMF2"
                : assembly.GetName().Name ?? assembly.FullName ?? "Module library";
            var libraryKey = isProjectAssembly ? "XTMF2" : assembly.FullName ?? libraryName;

            Type[] exportedTypes;
            try
            {
                exportedTypes = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                var loaderErrors = string.Join(
                    Environment.NewLine,
                    exception.LoaderExceptions
                        .Where(loaderException => loaderException is not null)
                        .Select(loaderException => loaderException!.Message));
                throw new InvalidOperationException(
                    $"Tutorial providers in '{assembly.FullName}' could not be inspected: {loaderErrors}",
                    exception);
            }

            foreach (var providerType in exportedTypes
                .Where(type => !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters)
                .Where(type => typeof(ITutorialProvider).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                if (providerType == typeof(GettingStartedTutorialProvider)
                    || providerType == typeof(BuiltInRuntimeModuleTutorialProvider))
                    continue;

                if (providerType.GetConstructor(Type.EmptyTypes) is null)
                {
                    throw new InvalidOperationException(
                        $"Tutorial provider '{providerType.FullName}' must have a public parameterless constructor.");
                }

                try
                {
                    var provider = (ITutorialProvider)(Activator.CreateInstance(providerType)
                        ?? throw new InvalidOperationException(
                            $"Tutorial provider '{providerType.FullName}' could not be created."));
                    providers.Add(new TutorialProviderRegistration(
                        provider,
                        libraryName,
                        libraryKey,
                        isProjectAssembly));
                }
                catch (TargetInvocationException exception)
                {
                    throw new InvalidOperationException(
                        $"Tutorial provider '{providerType.FullName}' failed during construction.",
                        exception.InnerException ?? exception);
                }
            }
        }

        return new TutorialCatalog(providers);
    }

    private sealed record TutorialProviderRegistration(
        ITutorialProvider Provider,
        string LibraryName,
        string LibraryKey,
        bool IsProject);

    private sealed record TutorialRegistration(
        TutorialDefinition Tutorial,
        string LibraryName,
        string LibraryKey,
        bool IsProject);
}

public sealed class TutorialCatalogEntry
{
    public TutorialDefinition Tutorial { get; }
    public string LibraryName { get; }
    public int SequenceNumber { get; }
    public bool IsFirstInLibrary { get; }

    internal TutorialCatalogEntry(
        TutorialDefinition tutorial,
        string libraryName,
        int sequenceNumber,
        bool isFirstInLibrary)
    {
        Tutorial = tutorial;
        LibraryName = libraryName;
        SequenceNumber = sequenceNumber;
        IsFirstInLibrary = isFirstInLibrary;
    }
}

public static class TutorialTargetIds
{
    public const string Canvas = "canvas";
    public const string SaveButton = "save-button";
    public const string RunButton = "run-button";
    public const string EstimationSettingsButton = "estimation-settings-button";
    public const string EstimationRunButton = "estimation-run-button";
    public const string CalibrationSettingsButton = "calibration-settings-button";
    public const string CalibrationRunButton = "calibration-run-button";
}

public sealed class GettingStartedTutorialProvider : ITutorialProvider
{
    public const string TutorialId = "xtmf2.getting-started";

    public IEnumerable<TutorialDefinition> GetTutorials()
    {
        yield return new TutorialDefinition(
            TutorialId,
            "Getting Started",
            "You created a runnable model system with a Start, a Sleep module node, and a comment, then completed a run.",
            [
                new TutorialStepDefinition(
                    "welcome",
                    "Welcome to XTMF2",
                    "This guided lesson uses a separate tutorial project. You can try the editor without changing your own projects.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "The separate project lets you practice without changing your own model systems."),
                new TutorialStepDefinition(
                    "add-start",
                    "Add a Start",
                    "A Start is an entry point for running a model system. Right-click the canvas and choose Add Start.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.StartAdded),
                    "Start marks where execution begins; unlike a regular node, it is not an instance of a module.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureStart("Start 1")
                },
                new TutorialStepDefinition(
                    "add-sleep-node",
                    "Add a module node",
                    "Use Ctrl+M and select the built-in Sleep module. Leave its generated duration at the default 1000 ms.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(Sleep).AssemblyQualifiedName),
                    "Each node is an instance of a module. Sleep is an action module, and its duration parameter controls how long it waits.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(typeof(Sleep), "Sleep")
                },
                new TutorialStepDefinition(
                    "connect-start",
                    "Connect Start to the node",
                    "Right-click and drag from the Start node to the Sleep module node.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "ToExecute"),
                    "This connection defines the execution flow: it tells Start which action to run.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Start 1", "ToExecute", "Sleep")
                },
                new TutorialStepDefinition(
                    "add-comment",
                    "Add a comment",
                    "Add a comment block with Ctrl+N or the canvas context menu. It has a header and a body: give it a short title, then use the body for a note such as why this model waits briefly and then finishes.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.CommentAdded),
                    "Comments document intent for people editing the model; they do not affect execution.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureComment(
                        "Model behavior",
                        "This model waits briefly and then finishes.")
                },
                new TutorialStepDefinition(
                    "save-model-system",
                    "Save your model system",
                    "Save the runnable model system with Ctrl+S or the Save button.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemSaved),
                    "Saving preserves your changes so the model system can be reopened and run later.",
                    TutorialTargetIds.SaveButton)
                {
                    SkipAction = context => context.SaveModelSystem()
                },
                new TutorialStepDefinition(
                    "run-model-system",
                    "Run your model system",
                    "Press F5 or choose Run. In the run dialog, select Start 1 and the Local RunServer, then start the run. The Runs view opens; wait for Finished and return to the tutorial editor.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemRunCompleted),
                    "A successful run confirms that XTMF2 follows the Start-to-Sleep connection and invokes the module with its configured duration.",
                    TutorialTargetIds.RunButton)
            ],
            order: 1);
    }
}
