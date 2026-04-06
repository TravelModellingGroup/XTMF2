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
using System.ComponentModel;
using System.Text;

namespace XTMF2.Editing
{
    public sealed class CommandBuffer : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private const int MaxCapacity = 20;
        private readonly EditingStack _undo = new EditingStack(MaxCapacity);
        private readonly EditingStack _redo = new EditingStack(MaxCapacity);
        private readonly object _executionLock = new object();

        /// <summary>
        /// When non-null, all incoming <see cref="AddUndo"/> calls accumulate here
        /// instead of being pushed to the undo stack individually.
        /// Committed as one atomic entry by <see cref="CommitAggregateBatch"/>.
        /// </summary>
        private CommandBatch? _activeBatch = null;

        public CommandBuffer()
        {
            _undo.PropertyChanged += (_, _) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUndo)));
            _redo.PropertyChanged += (_, _) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRedo)));
        }

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
            lock (_executionLock)
            {
                if (_activeBatch is not null)
                    _activeBatch.Add(command);
                else
                {
                    _undo.Add(new CommandBatch(command));
                    _redo.Clear();
                }
            }
        }

        /// <summary>Pushes a pre-built <see cref="CommandBatch"/> as a single undoable entry.
        /// When an aggregate batch is active the pre-built batch is merged into it.</summary>
        internal void AddUndo(CommandBatch batch)
        {
            lock (_executionLock)
            {
                if (_activeBatch is not null)
                    batch.MergeInto(_activeBatch);
                else
                {
                    _undo.Add(batch);
                    _redo.Clear();
                }
            }
        }

        /// <summary>
        /// Begins collecting all subsequent <see cref="AddUndo"/> calls into a single
        /// <see cref="CommandBatch"/>. Must be paired with <see cref="CommitAggregateBatch"/>.
        /// </summary>
        internal void BeginAggregateBatch()
        {
            lock (_executionLock)
                _activeBatch = new CommandBatch();
        }

        /// <summary>
        /// Commits the accumulated batch as one undoable entry and clears batch mode.
        /// If no commands were accumulated the batch is discarded rather than pushed.
        /// </summary>
        internal void CommitAggregateBatch()
        {
            lock (_executionLock)
            {
                if (_activeBatch is { } batch)
                {
                    _activeBatch = null;
                    // Only push if the batch actually contains commands.
                    if (batch.HasCommands)
                    {
                        _undo.Add(batch);
                        _redo.Clear();
                    }
                }
            }
        }

        /// <summary>True when there is at least one undoable command.</summary>
        public bool CanUndo => _undo.Count > 0;

        /// <summary>True when there is at least one redoable command.</summary>
        public bool CanRedo => _redo.Count > 0;
    }
}
