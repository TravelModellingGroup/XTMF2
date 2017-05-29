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
    public class Cache<T> : BaseFunction<T>, IDisposable
    {
        object Lock = new object();

        private T CachedValue;
        private bool Initialized = false;


        [SubModule(Required = true, Name = "Source", Description = "Get the cached data")]
        public IFunction<T> Source;

        [SubModule(Required = true, Name = "Force Update", Description = "Invoke to force an update")]
        public IEvent ForceUpdate;

        public override T Invoke()
        {
            lock (Lock)
            {
                if (!Initialized)
                {
                    CachedValue = Source.Invoke();
                    Initialized = true;
                }
                return CachedValue;
            }
        }

        public override bool RuntimeValidation(ref string error)
        {
            ForceUpdate?.Register(() =>
            {
                lock (Lock)
                {
                    Initialized = false;
                    Dispose();
                }
            });
            return true;
        }

        public void Dispose()
        {
            lock (Lock)
            {
                if (Initialized && CachedValue is IDisposable disposable)
                {
                    disposable.Dispose();
                    CachedValue = default(T);
                }
            }
        }
    }
}
