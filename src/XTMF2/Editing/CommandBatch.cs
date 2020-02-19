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
    /// Represents a series of commands to be executed
    /// </summary>
    public sealed class CommandBatch
    {
        /// <summary>
        /// The commands in order
        /// </summary>
        private List<Command> _Commands = new List<Command>();
        
        /// <summary>
        /// Create a command batch from a single command.
        /// </summary>
        /// <param name="command">The command to set in a batch</param>
        public CommandBatch(Command command)
        {
            Add(command);
        }


        /// <summary>
        /// Add a command into a command batch.
        /// </summary>
        /// <param name="command">The command to add to the batch</param>
        public void Add(Command command)
        {
            _Commands.Add(command ?? throw new ArgumentNullException(nameof(command)));
        }

        /// <summary>
        /// Undo the batch of commands.
        /// </summary>
        /// <param name="error">An error message if the undo fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        public bool Undo(out CommandError? error)
        {
            foreach (var command in _Commands)
            {
                var result = command.Undo();
                if (!result.Success)
                {
                    error = result.Error;
                    return false;
                }
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Redo the batch of commands.
        /// </summary>
        /// <param name="error">An error message if the redo fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        public bool Redo(out CommandError? error)
        {
            foreach (var command in _Commands)
            {
                var result = command.Redo();
                if (!result.Success)
                {
                    error = result.Error;
                    return false;
                }
            }
            error = null;
            return true;
        }
    }
}
