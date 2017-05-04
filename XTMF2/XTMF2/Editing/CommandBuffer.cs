﻿/*
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
        private EditingStack Undo = new EditingStack(MaxCapacity);
        private EditingStack Redo = new EditingStack(MaxCapacity);
        private object ExecutionLock = new object();

        public bool ExecuteCommands(CommandBatch batch, ref string error)
        {
            lock (ExecutionLock)
            {
                var ret = batch.Do(ref error);
                if (ret)
                {
                    Undo.Add(batch);
                }
                return ret;
            }
        }

        public bool UndoCommands(ref string error)
        {
            lock (ExecutionLock)
            {
                if (Undo.TryPop(out var batch))
                {
                    var ret = batch.Undo(ref error);
                    if (ret)
                    {
                        Redo.Add(batch);
                    }
                    return ret;
                }
                return false;
            }
        }

        public bool RedoCommands(ref string error)
        {
            lock (ExecutionLock)
            {
                if (Undo.TryPop(out var batch))
                {
                    var ret = batch.Redo(ref error);
                    if (ret)
                    {
                        Undo.Add(batch);
                    }
                    return ret;
                }
                return false;
            }
        }

    }
}
