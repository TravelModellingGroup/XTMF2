/*
    Copyright 2020 University of Toronto

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

namespace XTMF2.Editing
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class CommandError
    {
        /// <summary>
        /// Create a new error report to the issuing program.
        /// </summary>
        /// <param name="message">A message that describes the error.</param>
        /// <param name="unauthorizedUser">True if the user was not authorized to issue the command.</param>
        public CommandError(string message, bool unauthorizedUser = false)
        {
            Message = message;
            UnauthorizedUser = unauthorizedUser;
        }

        /// <summary>
        /// An message about why the command failed.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// True if the user was not authorized to issue the command.
        /// </summary>
        public bool UnauthorizedUser { get; }

        public override bool Equals(object obj)
        {
            return obj is CommandError error &&
                   Message == error.Message &&
                   UnauthorizedUser == error.UnauthorizedUser;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Message, UnauthorizedUser);
        }
    }
}
