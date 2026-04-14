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

using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Controls;

namespace XTMF2.GUI.Tests.Headless;

/// <summary>
/// Headless tests for <see cref="SearchBox"/> — a pure Avalonia control
/// that does not require an <see cref="XTMF2.XTMFRuntime"/> instance.
/// </summary>
[TestClass]
public class SearchBoxTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void SearchBox_CanBeInstantiated()
    {
        Session.Dispatch(() =>
        {
            var searchBox = new SearchBox();
            Assert.IsNotNull(searchBox);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void SearchBox_Text_DefaultsToEmpty()
    {
        Session.Dispatch(() =>
        {
            var searchBox = new SearchBox();
            // Text StyledProperty defaults to "" as registered.
            Assert.AreEqual(string.Empty, searchBox.Text);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}
