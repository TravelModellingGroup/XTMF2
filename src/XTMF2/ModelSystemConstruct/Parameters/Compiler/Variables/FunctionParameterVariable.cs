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
/// A <see cref="Variable"/> backed by a <see cref="FunctionParameter"/> whose type is
/// <c>IFunction&lt;<typeparamref name="T"/>&gt;</c>.
/// <para>
/// At runtime the value is retrieved by calling <see cref="IFunction{T}.Invoke()"/> on the
/// module that was bound to the parameter by the enclosing <see cref="FunctionInstance"/>.
/// </para>
/// </summary>
internal sealed class FunctionParameterVariable<T> : Variable
{
    private readonly FunctionParameter _fp;

    public FunctionParameterVariable(ReadOnlyMemory<char> text, int offset, FunctionParameter fp)
        : base(text, offset)
    {
        _fp = fp;
    }

    public override Type Type => typeof(T);

    internal override Result GetResult(IModule caller)
    {
        // The FunctionParameter's Module is set to the externally-bound IFunction<T> module
        // during FunctionInstance.ConstructRuntimeLinks.  For per-instance FunctionInstances,
        // the binding is stored in the active FI context rather than on the node directly.
        IFunction<T>? func = null;
        if (_fp.Module is IFunction<T> direct)
            func = direct;
        else if (FunctionInstance.Current?.GetBoundModule(_fp) is IFunction<T> fiBound)
            func = fiBound;
        if (func is not null)
        {
            var value = func.Invoke();
            return value switch
            {
                bool   b => new BooleanResult(b)     as Result,
                int    i => new IntegerResult(i)      as Result,
                float  f => new FloatResult(f)        as Result,
                string s => new StringResult(s)       as Result,
                _        => null!
            } ?? new ErrorResult(
                $"FunctionParameter '{_fp.Name}' returned an unsupported type '{typeof(T).FullName}'.",
                typeof(T));
        }

        // The module is not yet bound (design-time evaluation or un-wired slot).
        return new ErrorResult(
            $"FunctionParameter '{_fp.Name}' is not bound to a runtime module. " +
            $"Connect an IFunction<{typeof(T).Name}> module to this hook on every FunctionInstance.",
            typeof(T));
    }
}
