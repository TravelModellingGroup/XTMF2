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

namespace XTMF2.RuntimeModules
{
    public sealed class StepReturnUp<Original, ConvertTo> : IFunction<ConvertTo> 
        where Original : ConvertTo
    {
        public string Name { get; set; }

        public IFunction<Original> ToInvoke;

        public ConvertTo Invoke()
        {
            return (ConvertTo)ToInvoke.Invoke();
        }
    }

    public sealed class StepReturnUp<Original, ConvertTo, Context> : IFunction<Context, ConvertTo> 
        where Original : ConvertTo
    {
        public string Name { get; set; }

        public IFunction<Context, Original> ToInvoke;

        public ConvertTo Invoke(Context context)
        {
            return (ConvertTo)ToInvoke.Invoke(context);
        }
    }

    public sealed class StepActionUp<Original, ConvertTo> : IAction<Original>
        where Original : ConvertTo
    {
        public string Name { get; set; }

        public IAction<ConvertTo> ToInvoke;

        public void Invoke(Original context)
        {
            ToInvoke.Invoke((ConvertTo)context);
        }
    }
}
