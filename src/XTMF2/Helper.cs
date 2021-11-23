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
using System.Runtime.CompilerServices;
using System.Text;

namespace XTMF2
{
    public static class Helper
    {
        /// <summary>
        /// Conditionally execute code on a disposable object while this
        /// function maintains it's lifetime
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="callResult"></param>
        /// <param name="disposable"></param>
        /// <param name="disposableToExecuteOnSuccess"></param>
        /// <returns></returns>
        public static bool UsingIf<T>(this bool callResult, T disposable, Action disposableToExecuteOnSuccess)
            where T : IDisposable
        {
            if (callResult)
            {
                using (disposable)
                {
                    disposableToExecuteOnSuccess();
                }
            }
            return callResult;
        }

        public static void UsingIf<T>(this bool callResult, T disposable, Action disposableToExecuteOnSuccess, Action onFailure)
            where T : IDisposable
        {
            if (callResult)
            {
                using (disposable)
                {
                    disposableToExecuteOnSuccess();
                }
            }
            else
            {
                onFailure();
            }
        }

        /// <summary>
        /// Throw an exception is the string is null empty or whitespace
        /// </summary>
        /// <param name="argument">The string to test.</param>
        /// <param name="paramName">The name of the parameter. Automatically set to the name of the argument.</param>
        /// <exception cref="ArgumentNullException">Thrown if the argument is null, empty, or only contains whitespace.</exception>
        public static void ThrowIfNullOrWhitespace(string? argument, [CallerArgumentExpression("argument")] string? paramName = null)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                throw new ArgumentNullException(paramName);
            }
        }
    }
}
