/*
    Copyright 2018 University of Toronto

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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2;
using XTMF2.Editing;
using XTMF2.Controllers;
using System.Linq;
using XTMF2.ModelSystemConstruct;
using static XTMF2.Helper;
using TestXTMF.Modules;
using XTMF2.RuntimeModules;
using static TestXTMF.TestHelper;

namespace TestXTMF.Editing
{
    [TestClass]
    public class TestDisabledModule
    {
        [TestMethod]
        public void TestDisablingModule()
        {
            RunInModelSystemContext("ModelSystemSavedWithModelSystemStructureOnly", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss, ref error));
                Assert.AreEqual("MyMSS", mss.Name);
                Assert.IsFalse(mss.IsDisabled, "The model system structure started out as disabled!");
                Assert.IsTrue(mSession.SetModelSystemStructureDisabled(user, mss, true, ref error), error);
                Assert.IsTrue(mss.IsDisabled, "The model system structure was not disabled!");
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.IsFalse(mss.IsDisabled, "The model system structure was not re-enabled when undoing the disable instruction!");
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.IsTrue(mss.IsDisabled, "The model system structure was not disabled during the redo!");
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
            });
        }
    }
}
