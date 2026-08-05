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
using XTMF2.RuntimeModules;

namespace XTMF2.ModelSystemConstruct.Parameters.Compiler;

internal abstract class Variable : Expression
{
    public Variable(ReadOnlyMemory<char> text, int offset) : base(text, offset)
    {

    }

    internal static Variable CreateVariableForNode(Node node, ReadOnlyMemory<char> text, int offset)
    {
            // A FunctionInstance whose template entry-node type is IFunction<T> for a supported
            // basic type can be used as a variable; its runtime value is obtained by invoking the
            // per-instance cloned entry-node module.
            if (node is ModelSystemConstruct.FunctionInstance fi)
            {
                var inner = ModelSystemConstruct.FunctionTemplate.ExtractFunctionInstanceVariableType(fi);
                if (inner is null)
                    throw new CompilerException(
                        $"FunctionInstance '{node.Name}' type '{fi.Type?.FullName}' is not IFunction<T> of a supported basic type.",
                        offset);
                return inner.FullName switch
                {
                    "System.Boolean" => new FunctionInstanceVariable<bool>(text, offset, fi),
                    "System.Int32"   => new FunctionInstanceVariable<int>(text, offset, fi),
                    "System.Single"  => new FunctionInstanceVariable<float>(text, offset, fi),
                    "System.String"  => new FunctionInstanceVariable<string>(text, offset, fi),
                    _ => throw new CompilerException(
                        $"Unsupported IFunction inner type '{inner.FullName}' for FunctionInstance '{node.Name}'.", offset)
                };
            }

            // FunctionParameter nodes expose IFunction<T> for a basic type; handle them specially
            // because they don't have a ParameterValue — their value arrives via the FunctionInstance
            // hook binding at runtime.
            if (node is ModelSystemConstruct.FunctionParameter fp)
            {
                var inner = ModelSystemConstruct.FunctionTemplate.ExtractIFunctionInnerType(fp.Type);
                if (inner is null)
                    throw new CompilerException(
                        $"FunctionParameter '{node.Name}' type '{fp.Type?.FullName}' is not IFunction<T> of a supported basic type.",
                        offset);
                return inner.FullName switch
                {
                    "System.Boolean" => new FunctionParameterVariable<bool>(text, offset, fp),
                    "System.Int32"   => new FunctionParameterVariable<int>(text, offset, fp),
                    "System.Single"  => new FunctionParameterVariable<float>(text, offset, fp),
                    "System.String"  => new FunctionParameterVariable<string>(text, offset, fp),
                    _ => throw new CompilerException(
                        $"Unsupported IFunction inner type '{inner.FullName}' for FunctionParameter '{node.Name}'.", offset)
                };
            }

            var parameterValue = node.ParameterValue;
            // Determine the dispatch type. Prefer ParameterValue.Type (accurate at runtime),
            // but fall back to the generic argument of the node's module type so that a
            // freshly-created BasicParameter<int> node (ParameterValue still null) can still
            // be used as a variable in expressions.
            Type? valueType = parameterValue?.Type;
            if (valueType is null && node.Type is { IsGenericType: true } nt)
            {
                var td = nt.GetGenericTypeDefinition();
                if (td == typeof(RuntimeModules.BasicParameter<>)
                 || td == typeof(RuntimeModules.ScriptedParameter<>))
                    valueType = nt.GetGenericArguments()[0];
            }
            if (valueType is null)
            {
                throw new CompilerException($"Unable to create a variable for node {node.Name} because it has no parameter value and its type cannot be inferred!", offset);
            }
            return valueType.FullName switch
            {
                "System.Boolean" => new BooleanVariable(text, offset, node),
                "System.Int32" => new IntegerVariable(text, offset, node),
                "System.Single" => new FloatVariable(text, offset, node),
                "System.String" => new StringVariable(text, offset, node),
                _ => throw new CompilerException($"Invalid type for a variable {valueType.FullName} found when trying to" +
                $" use {node.Name}!", offset)
            };
        }
}