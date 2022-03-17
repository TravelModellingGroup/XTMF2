/*
    Copyright 2022 University of Toronto

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

namespace XTMF2.ModelSystemConstruct.Parameters.Compiler;

/// <summary>
/// This class represents a boolean value that can change during
/// a model system run.
/// </summary>
internal sealed class StringVariable : Variable
{
    /// <summary>
    /// The node that backs this variable
    /// </summary>
    private readonly Node _backingNode;

    public StringVariable(ReadOnlyMemory<char> text, int offset, Node backingNode) : base(text, offset)
    {
        _backingNode = backingNode;
    }

    public override Type Type => typeof(string);

    internal override Result GetResult(IModule caller)
    {
        string? error = null;
        var expression = _backingNode.ParameterValue;
        if (expression is null || expression?.IsCompatible(typeof(string), ref error) != true)
        {
            return new ErrorResult(error ?? $"{_backingNode.Name} does not have an expression!", typeof(string));
        }
        var ret = expression.GetValue(caller, typeof(string), ref error);
        if (ret is string b)
        {
            return new StringResult(b);
        }
        return new ErrorResult(error!, typeof(string));
    }
}
