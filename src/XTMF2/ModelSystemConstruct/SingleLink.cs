/*
    Copyright 2017 University of Toronto

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
using System.Text;
using System.Text.Json;
using XTMF2.Editing;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace XTMF2.ModelSystemConstruct;

public sealed class SingleLink : Link
{
    public Node Destination { get; private set; }
    public bool DestinationHidden { get; private set; }

    public SingleLink(Node origin, NodeHook hook, Node destination, bool disabled, bool orthogonal = false,
        bool destinationHidden = false, Guid id = default)
        : base(origin, hook, disabled, orthogonal, id)
    {
        Destination = destination;
        DestinationHidden = destinationHidden;
    }

    internal bool SetDestination(Node destination, out CommandError? error)
    {
        Destination = destination;
        Notify(nameof(Destination));
        error = null;
        return true;
    }

    internal override void Save(Dictionary<Node, int> moduleDictionary, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString(IdProperty, Id);
        writer.WriteNumber(OriginProperty, moduleDictionary[Origin!]);
        writer.WriteString(HookProperty, OriginHook.Name);
        writer.WriteNumber(DestinationProperty, moduleDictionary[Destination!]);
        if (IsDisabled)
        {
            writer.WriteBoolean(DisabledProperty, true);
        }
        if (IsOrthogonal)
        {
            writer.WriteBoolean(OrthogonalProperty, true);
        }
        if (DestinationHidden)
        {
            writer.WriteBoolean(HiddenDestinationsProperty, true);
        }
        writer.WriteEndObject();
    }

    public override int DestinationCount => 1;

    public override bool IsDestinationHidden(int destinationIndex)
        => destinationIndex == 0 && DestinationHidden;

    internal override bool SetDestinationHidden(int destinationIndex, bool hidden, [NotNullWhen(false)] out CommandError? error)
    {
        if (destinationIndex != 0)
        {
            error = new CommandError("Destination index out of range for SingleLink.");
            return false;
        }

        DestinationHidden = hidden;
        Notify(nameof(DestinationHidden));
        error = null;
        return true;
    }

    internal override bool SetAllDestinationsHidden(bool hidden, [NotNullWhen(false)] out CommandError? error)
        => SetDestinationHidden(0, hidden, out error);

    internal override bool Construct(ref string? error, ref Guid? elementId)
    {
        if (Origin.IsDisabled)
        {
            error = null;
            return true;
        }

        // FunctionParameter destinations are resolved transitively at runtime by
        // FunctionInstance.ConstructRuntimeLink(); no static wiring is needed here.
        if (Destination is FunctionParameter)
        {
            error = null;
            return true;
        }

        // If the origin is a FunctionInstance wired through a FunctionParameterHook,
        // record the parameter binding so FunctionInstance can route internal links
        // to the actual external module at runtime.
        if (Origin is FunctionInstance fiBinder && OriginHook is FunctionParameterHook fph)
        {
            var resolvedFiDest = Destination is GhostNode gnFi
                ? gnFi.ReferencedNode
                : Destination!;
            IModule? bindModule;
            if (resolvedFiDest is FunctionInstance destFiBinder)
            {
                bindModule = destFiBinder.Template.EntryNode is not null
                    ? destFiBinder.GetRuntimeModule(destFiBinder.Template.EntryNode)
                    : null;
            }
            else
            {
                bindModule = resolvedFiDest.Module;
            }
            fiBinder.BindParameter(fph.Parameter, bindModule);
            error = null;
            return true;
        }

        // Resolve ghost-node destinations to their real node.
        var resolved = Destination is GhostNode gn ? gn.ReferencedNode : Destination!;

        // Determine the effective destination node (for cardinality/disabled checks)
        // and the actual IModule to wire (per-instance clone for FunctionInstances).
        Node effectiveDest;
        IModule? destModule;
        bool destinationIsDisabled;
        if (resolved is FunctionInstance fi)
        {
            effectiveDest = fi.Template.EntryNode ?? resolved;
            destModule    = fi.Template.EntryNode is not null
                ? fi.GetRuntimeModule(fi.Template.EntryNode)
                : null;
            destinationIsDisabled = fi.IsDisabled || effectiveDest.IsDisabled;
        }
        else
        {
            effectiveDest = resolved;
            destModule    = resolved.Module;
            destinationIsDisabled = effectiveDest.IsDisabled;
        }

        // if not optional
        if (OriginHook!.Cardinality == HookCardinality.Single)
        {
            if (destinationIsDisabled)
            {
                error = "A link destined for a disabled module was not optional.";
                elementId = effectiveDest.Id;
                return false;
            }
            if (IsDisabled)
            {
                error = "A non optional link is disabled!";
                elementId = Origin.Id;
                return false;
            }
        }
        if (!IsDisabled && !destinationIsDisabled && destModule is not null)
        {
            OriginHook.Install(Origin!.Module!, destModule, 0);
        }
        return true;
    }

    internal override bool HasDestination(Node destNode)
    {
        return Destination == destNode;
    }

    override internal bool TryGetFirstDestination([NotNullWhen(true)] out object? dest)
    {
        dest = Destination;
        return dest is not null;
    }
}

