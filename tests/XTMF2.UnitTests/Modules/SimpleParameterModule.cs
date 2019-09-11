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
using XTMF2;

namespace TestXTMF.Modules
{
    [Module(Name = "Simple Parameter Module", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
        Description = "A test module to invoke a function that returns a string.")]
    public class SimpleParameterModule : BaseFunction<string>
    {
        [Parameter(Name = "Real Function", Description = "Will be called", Required = true, Index = 0, DefaultValue = "Hello World")]
        public IFunction<string> RealValue;

        public override string Invoke()
        {
            return RealValue.Invoke();
        }
    }
}
