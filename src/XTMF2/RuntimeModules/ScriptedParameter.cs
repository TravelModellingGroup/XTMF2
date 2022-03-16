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

using System.Diagnostics.CodeAnalysis;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Scripted Parameter", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
    Description = "Provides the ability to have a value that is calculated in an expression.")]
    public sealed class ScriptedParameter<T> : BaseFunction<T>
    {
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public ParameterExpression Expression;
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

        public override T Invoke()
        {
            string? error = null;
            if (!Expression.IsCompatible(typeof(T), ref error))
            {
                Throw(error);
            }
            var ret = Expression.GetValue(typeof(T), ref error);
            if (ret is null)
            {
                ThrowGotNull();
            }
            return (T)ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="error"></param>
        /// <exception cref="XTMFRuntimeException">The requested error message.</exception>
        [DoesNotReturn]
        public void Throw(string error)
        {
            throw new XTMFRuntimeException(this, error);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="XTMFRuntimeException"></exception>
        [DoesNotReturn]
        public void ThrowGotNull()
        {
            throw new XTMFRuntimeException(this, $"Unable to get a {typeof(T).FullName} value from expression '{Expression.Representation}'!");
        }
    }
}
