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
using XTMF2.ModelSystemConstruct;

namespace XTMF2.ModelSystemConstruct.Parameters.Compiler;

/// <summary>
/// A <see cref="Variable"/> backed by a <see cref="FunctionInstance"/> whose
/// template entry-node type is <c>IFunction&lt;<typeparamref name="T"/>&gt;</c>.
/// <para>
/// At runtime the value is retrieved by calling <see cref="IFunction{T}.Invoke()"/> on the
/// per-instance cloned module returned by
/// <see cref="FunctionInstance.GetRuntimeModule"/>.
/// </para>
/// </summary>
internal sealed class FunctionInstanceVariable<T> : Variable
{
    private readonly FunctionInstance _fi;

    public FunctionInstanceVariable(ReadOnlyMemory<char> text, int offset, FunctionInstance fi)
        : base(text, offset)
    {
        _fi = fi;
    }

    public override Type Type => typeof(T);

    internal override Result GetResult(IModule caller)
    {
        var entryNode = _fi.Template.EntryNode;
        if (entryNode is null)
            return new ErrorResult(
                $"FunctionInstance '{_fi.Name}' has no entry node set.",
                typeof(T));

        var runtimeModule = _fi.GetRuntimeModule(entryNode);
        if (runtimeModule is IFunction<T> func)
        {
            var value = func.Invoke();
            return value switch
            {
                bool   b => (Result)new BooleanResult(b),
                int    i => (Result)new IntegerResult(i),
                float  f => (Result)new FloatResult(f),
                string s => (Result)new StringResult(s),
                _        => new ErrorResult(
                    $"FunctionInstance '{_fi.Name}' returned an unsupported type '{typeof(T).FullName}'.",
                    typeof(T))
            };
        }

        return new ErrorResult(
            $"FunctionInstance '{_fi.Name}' runtime module is not IFunction<{typeof(T).Name}>. " +
            $"Ensure the template's entry node implements IFunction<{typeof(T).Name}>.",
            typeof(T));
    }
}
