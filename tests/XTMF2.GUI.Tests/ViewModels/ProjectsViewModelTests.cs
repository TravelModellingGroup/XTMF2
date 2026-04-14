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

using System.ComponentModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class ProjectsViewModelTests
{
    [TestMethod]
    public void FilteredProjects_WithNoSearchText_ContainsAllProjects()
    {
        TestGuiHelper.RunInProjectContext("PVM_NoFilter", (runtime, user, pSession) =>
        {
            var vm = new ProjectsViewModel(runtime);

            vm.SearchText = "";

            // The project created by RunInProjectContext ("TestProject") should appear.
            Assert.IsNotEmpty(vm.FilteredProjects);
        });
    }

    [TestMethod]
    public void FilteredProjects_WithMatchingSearchText_RetainsMatchingProjects()
    {
        TestGuiHelper.RunInProjectContext("PVM_MatchingFilter", (runtime, user, pSession) =>
        {
            var vm = new ProjectsViewModel(runtime);

            vm.SearchText = "Test";

            // "TestProject" matches "Test" (case-insensitive).
            Assert.IsNotEmpty(vm.FilteredProjects);
            foreach (var project in vm.FilteredProjects)
            {
                StringAssert.Contains(
                    project.Name?.ToLowerInvariant() ?? string.Empty, "test");
            }
        });
    }

    [TestMethod]
    public void FilteredProjects_WithNonMatchingSearchText_ReturnsEmpty()
    {
        TestGuiHelper.RunInProjectContext("PVM_NonMatchingFilter", (runtime, user, pSession) =>
        {
            var vm = new ProjectsViewModel(runtime);

            vm.SearchText = "ZZZNOMATCH999";

            Assert.IsEmpty(vm.FilteredProjects);
        });
    }

    [TestMethod]
    public void FilteredProjects_CaseInsensitiveSearch()
    {
        TestGuiHelper.RunInProjectContext("PVM_CaseInsensitive", (runtime, user, pSession) =>
        {
            var vm = new ProjectsViewModel(runtime);

            // Pick the first project the VM sees for this user and search for its
            // name in all-lowercase — case-insensitive matching must still find it.
            var firstProject = vm.Projects?.FirstOrDefault();
            if (firstProject?.Name is not string projectName || projectName.Length == 0)
            {
                Assert.Inconclusive("No projects available in the current user context.");
                return;
            }

            vm.SearchText = projectName.ToLowerInvariant();

            Assert.IsNotEmpty(vm.FilteredProjects,
                $"Expected case-insensitive search '{projectName.ToLowerInvariant()}' to match project '{projectName}'.");
        });
    }

    [TestMethod]
    public void SearchText_Changed_TriggersPropertyChanged()
    {
        TestGuiHelper.RunInProjectContext("PVM_PropertyChanged", (runtime, user, pSession) =>
        {
            var vm = new ProjectsViewModel(runtime);

            bool propertyChangedFired = false;
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProjectsViewModel.FilteredProjects) ||
                    e.PropertyName == nameof(ProjectsViewModel.SearchText))
                    propertyChangedFired = true;
            };

            vm.SearchText = "SomeFilter";

            Assert.IsTrue(propertyChangedFired);
        });
    }

    [TestMethod]
    public void CurrentUser_IsNotNull_WhenRuntimeHasUsers()
    {
        TestGuiHelper.RunInProjectContext("PVM_CurrentUser", (runtime, user, pSession) =>
        {
            var vm = new ProjectsViewModel(runtime);

            Assert.IsNotNull(vm.CurrentUser);
        });
    }
}
