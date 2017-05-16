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
using Newtonsoft.Json;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// A start is a special model system structure where
    /// it has no type and can be used to enter the
    /// model system
    /// </summary>
    public sealed class Start : ModelSystemStructure
    {
        public Start(string startName, Boundary boundary, string description, Point point) : base(startName)
        {
            ContainedWithin = boundary;
            Description = description;
            Location = point;
        }

        internal override void Save(ref int index, Dictionary<Type, int> typeDictionary, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Name");
            writer.WriteValue(Name);
            writer.WritePropertyName("Index");
            writer.WriteValue(index++);
            writer.WriteEndObject();
        }

        internal static bool Load(JsonTextReader reader, out Start start, ref string error)
        {
            throw new NotImplementedException();
        }
    }
}
