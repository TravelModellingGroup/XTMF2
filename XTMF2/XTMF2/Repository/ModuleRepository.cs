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
using System.Collections.Concurrent;

namespace XTMF2.Repository
{
    public sealed class ModuleRepository
    {
        private ConcurrentDictionary<Type, (TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks)> Data = new ConcurrentDictionary<Type, (TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks)>();
        private static TypeInfo IModuleTypeInfo = typeof(IModule).GetTypeInfo();

        public void Add(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (!IModuleTypeInfo.IsAssignableFrom(type))
            {
                throw new ArgumentException(nameof(type), "The type is not of a module!");
            }
            Data[type] = GetTypeData(type);
        }

        private (TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks) GetTypeData(Type type)
        {
            throw new NotImplementedException();
        }

        public (TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks) this[Type type]
        {
            get
            {
                Data.TryGetValue(type, out var ret);
                return ret;
            }
        }
    }
}
