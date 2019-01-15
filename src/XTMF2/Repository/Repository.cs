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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace XTMF2.Repository
{
    /// <summary>
    /// A generalized data storage
    /// </summary>
    /// <typeparam name="T">The type that is being stored</typeparam>
    public abstract class Repository<T>
    {
        /// <summary>
        /// The backing of Store
        /// </summary>
        protected ObservableCollection<T> _Store;

        /// <summary>
        /// This lock must be obtained before editing _Store
        /// </summary>
        protected object StoreLock = new object();

        /// <summary>
        /// Get a read-only reference to the stored data
        /// </summary>
        public ReadOnlyObservableCollection<T> Store
        {
            get
            {
                lock (StoreLock)
                {
                    return new ReadOnlyObservableCollection<T>(_Store);
                }
            }
        }

        /// <summary>
        /// Create a new repository
        /// </summary>
        public Repository()
        {
            lock (StoreLock)
            {
                _Store = new ObservableCollection<T>();
            }
        }

        /// <summary>
        /// Add the data to the repository
        /// </summary>
        /// <param name="toAdd">The data to add.</param>
        /// <param name="error">An error message if the operation fails</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        public bool Add(T toAdd, ref string error)
        {
            // Ensure the data is not null
            if (toAdd is object o && o == null)
            {
                throw new ArgumentNullException(nameof(toAdd));
            }

            if (!ValidateInput(toAdd, ref error))
            {
                return false;
            }
            lock (StoreLock)
            {
                _Store.Add(toAdd);
                return true;
            }
        }

        /// <summary>
        /// Validate the gtiven input data.
        /// </summary>
        /// <param name="data">The data to validate</param>
        /// <param name="error">An error message explaining why the data is invalid.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        protected virtual bool ValidateInput(T data, ref string error)
        {
            return true;
        }

        /// <summary>
        /// Remove the given item from the repository
        /// </summary>
        /// <param name="toRemove">The item to remove</param>
        /// <returns>True if successful</returns>
        public bool Remove(T toRemove)
        {
            lock (StoreLock)
            {
                return _Store.Remove(toRemove);
            }
        }

        /// <summary>
        /// Check to see if the data is contained in the repository
        /// </summary>
        /// <param name="toCheck">The item to check</param>
        /// <returns>True if the item is in the repository</returns>
        public bool Contains(T toCheck)
        {
            lock (StoreLock)
            {
                return _Store.Contains(toCheck);
            }
        }
    }
}
