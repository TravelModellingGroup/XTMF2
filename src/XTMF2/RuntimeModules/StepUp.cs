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
    [Module(Name = "Step Return Up", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Converts the result of a function to the expected type from the calling module.")]
    public sealed class StepReturnUp<Original, ConvertTo> : BaseFunction<ConvertTo> 
        where Original : ConvertTo
    {
        [SubModule(Required = true, Name = "ToInvoke", Description = "Invoke with converted context", Index = 0)]
        public IFunction<Original> ToInvoke;

        public override ConvertTo Invoke()
        {
            return (ConvertTo)ToInvoke.Invoke();
        }
    }

    [Module(Name = "Step Return Up", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Converts the result of a function to the expected type from the calling module.")]
    public sealed class StepReturnUp<Original, ConvertTo, Context> : BaseFunction<Context, ConvertTo> 
        where Original : ConvertTo
    {
        [SubModule(Required = true, Name = "ToInvoke", Description = "Invoke with converted context", Index = 0)]
        public IFunction<Context, Original> ToInvoke;

        public override ConvertTo Invoke(Context context)
        {
            return (ConvertTo)ToInvoke.Invoke(context);
        }
    }

    [Module(Name = "Step Return Up", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Converts the result of a function to the expected type from the calling module.")]
    public sealed class StepActionUp<Original, ConvertTo> : BaseAction<Original>
        where Original : ConvertTo
    {
        [SubModule(Required = true, Name = "ToInvoke", Description = "Invoke with converted context", Index = 0)]
        public IAction<ConvertTo> ToInvoke;

        public override void Invoke(Original context)
        {
            ToInvoke.Invoke((ConvertTo)context);
        }
    }
}
