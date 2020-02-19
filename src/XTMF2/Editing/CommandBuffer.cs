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
    public sealed class CommandBuffer
    {
        private const int MaxCapacity = 20;
        private readonly EditingStack _undo = new EditingStack(MaxCapacity);
        private readonly EditingStack _redo = new EditingStack(MaxCapacity);
        private readonly object _executionLock = new object();

        public bool UndoCommands(out CommandError? error)
        {
            lock (_executionLock)
            {
                if (_undo.TryPop(out var batch))
                {
                    if (batch!.Undo(out error))
                    {
                        _redo.Add(batch);
                        return true;
                    }
                }
                else
                {
                    error = new CommandError("No command to undo");
                }
                return false;
            }
        }

        public bool RedoCommands(out CommandError? error)
        {
            lock (_executionLock)
            {
                if (_redo.TryPop(out var batch))
                {
                    if (batch!.Redo(out error))
                    {
                        _undo.Add(batch);
                        return true;
                    }
                }
                else
                {
                    error = new CommandError("No command to redo");
                }
                return false;
            }
        }

        internal void AddUndo(Command command)
        {
            lock(_executionLock)
            {
                _undo.Add(new CommandBatch(command));
            }
        }
    }
}
