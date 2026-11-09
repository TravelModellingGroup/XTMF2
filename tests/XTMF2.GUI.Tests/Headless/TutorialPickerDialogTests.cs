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
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Tutorials;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class TutorialPickerDialogTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void SelectingTutorialShowsDescriptionAndPreservesCompletedState()
    {
        var catalog = TutorialCatalog.FromModuleAssemblies(
            [typeof(TutorialPickerDialogTests).Assembly]);
        var completedTutorialId = catalog.Entries[0].Tutorial.Id;

        Session.Dispatch(() =>
        {
            var dialog = new TutorialPickerDialog(
                catalog.Entries,
                new HashSet<string> { completedTutorialId });
            var list = dialog.FindControl<ListBox>("TutorialList");
            var startButton = dialog.FindControl<Button>("StartButton");
            var selectedTitle = dialog.FindControl<TextBlock>("SelectedTutorialTitle");
            var selectedDescription = dialog.FindControl<TextBlock>("SelectedTutorialDescription");

            Assert.IsNotNull(list);
            Assert.IsNotNull(startButton);
            Assert.IsNotNull(selectedTitle);
            Assert.IsNotNull(selectedDescription);
            Assert.IsTrue(dialog.Classes.Contains("neon-dialog"));
            Assert.IsTrue(startButton!.Classes.Contains("dlg-primary"));
            Assert.IsFalse(startButton!.IsEnabled);

            list!.SelectedIndex = 0;

            var selectedItem = (TutorialPickerItem)list.SelectedItem!;
            Assert.IsTrue(selectedItem.IsCompleted);
            Assert.IsTrue(startButton.IsEnabled);
            Assert.AreEqual(selectedItem.Tutorial.Title, selectedTitle!.Text);
            Assert.AreEqual(selectedItem.Tutorial.Description, selectedDescription!.Text);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}
