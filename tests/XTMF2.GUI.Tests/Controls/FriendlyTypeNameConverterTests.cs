/*
    Copyright 2026 University of Toronto

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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Controls;

namespace XTMF2.GUI.Tests.Controls;

[TestClass]
public class FriendlyTypeNameConverterTests
{
    [TestMethod]
    public void GetFriendlyName_NonGenericType_ReturnsTypeName()
    {
        var result = FriendlyTypeNameConverter.GetFriendlyName(typeof(string));
        Assert.AreEqual("String", result);
    }

    [TestMethod]
    public void GetFriendlyName_NonGenericIntType_ReturnsTypeName()
    {
        var result = FriendlyTypeNameConverter.GetFriendlyName(typeof(int));
        Assert.AreEqual("Int32", result);
    }

    [TestMethod]
    public void GetFriendlyName_GenericList_FormatsWithAngularBrackets()
    {
        var result = FriendlyTypeNameConverter.GetFriendlyName(typeof(List<int>));
        Assert.AreEqual("List<Int32>", result);
    }

    [TestMethod]
    public void GetFriendlyName_NestedGeneric_FormatsRecursively()
    {
        var result = FriendlyTypeNameConverter.GetFriendlyName(typeof(Dictionary<string, List<int>>));
        Assert.AreEqual("Dictionary<String, List<Int32>>", result);
    }

    [TestMethod]
    public void GetFriendlyName_NullType_ReturnsEmpty()
    {
        var result = FriendlyTypeNameConverter.GetFriendlyName(null!);
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Convert_NonTypeValue_ReturnsEmpty()
    {
        var converter = FriendlyTypeNameConverter.Instance;
        var result = converter.Convert("not a type", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Convert_TypeValue_ReturnsFormattedName()
    {
        var converter = FriendlyTypeNameConverter.Instance;
        var result = converter.Convert(typeof(List<string>), typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual("List<String>", result);
    }

    [TestMethod]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var converter = FriendlyTypeNameConverter.Instance;
        Assert.ThrowsExactly<NotSupportedException>(() =>
            converter.ConvertBack("String", typeof(Type), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
