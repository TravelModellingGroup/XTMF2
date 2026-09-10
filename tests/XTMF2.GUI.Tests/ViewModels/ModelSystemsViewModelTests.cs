using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class ModelSystemsViewModelTests
{
    private static readonly string TestUserDirectory =
        Path.Combine(Path.GetTempPath(), "XTMF2", "GUITests", "Users");

    [TestMethod]
    public void RenameModelSystemThroughViewModelPersistsProjectAndNode()
    {
        const string userName = nameof(RenameModelSystemThroughViewModelPersistsProjectAndNode) + "User";
        const string projectName = "TestProject";
        const string modelSystemName = "ModelSystem1";
        const string renamedModelSystemName = "RenamedModelSystem";
        var runtime = XTMFRuntime.CreateRuntime(TestUserDirectory);
        var userController = runtime.UserController;
        var projectController = runtime.ProjectController;
        CommandError? error = null;

        userController.Delete(userName);
        Assert.IsTrue(userController.CreateNew(userName, false, out var user, out error), error?.Message);
        try
        {
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var projectSession, out error), error?.Message);
            using (projectSession)
            {
                Assert.IsTrue(projectSession.CreateNewModelSystem(user, modelSystemName, out var header, out error), error?.Message);
                Assert.IsTrue(projectSession.EditModelSystem(user, header, out var modelSystemSession, out error), error?.Message);
                using (modelSystemSession)
                {
                    Assert.IsTrue(modelSystemSession.AddNode(user, modelSystemSession.ModelSystem.GlobalBoundary,
                        "TestNode", typeof(SimpleGuiTestModule), Rectangle.Hidden, out _, out error), error?.Message);
                    Assert.IsTrue(modelSystemSession.Save(out error), error?.Message);
                }

                using var viewModel = new ModelSystemsViewModel(runtime, user, projectSession.Project);
                viewModel.SelectedModelSystem = header;
                Assert.IsTrue(viewModel.RenameSelectedModelSystem(renamedModelSystemName, out error), error?.Message);
            }

            runtime.Shutdown();
            runtime = XTMFRuntime.CreateRuntime(TestUserDirectory);
            user = runtime.UserController.GetUserByName(userName)!;
            Assert.IsTrue(runtime.ProjectController.GetProject(user, projectName, out var reloadedProject, out error), error?.Message);
            Assert.AreEqual(projectName, reloadedProject.Name);
            Assert.IsTrue(runtime.ProjectController.GetProjectSession(user, reloadedProject, out var reloadedProjectSession, out error), error?.Message);
            using (reloadedProjectSession)
            {
                Assert.IsTrue(reloadedProjectSession.GetModelSystemHeader(user, renamedModelSystemName, out var reloadedHeader, out error), error?.Message);
                Assert.IsTrue(reloadedProjectSession.EditModelSystem(user, reloadedHeader, out var reloadedModelSystemSession, out error), error?.Message);
                using (reloadedModelSystemSession)
                {
                    Assert.HasCount(1, reloadedModelSystemSession.ModelSystem.GlobalBoundary.Modules);
                }
            }
        }
        finally
        {
            runtime.UserController.Delete(user!);
        }
    }
}