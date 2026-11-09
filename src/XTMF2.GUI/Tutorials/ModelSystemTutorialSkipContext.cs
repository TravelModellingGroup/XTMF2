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
using System.Linq;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Tutorials;

internal sealed class ModelSystemTutorialSkipContext : ITutorialSkipContext
{
    private const float TutorialCanvasContentStartX = 420f;

    private readonly ModelSystemEditorViewModel _editor;

    public ModelSystemTutorialSkipContext(ModelSystemEditorViewModel editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }

    public TutorialSkipResult EnsureStart(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var boundary = _editor.CurrentBoundary;
        if (boundary.Starts.Any(start => string.Equals(start.Name, name, StringComparison.Ordinal)))
            return TutorialSkipResult.Success;
        if (boundary.Modules.Any(node => string.Equals(node.Name, name, StringComparison.Ordinal)))
            return TutorialSkipResult.Failure($"A module named '{name}' already exists in this boundary.");

        return _editor.Session.AddModelSystemStart(
                _editor.User,
                boundary,
                name,
                NextLocation(boundary.Starts.Count + boundary.Modules.Count + boundary.CommentBlocks.Count),
                out _,
                out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message ?? $"Could not add Start '{name}'.");
    }

    public TutorialSkipResult EnsureModule(Type moduleType, string name)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var boundary = _editor.CurrentBoundary;
        var existing = boundary.Modules.FirstOrDefault(node =>
            string.Equals(node.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing.Type == moduleType
                ? TutorialSkipResult.Success
                : TutorialSkipResult.Failure($"A different module named '{name}' already exists in this boundary.");
        }
        if (boundary.Starts.Any(start => string.Equals(start.Name, name, StringComparison.Ordinal)))
            return TutorialSkipResult.Failure($"A Start named '{name}' already exists in this boundary.");

        return _editor.Session.AddNodeGenerateParameters(
                _editor.User,
                boundary,
                name,
                moduleType,
                NextLocation(boundary.Starts.Count + boundary.Modules.Count + boundary.CommentBlocks.Count),
                out _,
                out _,
                out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message ?? $"Could not add module '{name}'.");
    }

    public TutorialSkipResult EnsureLink(string originName, string hookName, string destinationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originName);
        ArgumentException.ThrowIfNullOrWhiteSpace(hookName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationName);
        var boundary = _editor.CurrentBoundary;
        var originResult = FindNode(boundary, originName, out var origin);
        if (!originResult.Succeeded)
            return originResult;
        var destinationResult = FindNode(boundary, destinationName, out var destination);
        if (!destinationResult.Succeeded)
            return destinationResult;

        var hook = origin!.Hooks?.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, hookName, StringComparison.Ordinal));
        if (hook is null)
            return TutorialSkipResult.Failure($"Node '{originName}' does not have a hook named '{hookName}'.");

        var existing = boundary.Links.FirstOrDefault(link =>
            ReferenceEquals(link.Origin, origin) && ReferenceEquals(link.OriginHook, hook));
        if (existing is not null)
        {
            var alreadyConnected = existing switch
            {
                SingleLink single => ReferenceEquals(single.Destination, destination),
                MultiLink multi => multi.Destinations.Contains(destination!),
                _ => false
            };
            return alreadyConnected
                ? TutorialSkipResult.Success
                : TutorialSkipResult.Failure(
                    $"Hook '{hookName}' on '{originName}' is already connected to a different destination.");
        }

        return _editor.Session.AddLink(
                _editor.User,
                origin,
                hook,
                destination!,
                out _,
                out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message ?? "Could not create the tutorial connection.");
    }

    public TutorialSkipResult EnsureEstimationParameter(
        string parameterNodeName, double min, double max, double nullHypothesis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterNodeName);
        var result = FindNode(_editor.CurrentBoundary, parameterNodeName, out var parameter);
        if (!result.Succeeded)
            return result;

        var parameterType = parameter!.Type;
        if (parameterType is null
            || !parameterType.IsGenericType
            || parameterType.GetGenericTypeDefinition() != typeof(XTMF2.RuntimeModules.BasicParameter<>))
        {
            return TutorialSkipResult.Failure(
                $"'{parameterNodeName}' must be a BasicParameter<float> or BasicParameter<double>.");
        }
        var valueType = parameterType.GetGenericArguments()[0];
        if (valueType != typeof(float) && valueType != typeof(double))
        {
            return TutorialSkipResult.Failure(
                $"'{parameterNodeName}' must be a BasicParameter<float> or BasicParameter<double>.");
        }

        if (_editor.Session.ModelSystem.EstimationGroups
            .Any(group => group.Parameters.Any(entry => ReferenceEquals(entry.Node, parameter))))
        {
            return TutorialSkipResult.Success;
        }

        var group = _editor.Session.ModelSystem.EstimationGroups.FirstOrDefault();
        if (group is null
            && !_editor.Session.AddEstimationGroup(_editor.User, "Default", out group, out var groupError))
        {
            return TutorialSkipResult.Failure(groupError?.Message
                ?? "Could not create the default estimation group.");
        }

        return _editor.Session.AddEstimationParameter(
                _editor.User, group!, parameter!, min, max, nullHypothesis, out _, out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message
                ?? $"Could not add '{parameterNodeName}' to the estimation group.");
    }

    public TutorialSkipResult EnsureParameterValue(string parameterNodeName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterNodeName);
        ArgumentNullException.ThrowIfNull(value);
        var result = FindNode(_editor.CurrentBoundary, parameterNodeName, out var parameter);
        if (!result.Succeeded)
            return result;

        return _editor.Session.SetParameterValue(_editor.User, parameter!, value, out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message ?? $"Could not set parameter '{parameterNodeName}'.");
    }

    public TutorialSkipResult EnsureModelSystemVariable(string nodeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);
        var result = FindNode(_editor.CurrentBoundary, nodeName, out var node);
        if (!result.Succeeded)
            return result;
        if (_editor.Session.ModelSystem.Variables.Contains(node!))
            return TutorialSkipResult.Success;

        return _editor.Session.AddVariable(_editor.User, node!, out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message ?? $"Could not add '{nodeName}' to model-system variables.");
    }

    public TutorialSkipResult EnsureScriptedParameter(
        string parameterNodeName,
        Type scriptedParameterType,
        string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterNodeName);
        ArgumentNullException.ThrowIfNull(scriptedParameterType);
        ArgumentNullException.ThrowIfNull(expression);
        var result = FindNode(_editor.CurrentBoundary, parameterNodeName, out var parameter);
        if (!result.Succeeded)
            return result;

        if (parameter!.Type != scriptedParameterType
            && !_editor.Session.SetNodeType(_editor.User, parameter, scriptedParameterType, out var typeError))
        {
            return TutorialSkipResult.Failure(typeError?.Message
                ?? $"Could not change '{parameterNodeName}' to a scripted parameter.");
        }

        return _editor.Session.SetParameterExpression(_editor.User, parameter, expression, out var expressionError)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(expressionError?.Message
                ?? $"Could not set the expression for '{parameterNodeName}'.");
    }

    public TutorialSkipResult EnsureFunctionInstance(string templateName, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var boundary = _editor.CurrentBoundary;
        var template = boundary.FunctionTemplates.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, templateName, StringComparison.Ordinal));
        if (template is null)
            return TutorialSkipResult.Failure($"Could not find a function template named '{templateName}'.");

        var existing = boundary.FunctionInstances.FirstOrDefault(instance =>
            string.Equals(instance.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return ReferenceEquals(existing.Template, template)
                ? TutorialSkipResult.Success
                : TutorialSkipResult.Failure($"A different function instance named '{name}' already exists.");
        }

        var index = boundary.Starts.Count + boundary.Modules.Count
            + boundary.FunctionTemplates.Count + boundary.FunctionInstances.Count
            + boundary.CommentBlocks.Count;
        return _editor.Session.AddFunctionInstanceGenerateParameters(
                _editor.User,
                boundary,
                template,
                name,
                NextLocation(index, 160f, 70f),
                out _,
                out _,
                out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message ?? $"Could not add function instance '{name}'.");
    }

    public TutorialSkipResult EnsureComment(string header, string body)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(body);
        var boundary = _editor.CurrentBoundary;
        if (boundary.CommentBlocks.Any(comment =>
                string.Equals(comment.Header, header, StringComparison.Ordinal)
                && string.Equals(comment.Comment, body, StringComparison.Ordinal)))
        {
            return TutorialSkipResult.Success;
        }

        if (boundary.CommentBlocks.Any(comment =>
                string.Equals(comment.Header, header, StringComparison.Ordinal)))
        {
            return TutorialSkipResult.Failure($"A comment with the header '{header}' already exists.");
        }

        var location = NextLocation(
            boundary.Starts.Count + boundary.Modules.Count + boundary.CommentBlocks.Count,
            180f,
            90f);
        if (!_editor.Session.AddCommentBlock(
                _editor.User,
                boundary,
                body,
                location,
                out var block,
                out var addError))
        {
            return TutorialSkipResult.Failure(addError?.Message ?? "Could not add the tutorial comment.");
        }

        if (_editor.Session.SetCommentBlockHeader(_editor.User, block!, header, out var headerError))
            return TutorialSkipResult.Success;

        var headerFailure = headerError?.Message ?? "Could not set the tutorial comment header.";
        if (_editor.Session.RemoveCommentBlock(_editor.User, boundary, block!, out var removeError))
            return TutorialSkipResult.Failure(headerFailure);

        return TutorialSkipResult.Failure(
            $"{headerFailure} The incomplete comment could not be removed: {removeError?.Message ?? "unknown error"}");
    }

    public TutorialSkipResult SaveModelSystem()
        => _editor.Session.Save(out var error)
            ? TutorialSkipResult.Success
            : TutorialSkipResult.Failure(error?.Message ?? "Could not save the model system.");

    private static TutorialSkipResult FindNode(Boundary boundary, string name, out Node? node)
    {
        var matches = boundary.Modules
            .Cast<Node>()
            .Concat(boundary.Starts.Cast<Node>())
            .Concat(boundary.FunctionInstances.Cast<Node>())
            .Where(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 1)
        {
            node = matches[0];
            return TutorialSkipResult.Success;
        }

        node = null;
        return TutorialSkipResult.Failure(matches.Length == 0
            ? $"Could not find a node, Start, or function instance named '{name}'."
            : $"More than one node, Start, or function instance is named '{name}'.");
    }

    private static Rectangle NextLocation(int index, float width = 120f, float height = 50f)
        => new(TutorialCanvasContentStartX + index % 8 * 160f, 50f + index / 8 * 120f, width, height);
}
