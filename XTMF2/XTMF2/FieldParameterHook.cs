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
using System.Reflection;

namespace XTMF2
{
    /// <summary>
    /// Represents a parameter hook that is bound to a field
    /// </summary>
    internal sealed class FieldParameterHook : ParameterHook
    {
        public override string Name => Info.Name;

        public override Type Type => Info.FieldType;

        private readonly FieldInfo Info;

        /// <summary>
        /// Create a new parameter hook bound to a field
        /// </summary>
        /// <param name="info"></param>
        internal FieldParameterHook(FieldInfo info, ParameterAttribute attribute) : base(attribute)
        {
            Info = info;
        }

        public override bool AssignValueToModule(object module, ref string error)
        {
            var (success, value) = ArbitraryParameterParser.ArbitraryParameterParse(Info.FieldType, Parameter.Value, ref error);
            if (success)
            {
                Info.SetValue(module, value);
                return true;
            }
            return false;
        }

        protected override ParameterHook Clone()
        {
            return new FieldParameterHook(Info, Attribute);
        }
    }
}
