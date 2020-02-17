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
using System.Text;

namespace XTMF2.Editing
{
    /// <summary>
    /// Represents a command to the XTMF execution engine.
    /// </summary>
    public sealed class Command
    {
        internal Func<(bool Success, CommandError Error)> Undo;
        internal Func<(bool Success, CommandError Error)> Redo;
        /// <summary>
        /// Create a new command with the given delegates
        /// </summary>
        /// <param name="Undo">The logic to undo the command.</param>
        /// <param name="Redo">The logic to redo the command.</param>
        public Command(
            Func<(bool Success, CommandError Error)> Undo,
            Func<(bool Success, CommandError Error)> Redo
            )
        {
            if (Undo == null || Redo == null)
            {
                throw new ArgumentException($"Both {nameof(Undo)} and {nameof(Redo)} must be either assigned.");
            }
            this.Undo = Undo;
            this.Redo = Redo;
        }

        /// <summary>
        /// Tests if the command can be undone.
        /// </summary>
        public bool CanUndo => Undo != null;
    }
}
