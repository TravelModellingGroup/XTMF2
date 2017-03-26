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
using System.Reflection;
using System.Text;

namespace XTMF2
{
    internal sealed class PropertyParameterHook : ParameterHook
    {
        private PropertyInfo Info;

        public PropertyParameterHook(PropertyInfo property, ParameterAttribute parameterAt) : base(parameterAt)
        {
            Info = property;
        }

        public override string Name => Info.Name;

        public override Type Type => Info.PropertyType;

        public override bool AssignValueToModule(object module, ref string error)
        {
            var (success, value) = ArbitraryParameterParser.ArbitraryParameterParse(Info.PropertyType, Parameter.Value, ref error);
            if (success)
            {
                Info.SetValue(module, value);
                return true;
            }
            return false;
        }

        protected override ParameterHook Clone()
        {
            return new PropertyParameterHook(Info, Attribute);
        }
    }
}
