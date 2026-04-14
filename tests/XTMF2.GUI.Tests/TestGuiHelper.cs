using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Configuration;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Tests;

/// <summary>
/// Helpers for creating GUI view-model objects inside a properly bootstrapped
/// <see cref="ModelSystemSession"/> context.
/// </summary>
internal static class TestGuiHelper
{
    private const string ProjectName = "TestProject";
    private const string ModelSystemName = "ModelSystem1";

    /// <summary>
    /// Isolated user directory for the XTMF2.GUI.Tests assembly.
    /// Keeping this separate from the unit-test directory prevents cross-process
    /// file-handle collisions when <c>dotnet test</c> runs both assemblies concurrently.
    /// </summary>
    private static readonly string s_testUserDirectory =
        Path.Combine(Path.GetTempPath(), "XTMF2", "GUITests", "Users");

    internal static void RunInModelSystemContext(
        string name,
        Action<User, ProjectSession, ModelSystemSession> action)
    {
        var runtime = XTMFRuntime.CreateRuntime(s_testUserDirectory);
        var userController = runtime.UserController;
        var projectController = runtime.ProjectController;
        string userName = name + "GUITempUser";

        userController.Delete(userName);
        Assert.IsTrue(userController.CreateNew(userName, false, out var user, out var error), error?.Message);
        try
        {
            Assert.IsTrue(
                projectController.CreateNewProject(user, ProjectName, out var projectSession, out error)
                    .UsingIf(projectSession, () =>
                    {
                        Assert.IsNotNull(projectSession);
                        
                        Assert.IsTrue(projectSession.CreateNewModelSystem(user, ModelSystemName,
                            out var msHeader, out error), error?.Message);
                        Assert.IsTrue(
                            projectSession.EditModelSystem(user, msHeader!, out var msSession, out error)
                                .UsingIf(msSession, () =>
                                {
                                    Assert.IsNotNull(msSession);
                                    action(user, projectSession, msSession);
                                }), error?.Message);
                    }), error?.Message);
        }
        finally
        {
            userController.Delete(user);
        }
    }

    internal static void RunInProjectContext(
        string name,
        Action<XTMFRuntime, User, ProjectSession> action)
    {
        var runtime = XTMFRuntime.CreateRuntime(s_testUserDirectory);
        var userController = runtime.UserController;
        var projectController = runtime.ProjectController;
        string userName = name + "GUITempUser";

        userController.Delete(userName);
        Assert.IsTrue(userController.CreateNew(userName, false, out var user, out var error), error?.Message);
        try
        {
            Assert.IsTrue(
                projectController.CreateNewProject(user, ProjectName, out var projectSession, out error)
                    .UsingIf(projectSession, () =>
                    {
                        Assert.IsNotNull(projectSession);
                        action(runtime, user, projectSession);
                    }), error?.Message);
        }
        finally
        {
            userController.Delete(user);
        }
    }

    internal static NodeViewModel CreateNodeViewModel(
        Node node, ModelSystemSession session, User user)
        => new NodeViewModel(node, session, user);

    internal static CommentBlockViewModel CreateCommentBlockViewModel(
        CommentBlock block, ModelSystemSession session, User user)
        => new CommentBlockViewModel(block, session, user);
}
