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
using XTMF2;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.Tutorials;

public sealed class BuiltInRuntimeModuleTutorialProvider : ITutorialProvider
{
    public IEnumerable<TutorialDefinition> GetTutorials()
    {
        yield return new TutorialDefinition(
            "xtmf2.runtime.log-messages",
            "Write Messages to the Run Log",
            "You connected a Log to the run-status stream, wrote two messages, saved the model system, and completed a run.",
            [
                new TutorialStepDefinition(
                    "welcome",
                    "Write Messages to the Run Log",
                    "This tutorial builds a small action chain that writes two messages to the run's status output.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "Logging has three parts: a message-producing action, a Log that timestamps messages, and a stream that determines where they go."),
                new TutorialStepDefinition(
                    "add-start",
                    "Add a Start",
                    "Right-click the canvas and choose Add Start. Name it Start 1.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.StartAdded),
                    "Start is the entry point that invokes the first action in the model system.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureStart("Start 1")
                },
                new TutorialStepDefinition(
                    "add-run-status-stream",
                    "Add a run-status stream",
                    "Use Ctrl+M to add the built-in OpenWriteStreamToRunStatus module. Name it Run Status Stream.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(OpenWriteStreamToRunStatus).AssemblyQualifiedName),
                    "This module provides a WriteStream that forwards log output to the active run, so no file path is needed.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(
                        typeof(OpenWriteStreamToRunStatus),
                        "Run Status Stream")
                },
                new TutorialStepDefinition(
                    "add-log",
                    "Add a Log",
                    "Use Ctrl+M to add the built-in Log module and name it Log.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(Log).AssemblyQualifiedName),
                    "Log timestamps each message and writes it to the stream connected to its required LogStream hook.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(typeof(Log), "Log")
                },
                new TutorialStepDefinition(
                    "connect-log-stream",
                    "Choose where Log writes",
                    "Right-click Log's LogStream hook and drag to the Run Status Stream module.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "LogStream"),
                    "LogStream supplies the destination stream that receives every message.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Log", "LogStream", "Run Status Stream")
                },
                new TutorialStepDefinition(
                    "add-first-message",
                    "Add the first message action",
                    "Use Ctrl+M to add the built-in Write to Log action and name it Write Message 1.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(WriteToLogA).AssemblyQualifiedName),
                    "Write to Log is an action: when execution reaches it, it writes its Message through the connected Log.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(typeof(WriteToLogA), "Write Message 1")
                },
                new TutorialStepDefinition(
                    "connect-first-message-to-log",
                    "Connect the first action to Log",
                    "Right-click the first Write to Log action's Log hook and drag to the Log module.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "Log"),
                    "The action uses this connection to find the Log that will write its message.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Write Message 1", "Log", "Log")
                },
                new TutorialStepDefinition(
                    "connect-start-to-first-message",
                    "Start the message sequence",
                    "Right-click and drag from Start 1 to the first Write to Log action.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "ToExecute"),
                    "Start nodes do not display hooks; dragging from the Start connects execution to the first action.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Start 1", "ToExecute", "Write Message 1")
                },
                new TutorialStepDefinition(
                    "configure-first-message",
                    "Set the first message",
                    "Set the first action's generated Message parameter to: The run has started. Click Next when you are ready.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "The Message parameter is the text this action passes to Log."),
                new TutorialStepDefinition(
                    "add-second-message",
                    "Add a second message action",
                    "Use Ctrl+M to add another built-in Write to Log action and name it Write Message 2.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(WriteToLogA).AssemblyQualifiedName),
                    "A second action lets the model report more than one event during the same run.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(typeof(WriteToLogA), "Write Message 2")
                },
                new TutorialStepDefinition(
                    "connect-second-message-to-log",
                    "Connect the second action to Log",
                    "Right-click the second Write to Log action's Log hook and drag to the Log module.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "Log"),
                    "Both actions can share one Log and one output stream.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Write Message 2", "Log", "Log")
                },
                new TutorialStepDefinition(
                    "chain-messages",
                    "Run the second action next",
                    "Right-click the first action's To Invoke hook and drag to the second Write to Log action.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "To Invoke"),
                    "To Invoke passes execution to the next action after the first message is written.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Write Message 1", "To Invoke", "Write Message 2")
                },
                new TutorialStepDefinition(
                    "configure-second-message",
                    "Set the second message",
                    "Set the second action's generated Message parameter to: The logging sequence is complete. Click Next when you are ready.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "The two actions now form a small, ordered sequence of log messages."),
                new TutorialStepDefinition(
                    "save",
                    "Save the model system",
                    "Save the runnable logging model with Ctrl+S or the Save button.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemSaved),
                    "Saving preserves the action chain, message text, and log destination.",
                    TutorialTargetIds.SaveButton)
                {
                    SkipAction = context => context.SaveModelSystem()
                },
                new TutorialStepDefinition(
                    "run-model-system",
                    "Run and inspect the messages",
                    "Press F5 or choose Run. Select Start 1 and the Local RunServer, then start the run. In the Runs view, select the run and confirm both messages appear in its output before returning to the tutorial.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemRunCompleted),
                    "A successful run confirms that Start invokes both actions, Log writes both messages, and the run-status stream delivers them to the client.",
                    TutorialTargetIds.RunButton)
            ],
            order: 2);

        yield return new TutorialDefinition(
            "xtmf2.runtime.execute-actions",
            "Log Each Execute Iteration",
            "You used Execute to repeat a logging action and scripted its message with the current iteration number.",
            [
                new TutorialStepDefinition(
                    "welcome",
                    "Log Each Execute Iteration",
                    "Build a model that logs the current iteration number each time Execute runs its action sequence.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "Execute supplies the current iteration value; a scripted parameter can read that value to create a different message each time."),
                new TutorialStepDefinition(
                    "add-start",
                    "Add a Start",
                    "Right-click the canvas and choose Add Start. Name it Start 1.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.StartAdded),
                    "Start is the entry point for execution.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureStart("Start 1")
                },
                new TutorialStepDefinition(
                    "add-execute",
                    "Add Execute",
                    "Use Ctrl+M to add the built-in Execute module and name it Execute.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(Execute).AssemblyQualifiedName),
                    "Execute runs its To Execute actions for each iteration.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(typeof(Execute), "Execute")
                },
                new TutorialStepDefinition(
                    "connect-start",
                    "Connect Start to Execute",
                    "Right-click and drag from Start 1 to the Execute module.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "ToExecute"),
                    "Start nodes do not display hooks; this connection enters the Execute action sequence.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Start 1", "ToExecute", "Execute")
                },
                new TutorialStepDefinition(
                    "add-log",
                    "Add a Log",
                    "Use Ctrl+M to add the built-in Log module and name it Log.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(Log).AssemblyQualifiedName),
                    "Log timestamps each message and writes it to its connected stream.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(typeof(Log), "Log")
                },
                new TutorialStepDefinition(
                    "add-status-stream",
                    "Add a run-status stream",
                    "Use Ctrl+M to add the built-in OpenWriteStreamToRunStatus module. Name it Run Status Stream.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(OpenWriteStreamToRunStatus).AssemblyQualifiedName),
                    "This stream sends Log messages to the active run without requiring a file path.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(
                        typeof(OpenWriteStreamToRunStatus),
                        "Run Status Stream")
                },
                new TutorialStepDefinition(
                    "connect-log-stream",
                    "Connect Log to the run output",
                    "Right-click Log's LogStream hook and drag to Run Status Stream.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "LogStream"),
                    "The LogStream hook chooses where the logger sends its messages.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Log", "LogStream", "Run Status Stream")
                },
                new TutorialStepDefinition(
                    "add-current-iteration",
                    "Add an iteration value",
                    "Use Ctrl+M to add Setable Parameter<int> and name it CurrentIteration.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(SetableParameter<int>).AssemblyQualifiedName),
                    "Setable Parameter stores a value that Execute can update at runtime.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(
                        typeof(SetableParameter<int>),
                        "CurrentIteration")
                },
                new TutorialStepDefinition(
                    "connect-current-iteration",
                    "Connect Execute to the iteration value",
                    "Right-click Execute's Current Iteration hook and drag to CurrentIteration.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "Current Iteration"),
                    "Execute writes the current index into this settable value before invoking the actions for each iteration.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink(
                        "Execute",
                        "Current Iteration",
                        "CurrentIteration")
                },
                new TutorialStepDefinition(
                    "make-iteration-variable",
                    "Make the iteration value available",
                    "Set CurrentIteration's initial value to 0, then right-click the node and choose Add to Model System Variables.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "The initial value gives the expression compiler an integer type. Execute overwrites it before each iteration, and adding it as a model-system variable makes it available by name to the message expression.",
                    TutorialTargetIds.Canvas),
                new TutorialStepDefinition(
                    "add-log-action",
                    "Add a logging action",
                    "Use Ctrl+M to add the built-in Write to Log action and name it Log Iteration.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(WriteToLogA).AssemblyQualifiedName),
                    "The action will run once per Execute iteration and write its message through Log.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureModule(typeof(WriteToLogA), "Log Iteration")
                },
                new TutorialStepDefinition(
                    "connect-log-action",
                    "Add the action to Execute",
                    "Right-click Execute's To Execute hook and drag to Log Iteration.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "To Execute"),
                    "To Execute contains the actions that Execute runs during every iteration.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Execute", "To Execute", "Log Iteration")
                },
                new TutorialStepDefinition(
                    "connect-action-to-log",
                    "Connect the action to Log",
                    "Right-click Log Iteration's Log hook and drag to Log.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "Log"),
                    "The action sends each generated message to this shared Log module.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Log Iteration", "Log", "Log")
                },
                new TutorialStepDefinition(
                    "script-message",
                    "Script the iteration message",
                    "Right-click Log Iteration's generated Message parameter, choose Switch to Scripted Parameter, then set its expression to: \"Iteration: \" + CurrentIteration",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "String concatenation converts the integer value into text. Use this expression rather than CurrentIteration.ToString().",
                    TutorialTargetIds.Canvas),
                new TutorialStepDefinition(
                    "configure-iterations",
                    "Choose the number of iterations",
                    "Set Execute's generated Iterations parameter to 3. Leave Parallel Execution off so the messages arrive in a predictable order.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "CurrentIteration starts at 0, so three iterations should log 0, 1, and 2."),
                new TutorialStepDefinition(
                    "save",
                    "Save the model system",
                    "Save the iteration logging model with Ctrl+S or the Save button.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemSaved),
                    "Saving preserves the iteration count, scripted message, and logging connections.",
                    TutorialTargetIds.SaveButton)
                {
                    SkipAction = context => context.SaveModelSystem()
                },
                new TutorialStepDefinition(
                    "run-model-system",
                    "Run and inspect the iterations",
                    "Press F5 or choose Run. Select Start 1 and the Local RunServer, then start the run. In the Runs view, confirm the output contains Iteration: 0, Iteration: 1, and Iteration: 2, and that the run finishes successfully.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemRunCompleted),
                    "The three messages show that Execute updated CurrentIteration before each action and that the scripted Message expression was evaluated for each run through the sequence.",
                    TutorialTargetIds.RunButton)
            ],
            order: 3);

        yield return new TutorialDefinition(
            "xtmf2.runtime.function-templates",
            "Reuse an Iteration Logger",
            "Starting from the completed iteration logger, you turned its logging action into a reusable function template and used it twice in Execute.",
            [
                new TutorialStepDefinition(
                    "welcome",
                    "Reuse an Iteration Logger",
                    "This model starts in the same finished state as tutorial 3. Select Log Iteration and extract it to a function template.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "A function template stores reusable model logic; each function instance is a place where that logic is used."),
                new TutorialStepDefinition(
                    "extract-function-template",
                    "Extract the logger into a template",
                    "Select Log Iteration, then use Ctrl+Shift+M or its context menu's Extract to Function Template command. Name the template Iteration Logger and its instance Iteration Logger.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.FunctionTemplateAdded,
                        "Iteration Logger"),
                    "Extraction moves the action into a reusable template and replaces it in Execute with a function instance. Its external Log dependency is exposed as a Function Parameter hook.",
                    TutorialTargetIds.Canvas),
                new TutorialStepDefinition(
                    "open-function-template",
                    "Open the instance's template",
                    "Extraction replaced the Write to Log action on the main canvas with a Function Instance node named Iteration Logger. This node calls the reusable function; it is different from the Function Template definition. Right-click the Iteration Logger node and choose Open Template.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.FunctionTemplateOpened),
                    "Think of the instance as a call site and the template as the implementation it calls.",
                    TutorialTargetIds.Canvas),
                new TutorialStepDefinition(
                    "inspect-function-template",
                    "Inspect the template contents",
                    "Find the Write to Log action inside the template and its Log Function Parameter. The parameter stands for the Log module supplied from outside.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                    "The template keeps its implementation separate while exposing the dependency it needs."),
                new TutorialStepDefinition(
                    "close-function-template",
                    "Return to the parent model",
                    "Use Step Out or Navigate Up to return to the model containing Execute and the function instance.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.FunctionTemplateClosed),
                    "Function instances remain in the parent model; their implementation stays inside the template.",
                    TutorialTargetIds.Canvas),
                new TutorialStepDefinition(
                    "add-second-instance",
                    "Add another instance",
                    "Use Ctrl+I or the canvas context menu to add a Function Instance. Choose Iteration Logger and name the new instance Iteration Logger 2.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.FunctionInstanceAdded,
                        "Iteration Logger"),
                    "Both instances reference the same template, so the logging behavior does not need to be rebuilt.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureFunctionInstance("Iteration Logger", "Iteration Logger 2")
                },
                new TutorialStepDefinition(
                    "connect-second-instance-log",
                    "Supply the second instance's Log",
                    "Right-click Iteration Logger 2's Log hook and drag to Log.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "Log"),
                    "The Function Parameter hook supplies the outside Log module to this instance.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink("Iteration Logger 2", "Log", "Log")
                },
                new TutorialStepDefinition(
                    "connect-second-instance-execute",
                    "Run both instances in each iteration",
                    "Right-click Execute's To Execute hook and drag to Iteration Logger 2. Execute can invoke both connected actions each iteration.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "To Execute"),
                    "The first instance is already connected from extraction; this adds the second call to the same action sequence.",
                    TutorialTargetIds.Canvas)
                {
                    SkipAction = context => context.EnsureLink(
                        "Execute",
                        "To Execute",
                        "Iteration Logger 2")
                },
                new TutorialStepDefinition(
                    "save",
                    "Save the model system",
                    "Save the function-template model with Ctrl+S or the Save button.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemSaved),
                    "Saving preserves both instances and the shared template.",
                    TutorialTargetIds.SaveButton)
                {
                    SkipAction = context => context.SaveModelSystem()
                },
                new TutorialStepDefinition(
                    "run-model-system",
                    "Run and confirm reuse",
                    "Press F5 or choose Run. Select Start 1 and the Local RunServer. Confirm the run finishes and each iteration value (0, 1, and 2) appears twice in the run output.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.ModelSystemRunCompleted),
                    "Two messages per iteration confirm that both function instances invoke the same reusable template.",
                    TutorialTargetIds.RunButton)
            ],
            order: 4,
            initializeAction: PrepareIterationLoggingModel);
    }

    private static TutorialSkipResult PrepareIterationLoggingModel(ITutorialSkipContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TutorialStepSkipAction[] setupActions =
        [
            c => c.EnsureStart("Start 1"),
            c => c.EnsureModule(typeof(Execute), "Execute"),
            c => c.EnsureModule(typeof(Log), "Log"),
            c => c.EnsureModule(typeof(OpenWriteStreamToRunStatus), "Run Status Stream"),
            c => c.EnsureModule(typeof(SetableParameter<int>), "CurrentIteration"),
            c => c.EnsureModule(typeof(WriteToLogA), "Log Iteration"),
            c => c.EnsureLink("Start 1", "ToExecute", "Execute"),
            c => c.EnsureLink("Log", "LogStream", "Run Status Stream"),
            c => c.EnsureLink("Execute", "Current Iteration", "CurrentIteration"),
            c => c.EnsureLink("Execute", "To Execute", "Log Iteration"),
            c => c.EnsureLink("Log Iteration", "Log", "Log"),
            c => c.EnsureParameterValue("CurrentIteration", "0"),
            c => c.EnsureModelSystemVariable("CurrentIteration"),
            c => c.EnsureScriptedParameter(
                "Message",
                typeof(ScriptedParameter<string>),
                "\"Iteration: \" + CurrentIteration"),
            c => c.EnsureParameterValue("Iterations", "3")
        ];

        foreach (var setupAction in setupActions)
        {
            var result = setupAction(context);
            if (!result.Succeeded)
                return result;
        }

        return TutorialSkipResult.Success;
    }
}
