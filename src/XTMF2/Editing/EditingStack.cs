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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace XTMF2.Editing
{
    /// <summary>
    /// Provides support for a rolling stack
    /// </summary>
    public sealed class EditingStack : ICollection<CommandBatch>
    {
        public EditingStack(int capacity)
        {
            Capacity = capacity;
            _Data = new CommandBatch[capacity];
            IsReadOnly = false;
        }
        
        public int Capacity { get; private set; }

        public int Count { get; private set; }

        public bool IsReadOnly { get; private set; }

        /// <summary>
        /// The backing data for the stack
        /// </summary>
        private CommandBatch[] _Data;

        private int _Head = -1;

        private object _DataLock = new object();

        /// <summary>
        /// Add a new command onto the stack
        /// </summary>
        /// <param name="item"></param>
        /// <exception cref="ArgumentNullException">The item may not be null.</exception>
        public void Add(CommandBatch item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            lock (_DataLock)
            {
                // since this is a circle, there is no issue
                _Head = (_Head + 1) % Capacity;
                Count++;
                _Data[_Head] = item;
                if(Count > Capacity)
                {
                    Count = Capacity;
                }
            }
        }

        /// <summary>
        /// Get the top element off of the stack
        /// </summary>
        /// <returns>The top element, null if there is nothing.</returns>
        public CommandBatch Pop()
        {
            if (TryPop(out CommandBatch result))
            {
                return result;
            }
            return null;
        }

        /// <summary>
        /// Attempt to pop the top element off of the stack
        /// </summary>
        /// <param name="command">The command that was popped off the stack, null if it failed.</param>
        /// <returns>If the pop was successful</returns>
        public bool TryPop(out CommandBatch command)
        {
            lock (_DataLock)
            {
                if(Count > 0)
                {
                    Count--;
                    command = _Data[_Head];
                    _Head = (_Head - 1) % Capacity;
                    return true;
                }
                command = null;
                return false;
            }
        }

        /// <summary>
        /// Clear the data from the stack
        /// </summary>
        public void Clear()
        {
            lock (_DataLock)
            {
                Array.Clear(_Data, 0, _Data.Length);
                Count = 0;
            }
        }

        /// <summary>
        /// Tests to see if the item is contained in the stack
        /// </summary>
        /// <param name="item">The item to check for.</param>
        /// <returns>True if the item is contained, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">The item may not be null.</exception>
        public bool Contains(CommandBatch item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            lock (_DataLock)
            {
                for(int i = 0; i < Count; i++)
                {
                    var headoffset = (_Head - i);
                    int index = headoffset < 0 ? Capacity + headoffset : headoffset;
                    if (_Data[index] == item)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Copy the command batches to an array
        /// </summary>
        /// <param name="array">The array to store them into.</param>
        /// <param name="arrayIndex">The starting position to copy them to.</param>
        /// <exception cref="ArgumentNullException">The array may not be null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The array must be able to store all elements otherwise this error will be thrown.</exception>
        public void CopyTo(CommandBatch[] array, int arrayIndex)
        {
            if(array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            lock (_DataLock)
            {
                if(array.Length - arrayIndex < Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex));
                }
                for(int i = 0; i < Count; i++)
                {
                    var headoffset = (_Head - i);
                    int index = headoffset < 0 ? Capacity + headoffset : headoffset;
                    array[arrayIndex++] = _Data[index];
                }
            }
        }

        public IEnumerator<CommandBatch> GetEnumerator()
        {
            lock (_DataLock)
            {
                for(int i = 0; i < Count; i++)
                {
                    var headoffset = (_Head - i);
                    int index = headoffset < 0 ? Capacity + headoffset : headoffset;
                    yield return _Data[index];
                }
            }
        }

        public bool Remove(CommandBatch item)
        {
            throw new NotSupportedException("Removing an item is not supported for a stack.");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
