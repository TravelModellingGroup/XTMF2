/*
    Copyright 2017 University of Toronto

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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using XTMF2;
using XTMF2.Bus;
using XTMF2.Editing;
using XTMF2.RuntimeModules;

namespace TestXTMF
{
    static class TestHelper
    {
        /// <summary>
        /// Create a context to edit a model system for testing
        /// </summary>
        /// <param name="name">A unique name for the test</param>
        /// <param name="toExecute">The logic to execute inside of a model system context</param>
        internal static void RunInModelSystemContext(string name, Action<User, ProjectSession, ModelSystemSession> toExecute)
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            string userName = name + "TempUser";
            string projectName = "TestProject";
            string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, ref error), error);
            try
            {
                Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var projectSession, ref error).UsingIf(projectSession, () =>
                {
                    Assert.IsTrue(projectSession.CreateNewModelSystem(modelSystemName, out var modelSystemHeader, ref error), error);
                    Assert.IsTrue(projectSession.EditModelSystem(user, modelSystemHeader, out var modelSystemSession, ref error).UsingIf(modelSystemSession, () =>
                    {
                        toExecute(user, projectSession, modelSystemSession);
                    }), error);
                }), error);
            }
            finally
            {
                //cleanup
                userController.Delete(user);
            }
        }

        /// <summary>
        /// Create a context to edit a model system for testing where
        /// XTMF will be saved and then shutdown between contexts.
        /// </summary>
        /// <param name="name">A unique name for the test</param>
        /// <param name="toExecuteFirst">The logic to execute before XTMF has been restarted</param>
        /// <param name="toExecuteSecond">The logic to execute after XTMF has been restarted</param>
        internal static void RunInModelSystemContext(string name, Action<User, ProjectSession, ModelSystemSession> toExecuteFirst, Action<User, ProjectSession, ModelSystemSession> toExecuteSecond)
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            string userName = name + "TempUser";
            string projectName = "TestProject";
            string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);

            Assert.IsTrue(userController.CreateNew(userName, false, out var user, ref error), error);
            try
            {
                Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var projectSession, ref error).UsingIf(projectSession, () =>
                {
                    Assert.IsTrue(projectSession.CreateNewModelSystem(modelSystemName, out var modelSystemHeader, ref error), error);
                    Assert.IsTrue(projectSession.EditModelSystem(user, modelSystemHeader, out var modelSystemSession, ref error).UsingIf(modelSystemSession, () =>
                    {
                        toExecuteFirst(user, projectSession, modelSystemSession);
                        Assert.IsTrue(modelSystemSession.Save(ref error), error);
                    }), error);
                    Assert.IsTrue(projectSession.Save(ref error));
                }), error);

                runtime.Shutdown();

                runtime = XTMFRuntime.CreateRuntime();
                userController = runtime.UserController;
                projectController = runtime.ProjectController;
                user = userController.GetUserByName(userName);
                Assert.IsTrue(projectController.GetProject(userName, projectName, out var project, ref error), error);
                Assert.IsTrue(projectController.GetProjectSession(user, project, out projectSession, ref error).UsingIf(projectSession, () =>
                {
                    Assert.IsTrue(projectSession.GetModelSystemHeader(user, modelSystemName, out var modelSystemHeader, ref error), error);
                    Assert.IsTrue(projectSession.EditModelSystem(user, modelSystemHeader, out var modelSystemSession, ref error).UsingIf(modelSystemSession, () =>
                    {
                        toExecuteSecond(user, projectSession, modelSystemSession);
                    }), error);
                }), error);
            }
            finally
            {
                //cleanup
                userController.Delete(user);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="runLogic"></param>
        public static void CreateRunClient(bool startClientProcess, Action<RunBusHost> runLogic)
        {
            string error = null;
            var id = startClientProcess ? Guid.NewGuid().ToString() : "123";
            var xtmfRunFileName = typeof(XTMF2.Run.CreateStreams).GetTypeInfo().Assembly.Location;
            var testFileName = Path.GetFullPath(typeof(TestHelper).GetTypeInfo().Assembly.Location);
            Process client = null;
            try
            {
                Assert.IsTrue(XTMF2.Run.CreateStreams.CreateNewNamedPipeHost(id, out var hostStream, ref error,
                () =>
                {
                    if (startClientProcess)
                    {
                        try
                        {
                            var startInfo = new ProcessStartInfo()
                            {
                                FileName = "dotnet",
                                Arguments = $"\"{xtmfRunFileName}\" -loadDLL \"{testFileName}\" -namedPipe \"{id}\"",
                                CreateNoWindow = false,
                                WorkingDirectory = Path.GetDirectoryName(typeof(TestHelper).GetTypeInfo().Assembly.Location)
                            };
                            client = new Process()
                            {
                                StartInfo = startInfo
                            };
                            client.EnableRaisingEvents = true;
                            client.Start();
                        }
                        catch (Exception e)
                        {
                            Assert.Fail(e.Message);
                        }
                    }
                }).UsingIf(hostStream,
            () =>
            {
                if (startClientProcess)
                {
                    Assert.IsNotNull(client, "The client was never created!");
                }
                runLogic(new RunBusHost(hostStream, true));
            }), error);
            }
            finally
            {
                if (startClientProcess)
                {
                    try
                    {
                        client?.Kill();
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {

                    }
                    catch (InvalidOperationException)
                    {
                        // This will cover the case that the client has already exited
                    }
                }
            }
        }

        /// <summary>
        /// Get a node hook with the given name.
        /// </summary>
        /// <param name="hooks">The stet of hooks available</param>
        /// <param name="name">The name of the hook to access</param>
        /// <returns>The hook with the given name, or null if it isn't found.</returns>
        public static NodeHook GetHook(IReadOnlyList<NodeHook> hooks, string name)
        {
            return hooks.FirstOrDefault(hook => hook.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Automate the lifetime of a temporary file.
        /// </summary>
        /// <param name="action">The action to perform while the file exists.</param>
        internal static void CreateTemporaryFile(Action<string> action)
        {
            string path = null;
            try
            {
                path = Path.GetTempFileName();
                action(path);
            }
            finally
            {
                try
                {
                    if (path != null)
                    {
                        File.Delete(path);
                    }
                }
                catch(IOException)
                {

                }
            }
        }

        /// <summary>
        /// Generate a new basic parameter with the given value
        /// </summary>
        /// <typeparam name="T">The return type</typeparam>
        /// <param name="value">The value to be returned</param>
        /// <param name="moduleName">The name of the module to create.</param>
        /// <returns></returns>
        internal static IFunction<T> CreateParameter<T>(T value, string moduleName = null)
        {
            return new BasicParameter<T>()
            {
                Name = moduleName,
                Value = value
            };
        }

        /// <summary>
        /// Execute the action and check to see if it throws an exception
        /// </summary>
        /// <param name="a">The action to execute.</param>
        /// <param name="e">The exception if one occurred</param>
        /// <returns>True if there was no exception, false otherwise with error stored in e.</returns>
        internal static bool NoExecutionErrors(Action a, out Exception e)
        {
            try
            {
                a();
            }
            catch(Exception e2)
            {
                e = e2;
                return false;
            }
            e = null;
            return true;
        }

    }
}
