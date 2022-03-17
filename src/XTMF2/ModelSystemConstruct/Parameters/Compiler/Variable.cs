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

internal abstract class Variable : Expression
{
    public Variable(ReadOnlyMemory<char> text, int offset) : base(text, offset)
    {

    }

    internal static Variable CreateVariableForNode(Node node, ReadOnlyMemory<char> text, int offset)
    {
        var parameterValue = node.ParameterValue;
        if(parameterValue is null)
        {
            throw new CompilerException($"Unable to create a variable for node {node.Name} because it has no parameter value!", offset);
        }
        return parameterValue.Type.FullName switch
        {
            "System.Boolean" => new BooleanVariable(text, offset, node),
            "System.Int32" => new IntegerVariable(text, offset, node),
            "System.Single" => new FloatVariable(text, offset, node),
            "System.String" => new StringVariable(text, offset, node),
            _ => throw new CompilerException($"Invalid type for a variable {parameterValue.Type.FullName} found when trying to" +
            $" use {node.Name}!", offset)
        };   
    }
}
