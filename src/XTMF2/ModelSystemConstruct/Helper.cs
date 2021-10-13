/*
    Copyright 2021 University of Toronto

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
using XTMF2.Editing;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// Provides helper functions for use in the ModelSystemConstruct namespace
    /// </summary>
    internal static class Helper
    {
        /// <summary>
        /// Returns false after setting the error message.
        /// </summary>
        /// <param name="error">The error message to set.</param>
        /// <param name="message">The error message.</param>
        /// <returns>False</returns>
        internal static bool FailWith(out string error, string message)
        {
            error = message;
            return false;
        }

        /// <summary>
        /// Returns false after setting the error message.
        /// </summary>
        /// <param name="error">The command error to create with the given message.</param>
        /// <param name="message">The error message.</param>
        /// <returns>False</returns>
        internal static bool FailWith(out CommandError error, string message)
        {
            error = new CommandError(message);
            return false;
        }
    }
}
