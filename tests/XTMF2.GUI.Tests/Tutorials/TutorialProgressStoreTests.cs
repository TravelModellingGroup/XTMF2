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
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Tutorials;

namespace XTMF2.GUI.Tests.Tutorials;

[TestClass]
public class TutorialProgressStoreTests
{
    [TestMethod]
    public void MarkCompletedPersistsOnceAndUsesCaseInsensitiveIds()
    {
        var savedIds = Array.Empty<string>();
        var saveCount = 0;
        var store = new TutorialProgressStore(
            Array.Empty<string>(),
            ids =>
            {
                savedIds = ids.ToArray();
                saveCount++;
            });

        store.MarkCompleted("library.lesson-one");
        store.MarkCompleted("LIBRARY.LESSON-ONE");

        Assert.IsTrue(store.IsCompleted("Library.Lesson-One"));
        Assert.AreEqual(1, saveCount);
        CollectionAssert.AreEqual(new[] { "library.lesson-one" }, savedIds);
    }

    [TestMethod]
    public void MarkCompletedRollsBackWhenPersistenceFails()
    {
        var store = new TutorialProgressStore(
            Array.Empty<string>(),
            _ => throw new IOException("Unable to persist tutorial progress."));

        Assert.ThrowsExactly<IOException>(() => store.MarkCompleted("library.lesson-one"));
        Assert.IsFalse(store.IsCompleted("library.lesson-one"));
    }
}
