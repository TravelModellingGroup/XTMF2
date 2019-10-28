/*
    Copyright 2014 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of XTMF.

    XTMF is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Cache", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
    Description = "Provides a way to keep the result of a function unless unloaded by an event.")]
    public sealed class Cache<T> : BaseFunction<T>, IDisposable
    {
        private readonly object _lock = new object();

        private T _cachedValue;
        private bool _initialized = false;


        [SubModule(Required = true, Name = "Source", Description = "Get the cached data", Index = 0)]
        public IFunction<T> Source;

        [SubModule(Required = true, Name = "Force Update", Description = "Invoke to force an update", Index = 1)]
        public IEvent ForceUpdate;

        public override T Invoke()
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    _cachedValue = Source.Invoke();
                    _initialized = true;
                    GC.ReRegisterForFinalize(this);
                }
                return _cachedValue;
            }
        }

        public override bool RuntimeValidation(ref string error)
        {
            ForceUpdate?.Register(() =>
            {
                lock (_lock)
                {
                    _initialized = false;
                    Dispose();
                }
            });
            return true;
        }

        ~Cache()
        {
            Dispose(false);
        }

        private void Dispose(bool managed)
        {
            if(managed)
            {
                GC.SuppressFinalize(this);
            }
            lock (_lock)
            {
                if (_initialized && _cachedValue is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _cachedValue = default;
                _initialized = false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
