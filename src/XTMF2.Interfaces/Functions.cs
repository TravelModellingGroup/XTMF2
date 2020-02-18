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

namespace XTMF2
{

    public abstract class BaseModule : IModule
    {
        public string? Name { get; set; }

        public virtual bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }

    public interface IFunction<Result> : IModule
    {   
        Result Invoke();
    }

    public abstract class BaseFunction<Result> : IFunction<Result>
    {
        public string? Name { get; set; }

        public abstract Result Invoke();

        public virtual bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }

    public interface IFunction<Context,Result> : IModule
    {   
        Result Invoke(Context context);
    }

    public abstract class BaseFunction<Context, Result> : IFunction<Context, Result>
    {
        public string? Name { get; set; }

        public abstract Result Invoke(Context context);

        public virtual bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }

    public interface IAction : IModule
    {   
        void Invoke();
    }

    public abstract class BaseAction : IAction
    {
        public string? Name { get; set; }

        public abstract void Invoke();

        public virtual bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }

    public interface IAction<Context> : IModule
    {
        void Invoke(Context context);
    }

    public abstract class BaseAction<Context> : IAction<Context>
    {
        public string? Name { get; set; }

        public abstract void Invoke(Context context);

        public virtual bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }

    public interface IEvent : IAction
    {
        void Register(Action module);
    }

    public abstract class BaseEvent : IEvent
    {
        public string? Name { get; set; }

        public abstract void Invoke();

        public abstract void Register(Action module);

        public virtual bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }

    public interface IEvent<Context> : IAction<Context>
    {
        void Register(Action<Context> module);
    }

    public abstract class BaseEvent<Context> : IEvent<Context>
    {
        public string? Name { get; set; }

        public abstract void Invoke(Context context);

        public abstract void Register(Action<Context> module);

        public virtual bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }
}
