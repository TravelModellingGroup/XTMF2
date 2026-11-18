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

namespace XTMF2.GUI.Tests.Modules;

[Module(Name = "Simple GUI Test Module",
    DocumentationLink = "http://example.com",
    Description = "A minimal module used in GUI unit tests.")]
public sealed class SimpleGuiTestModule : BaseFunction<string>
{
    public override string Invoke() => "GUI Test";
}

[Module(Name = "Linked GUI Test Module",
    DocumentationLink = "http://example.com",
    Description = "A minimal module with an outgoing hook used in GUI unit tests.")]
public sealed class LinkedGuiTestModule : BaseFunction<string>
{
    [SubModule(Name = "Child", Description = "A linked child module", Required = false, Index = 0)]
    public SimpleGuiTestModule? Child { get; set; }

    public override string Invoke() => Child?.Invoke() ?? string.Empty;
}

[Module(Name = "Optional Then Parameter GUI Test Module",
    DocumentationLink = "http://example.com",
    Description = "A module whose hidden optional hook precedes an inlined parameter hook.")]
public sealed class OptionalThenParameterGuiTestModule : BaseFunction<string>
{
    [SubModule(Name = "Optional Child", Description = "An unselected optional hook", Required = false, Index = 0)]
    public SimpleGuiTestModule? OptionalChild { get; set; }

    [Parameter(Name = "Editable Value", Description = "An editable parameter hook", Required = false, Index = 1, DefaultValue = "")]
    public IFunction<string>? EditableValue { get; set; }

    public override string Invoke() => EditableValue?.Invoke() ?? string.Empty;
}

[Module(Name = "Embedded Parameter GUI Test Module",
    DocumentationLink = "http://example.com",
    Description = "A module with a required parameter hook for embedded-row GUI tests.")]
public sealed class EmbeddedParameterGuiTestModule : BaseFunction<string>
{
    [Parameter(Name = "Editable Value", Description = "An editable parameter hook", Required = true, Index = 0)]
    public IFunction<string>? EditableValue { get; set; }

    public override string Invoke() => EditableValue?.Invoke() ?? string.Empty;
}

[Module(Name = "Embedded Node Host GUI Test Module",
    DocumentationLink = "http://example.com",
    Description = "A module with a required child hook for embedded-row GUI tests.")]
public sealed class EmbeddedNodeHostGuiTestModule : BaseFunction<string>
{
    [SubModule(Name = "Embedded Child", Description = "The embedded child", Required = true, Index = 0)]
    public EmbeddedParameterGuiTestModule? Child { get; set; }

    [Parameter(Name = "Direct Value", Description = "A direct parameter hook", Required = true, Index = 1)]
    public IFunction<string>? DirectValue { get; set; }

    public override string Invoke() => (Child?.Invoke() ?? string.Empty) + (DirectValue?.Invoke() ?? string.Empty);
}
