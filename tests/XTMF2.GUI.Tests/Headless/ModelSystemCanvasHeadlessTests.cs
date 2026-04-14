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
/// Headless smoke tests for <see cref="ModelSystemCanvas"/> — verifies the
/// control can be instantiated and measured without a full runtime context.
/// </summary>
[TestClass]
public class ModelSystemCanvasHeadlessTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void ModelSystemCanvas_CanBeInstantiated_WithNoViewModel()
    {
        Session.Dispatch(() =>
        {
            var canvas = new ModelSystemCanvas();
            Assert.IsNotNull(canvas);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ModelSystemCanvas_Measure_DoesNotThrow()
    {
        Session.Dispatch(() =>
        {
            var canvas = new ModelSystemCanvas();
            canvas.Measure(new Avalonia.Size(800, 600));
            // No exception means success; Bounds may be zero without layout root.
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ModelSystemCanvas_DataContext_DefaultsToNull()
    {
        Session.Dispatch(() =>
        {
            var canvas = new ModelSystemCanvas();
            // Without setting DataContext, the canvas VM is null.
            Assert.IsNull(canvas.DataContext);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}
