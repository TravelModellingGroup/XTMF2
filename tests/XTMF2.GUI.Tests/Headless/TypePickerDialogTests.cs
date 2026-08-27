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
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Controls;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class TypePickerDialogTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void FilterBoxEnter_SelectsFirstFilteredTypeAndConfirmsDialog()
    {
        Session.Dispatch(() =>
        {
            var types = new ReadOnlyObservableCollection<Type>(new ObservableCollection<Type>
            {
                typeof(TypePickerDialogTests),
                typeof(SearchBox)
            });

            var dialog = new TypePickerDialog(types)
            {
                FilterText = nameof(SearchBox)
            };

            var filterBox = dialog.FindControl<SearchBox>("FilterBox");
            Assert.IsNotNull(filterBox);

            filterBox.RaiseEvent(new RoutedEventArgs(SearchBox.EnterPressedEvent));

            Assert.IsFalse(dialog.WasCancelled);
            Assert.AreEqual(typeof(SearchBox), dialog.SelectedType);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}