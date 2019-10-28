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
using System.Linq;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Basic Event", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
    Description = "Provides the ability for modules to invoke a set of other modules that are waiting for something to occur.")]
    public sealed class BasicEvent : BaseEvent
    {
        private readonly List<Action> _toInvoke = new List<Action>();

        public override void Invoke()
        {
            // make a copy in case the invocation causes an additional registration
            List<Action> copy;
            lock(_toInvoke)
            {
                copy = _toInvoke.ToList();
            }
            foreach(var registered in copy)
            {
                registered.Invoke();
            }
        }

        public override void Register(Action module)
        {
            lock(_toInvoke)
            {
                _toInvoke.Add(module);
            }
        }
    }

    [Module(Name = "Basic Event", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
    Description = "Provides the ability for modules to invoke a set of other modules that are waiting for something to occur.")]
    public sealed class BasicEvent<Context> : BaseEvent<Context>
    {
        private readonly List<Action<Context>> _toInvoke = new List<Action<Context>>();

        public override void Invoke(Context context)
        {
            // make a copy in case the invocation causes an additional registration
            List<Action<Context>> copy;
            lock (_toInvoke)
            {
                copy = _toInvoke.ToList();
            }
            foreach (var registered in copy)
            {
                registered.Invoke(context);
            }
        }

        public override void Register(Action<Context> module)
        {
            lock (_toInvoke)
            {
                _toInvoke.Add(module);
            }
        }
    }
}
