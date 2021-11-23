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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using XTMF2.ModelSystemConstruct;
using XTMF2.UnitTests.Modules;
using XTMF2.RuntimeModules;
using static XTMF2.UnitTests.TestHelper;
using XTMF2.Editing;
using System.Text;

namespace XTMF2.UnitTests
{
    [TestClass]
    public class TestModelSystem
    {
        [TestMethod]
        public void ModelSystemPersistance()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            CommandError error = null;
            const string userName = "NewUser";
            const string projectName = "TestProject";
            const string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, out error), error?.Message);
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var session, out error).UsingIf(session, () =>
            {
                Assert.IsTrue(session.CreateNewModelSystem(user, modelSystemName, out var modelSystemHeader, out error), error?.Message);
                Assert.IsTrue(session.Save(out error));
            }), error?.Message);
            runtime.Shutdown();
            runtime = XTMFRuntime.CreateRuntime();
            userController = runtime.UserController;
            projectController = runtime.ProjectController;
            user = userController.GetUserByName(userName);
            Assert.IsTrue(projectController.GetProjectSession(user, user.AvailableProjects[0], out session, out error).UsingIf(session, () =>
             {
                 var modelSystems = session.ModelSystems;
                 Assert.AreEqual(1, modelSystems.Count);
                 Assert.AreEqual(modelSystemName, modelSystems[0].Name);
             }), error?.Message);
            //cleanup
            userController.Delete(user);
        }

        [TestMethod]
        public void CreateModelSystemOrGet()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            CommandError error = null;
            const string userName = "NewUser";
            const string projectName = "TestProject";
            const string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, out error), error?.Message);
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var session, out error).UsingIf(session, () =>
            {
                Assert.IsTrue(session.CreateOrGetModelSystem(user, modelSystemName, out var modelSystemHeader, out error), error?.Message);
                Assert.IsTrue(session.CreateOrGetModelSystem(user, modelSystemName, out var modelSystemHeader2, out error), error?.Message);
                Assert.AreSame(modelSystemHeader, modelSystemHeader2);
                Assert.IsTrue(session.Save(out error));
            }), error?.Message);
            //cleanup
            userController.Delete(user);
        }

        [TestMethod]
        public void GetModelSystemSession()
        {
            RunInModelSystemContext("GetModelSystemSession", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var globalBoundary = mSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(mSession.AddModelSystemStart(user, globalBoundary, "Start", Rectangle.Hidden, out Start start, out error), error?.Message);
                Assert.IsFalse(mSession.AddModelSystemStart(user, globalBoundary, "Start", Rectangle.Hidden, out Start start_, out error));
            });
        }

        [TestMethod]
        public void EnsureSameModelSystemSession()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            CommandError error = null;
            const string userName = "NewUser";
            const string userName2 = "NewUser2";
            const string projectName = "TestProject";
            const string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, out error), error?.Message);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, out error), error?.Message);
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var session, out error).UsingIf(session, () =>
            {
                // share the session with the second user
                Assert.IsTrue(session.ShareWith(user, user2, out error), error?.Message);
                // create a new model system for both users to try to edit
                Assert.IsTrue(session.CreateNewModelSystem(user, modelSystemName, out var modelSystemHeader, out error), error?.Message);
                Assert.IsTrue(session.EditModelSystem(user, modelSystemHeader, out var modelSystemSession, out error).UsingIf(
                    modelSystemSession, () =>
                    {
                        Assert.IsTrue(session.EditModelSystem(user2, modelSystemHeader, out var modelSystemSession2, out error).UsingIf(modelSystemSession2, () =>
                        {
                            Assert.AreSame(modelSystemSession, modelSystemSession2);
                        }), error?.Message);
                    }), error?.Message);
                Assert.IsTrue(session.Save(out error));
            }), error?.Message);

            //cleanup
            userController.Delete(user);
        }

        [TestMethod]
        public void EnsureDifferentModelSystemSession()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            CommandError error = null;
            const string userName = "NewUser";
            const string userName2 = "NewUser2";
            const string projectName = "TestProject";
            const string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, out error), error?.Message);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, out error), error?.Message);
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var session, out error).UsingIf(session, () =>
            {
                // share the session with the second user
                Assert.IsTrue(session.ShareWith(user, user2, out error), error?.Message);
                // create a new model system for both users to try to edit
                Assert.IsTrue(session.CreateNewModelSystem(user, modelSystemName, out var modelSystemHeader, out error), error?.Message);
                Assert.IsTrue(session.EditModelSystem(user, modelSystemHeader, out var modelSystemSession, out error).UsingIf(
                    modelSystemSession, () =>
                    {

                    }), error?.Message);
                Assert.IsTrue(session.EditModelSystem(user2, modelSystemHeader, out var modelSystemSession2, out error).UsingIf(modelSystemSession2, () =>
                {
                    Assert.AreNotSame(modelSystemSession, modelSystemSession2);
                }), error?.Message);
                Assert.IsTrue(session.Save(out error));
            }), error?.Message);

            //cleanup
            userController.Delete(user);
        }

        [TestMethod]
        public void ModelSystemSavedWithStartOnly()
        {
            RunInModelSystemContext("ModelSystemSavedWithStartOnly", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);

            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                // we shouldn't be able to add another start with the same name in the same boundary
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);
            });
        }

        [TestMethod]
        public void ModelSystemSavedWithNodeOnly()
        {
            RunInModelSystemContext("ModelSystemSavedWithNodeOnly", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), Rectangle.Hidden, out var mss, out error));
                Assert.AreEqual("MyMSS", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
            });
        }

        [TestMethod]
        public void ModelSystemSavedWithStartAndNode()
        {
            RunInModelSystemContext("ModelSystemSavedWithStartAndNode", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), Rectangle.Hidden, out var mss, out error));
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.AreEqual("MyMSS", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);
            });
        }

        [TestMethod]
        public void ModelSystemWithLink()
        {
            RunInModelSystemContext("ModelSystemWithLink", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), Rectangle.Hidden, out var mss, out error));
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss, out var link, out error), error?.Message);
                Assert.AreEqual("MyMSS", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden,
                    out var start, out error), error?.Message);
            });
        }

        [TestMethod]
        public void ModelSystemWithMultiLink()
        {
            RunInModelSystemContext("ModelSystemWithLink", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);

                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Execute", typeof(Execute), Rectangle.Hidden, out var mss, out error));
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Ignore1", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore1, out error));
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Ignore2", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore2, out error));
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Ignore3", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore3, out error));
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Hello World", typeof(SimpleTestModule), Rectangle.Hidden, out var hello, out error));


                Assert.IsTrue(mSession.AddLink(user, start, GetHook(start.Hooks, "ToExecute"), mss, out var link, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore1, out var link1, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore2, out var link2, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore3, out var link3, out error), error?.Message);

                Assert.AreNotSame(link, link1);
                Assert.AreSame(link1, link2);
                Assert.AreSame(link1, link3);

                Assert.IsTrue(mSession.AddLink(user, ignore1, GetHook(ignore1.Hooks, "To Ignore"), hello, out var toSame1, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, ignore2, GetHook(ignore2.Hooks, "To Ignore"), hello, out var toSame2, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, ignore3, GetHook(ignore3.Hooks, "To Ignore"), hello, out var toSame3, out error), error?.Message);

                Assert.AreNotSame(toSame1, toSame2);
                Assert.AreNotSame(toSame1, toSame3);
                Assert.AreNotSame(toSame2, toSame3);

                Assert.AreEqual("Execute", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreEqual(5, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(5, ms.GlobalBoundary.Links.Count);
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden,
                    out var start, out error), error?.Message);
            });
        }

        [TestMethod]
        public void ExportModelSystem()
        {
            RunInProjectContext("ExportModelSystem", (user, project) =>
            {
                CommandError error = null;
                var msName = "MSToExport";
                var startName = "MyStart";
                var nodeName = "MyNode";
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error), error?.Message);
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    using (session)
                    {
                        var ms = session.ModelSystem;
                        Assert.IsTrue(session.AddModelSystemStart(user, ms.GlobalBoundary, startName, Rectangle.Hidden, out var start, out error), error?.Message);
                        Assert.IsTrue(session.AddNode(user, ms.GlobalBoundary, nodeName, typeof(IgnoreResult<string>),
                            Rectangle.Hidden, out var node, out error), error?.Message);
                        Assert.IsTrue(session.AddLink(user, start, TestHelper.GetHook(start.Hooks, "ToExecute"), node, out var link, out error), error?.Message);
                        Assert.IsTrue(session.Save(out error), error?.Message);
                        Assert.IsFalse(project.ExportModelSystem(user, msHeader, tempFile.FullName, out error),
                            "The model system was exported while there was still a session using it!");
                        tempFile.Refresh();
                        Assert.IsFalse(tempFile.Exists, "The model system was exported even through it reported to fail to export!");
                    }
                    Assert.IsTrue(project.ExportModelSystem(user, msHeader, tempFile.FullName, out error), error?.Message);
                    tempFile.Refresh();
                    Assert.IsTrue(tempFile.Exists, "The exported model system does not exist even after confirming that it was exported successfully!");
                }
                finally
                {
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void ExportModelSystemMetaData()
        {
            RunInProjectContext("ExportModelSystem", (user, project) =>
            {
                CommandError error = null;
                var msName = "MSToExport";
                var startName = "MyStart";
                var nodeName = "MyNode";
                var msDescription = "Description of the model system";
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.IsTrue(msHeader.SetDescription(project, msDescription, out error), error?.Message);
                Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error), error?.Message);
                FileInfo tempFile = new FileInfo(Path.GetTempFileName());
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(XTMF2.XTMFRuntime).Assembly.Location);
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    using (session)
                    {
                        var ms = session.ModelSystem;
                        Assert.IsTrue(session.AddModelSystemStart(user, ms.GlobalBoundary, startName, Rectangle.Hidden, out var start, out error), error?.Message);
                        Assert.IsTrue(session.AddNode(user, ms.GlobalBoundary, nodeName, typeof(IgnoreResult<string>), Rectangle.Hidden, out var node, out error), error?.Message);
                        Assert.IsTrue(session.AddLink(user, start, TestHelper.GetHook(start.Hooks, "ToExecute"), node, out var link, out error), error?.Message);
                        Assert.IsTrue(session.Save(out error), error?.Message);
                    }
                    Assert.IsTrue(project.ExportModelSystem(user, msHeader, tempFile.FullName, out error), error?.Message);
                    tempFile.Refresh();
                    Assert.IsTrue(tempFile.Exists, "The exported model system does not exist even after confirming that it was exported successfully!");

                    // Now that we know that the exported model system exists, inspect the meta-data for the model system.
                    using var archive = ZipFile.OpenRead(tempFile.FullName);
                    var entry = archive.GetEntry("metadata.json");
                    Assert.IsNotNull(entry, "There was no entry for the model system's meta-data!");
                    byte[] buffer;
                    using (var entryStream = entry.Open())
                    {
                        buffer = new byte[entry.Length];
                        entryStream.Read(buffer, 0, buffer.Length);
                    }
                    var reader = new Utf8JsonReader(buffer);
                    Assert.IsTrue(reader.Read(), "Unable to read the initial object.");
                    Assert.IsTrue(reader.TokenType == JsonTokenType.StartObject, "The first element was not a start object");
                    bool readName = false, readDescription = false, readExportedOn = false, readExportedBy = false,
                            readVersionMajor = false, readVersionMinor = false;
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            if (reader.ValueTextEquals("Name"))
                            {
                                Assert.IsTrue(reader.Read(), "The reader was unable to read after a property name was declared!");
                                var value = reader.GetString();
                                Assert.IsNotNull(value, "No name was read.");
                                Assert.AreEqual(msName, value, "The name is not the same!");
                                readName = true;
                            }
                            else if (reader.ValueTextEquals("Description"))
                            {
                                Assert.IsTrue(reader.Read(), "The reader was unable to read after a property name was declared!");
                                var value = reader.GetString();
                                Assert.IsNotNull(value, "No description was read.");
                                Assert.AreEqual(msDescription, value, "The description is not the same!");
                                readDescription = true;
                            }
                            else if (reader.ValueTextEquals("ExportedOn"))
                            {
                                Assert.IsTrue(reader.Read(), "The reader was unable to read after a property name was declared!");
                                Assert.IsTrue(reader.TryGetDateTime(out var value), "We failed to read the date-time that the model system was exported on.");
                                readExportedOn = true;
                            }
                            else if (reader.ValueTextEquals("ExportedBy"))
                            {
                                Assert.IsTrue(reader.Read(), "The reader was unable to read after a property name was declared!");
                                var value = reader.GetString();
                                Assert.IsNotNull(value, "No description was read.");
                                Assert.AreEqual(user.UserName, value, "The exporting user name is not the same!");
                                readExportedBy = true;
                            }
                            else if (reader.ValueTextEquals("VersionMajor"))
                            {
                                Assert.IsTrue(reader.Read(), "The reader was unable to read after a property name was declared!");
                                Assert.IsTrue(reader.TryGetInt32(out var value), "Unable to create an int for the major version number.");
                                Assert.AreEqual(fvi.FileMajorPart, value, "The exported major version number was unexpected!");
                                readVersionMajor = true;
                            }
                            else if (reader.ValueTextEquals("VersionMinor"))
                            {
                                Assert.IsTrue(reader.Read(), "The reader was unable to read after a property name was declared!");
                                Assert.IsTrue(reader.TryGetInt32(out var value), "Unable to create an int for the minor version number.");
                                // TOOD: Automatically update the version number by referencing assembly information
                                Assert.AreEqual(fvi.FileMinorPart, value, "The exported minor version number was unexpected!");
                                readVersionMinor = true;
                            }
                            else
                            {
                                Assert.Fail($"Unknown meta-data property found: {System.Text.Encoding.UTF8.GetString(reader.ValueSpan)}");
                            }
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                    Assert.IsTrue(readName, "There was no property for the name of the model system!");
                    Assert.IsTrue(readDescription, "There was no property for the description of the model system!");
                    Assert.IsTrue(readExportedOn, "There was no property for the time the model system was exported on of the model system!");
                    Assert.IsTrue(readExportedBy, "There was no property for the name of the user that exported the model system!");
                    Assert.IsTrue(readVersionMajor, "There was no property for the major version number of XTMF that saved the model system!");
                    Assert.IsTrue(readVersionMinor, "There was no property for the minor version number of XTMF that saved the model system!");
                }
                finally
                {
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void ImportModelSystem()
        {
            RunInProjectContext("ImportModelSystem", (user, project) =>
            {
                CommandError error = null;
                var msName = "MSToExport";
                var importedName = "MSImported";
                var description = "A test model system.";
                var startName = "MyStart";
                var nodeName = "MyNode";
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.IsTrue(msHeader.SetDescription(project, description, out error), error?.Message);
                Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error), error?.Message);
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    using (session)
                    {
                        var ms = session.ModelSystem;
                        Assert.IsTrue(session.AddModelSystemStart(user, ms.GlobalBoundary, startName, Rectangle.Hidden, out var start, out error), error?.Message);
                        Assert.IsTrue(session.AddNode(user, ms.GlobalBoundary, nodeName, typeof(IgnoreResult<string>), Rectangle.Hidden, out var node, out error), error?.Message);
                        Assert.IsTrue(session.AddLink(user, start, TestHelper.GetHook(start.Hooks, "ToExecute"), node, out var link, out error), error?.Message);
                        Assert.IsTrue(session.Save(out error), error?.Message);
                    }
                    Assert.IsTrue(project.ExportModelSystem(user, msHeader, tempFile.FullName, out error), error?.Message);
                    Assert.IsTrue(project.ImportModelSystem(user, tempFile.FullName, importedName, out ModelSystemHeader importedHeader, out error), error?.Message);
                    Assert.IsNotNull(importedHeader, "The model system header was not set!");
                    Assert.AreEqual(importedName, importedHeader.Name, "The name of the imported model system was not the same as what was specified.");
                    Assert.AreEqual(description, importedHeader.Description);
                }
                finally
                {
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void ImportGivenModelSystem()
        {
            var modelSystemString = @"{""Types"":[{""Index"":0,""Type"":""XTMF2.UnitTests.Modules.SimpleTestModule, XTMF2.UnitTests, Version = 1.0.0.0, Culture = neutral, PublicKeyToken = null""}],""Boundaries"":[{""Name"":""global"",""Description"":"""",""Starts"":[{""Name"":""TestStart"",""Description"":"""",""Index"":0,""X"":10,""Y"":10}],""Nodes"":[{""Name"":""TestNode1"",""Description"":"""",""Type"":0,""X"":10,""Y"":10,""Width"":100,""Height"":100,""Index"":1}],""Boundaries"":[{""Name"":""TestBoundary1"",""Description"":"""",""Starts"":[],""Nodes"":[{""Name"":""TestNode2"",""Description"":"""",""Type"":0,""X"":10,""Y"":10,""Width"":100,""Height"":100,""Index"":2}],""Boundaries"":[],""Links"":[],""CommentBlocks"":[]}],""Links"":[{""Origin"":0,""Hook"":""ToExecute"",""Destination"":1}],""CommentBlocks"":[]}]}";
            var metadata = @"{""Name"":""MyMS""}";
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            void Write(string fileName, string text)
            {
                using var stream = File.OpenWrite(fileName);
                stream.Write(Encoding.UTF8.GetBytes(text).AsSpan());
            }
            TestHelper.RunInProjectContext(nameof(ImportGivenModelSystem), (user, project) =>
            {
                var tempDirName = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "XTMF-" + Guid.NewGuid());
                var exportPath = Path.GetTempFileName();
                try
                {
                    
                    CommandError error;
                    Directory.CreateDirectory(tempDirName);
                    Write(Path.Combine(tempDirName, "ModelSystem.xmsys"), modelSystemString);
                    Write(Path.Combine(tempDirName, "metadata.json"), metadata);
                    // The destination can not exist before we create the archive.
                    File.Delete(exportPath);
                    ZipFile.CreateFromDirectory(tempDirName, exportPath);
                    Assert.IsTrue(project.ImportModelSystem(user, exportPath, "TestModelSystem", out ModelSystemHeader importedHeader, out error), error?.Message);
                    Assert.IsTrue(project.EditModelSystem(user, importedHeader, out var modelSystemSession, out error).UsingIf(modelSystemSession, () =>
                     {
                         // If we get here then the model system session successfully loaded this test model system.
                     }), error?.Message);
                }
                finally
                {
                    try
                    {
                        Directory.Delete(tempDirName);
                        File.Delete(exportPath);
                    }
                    catch (IOException) { }
                }
            });
        }

        [TestMethod]
        public void ImportModelSystemBadUser()
        {
            RunInProjectContext("ImportModelSystemBadUser", (user, unauthorizedUser, project) =>
            {
                CommandError error = null;
                var msName = "MSToExport";
                var importedName = "MSImported";
                var description = "A test model system.";
                var startName = "MyStart";
                var nodeName = "MyNode";
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.IsTrue(msHeader.SetDescription(project, description, out error), error?.Message);
                Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error), error?.Message);
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    using (session)
                    {
                        var ms = session.ModelSystem;
                        Assert.IsTrue(session.AddModelSystemStart(user, ms.GlobalBoundary, startName, Rectangle.Hidden, out var start, out error), error?.Message);
                        Assert.IsTrue(session.AddNode(user, ms.GlobalBoundary, nodeName, typeof(IgnoreResult<string>), Rectangle.Hidden, out var node, out error), error?.Message);
                        Assert.IsTrue(session.AddLink(user, start, TestHelper.GetHook(start.Hooks, "ToExecute"), node, out var link, out error), error?.Message);
                        Assert.IsTrue(session.Save(out error), error?.Message);
                    }
                    Assert.IsTrue(project.ExportModelSystem(user, msHeader, tempFile.FullName, out error), error?.Message);
                    Assert.IsFalse(project.ImportModelSystem(unauthorizedUser, tempFile.FullName, importedName, out ModelSystemHeader importedHeader, out error),
                        "An unauthorized user was able to import a model system!");
                    Assert.IsNull(importedHeader, "A model system header was created even though the import model system operation failed!");
                }
                finally
                {
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void ImportModelSystemInvalidZipFile()
        {
            RunInProjectContext("ImportModelSystemBadFilePath", (user, project) =>
            {
                CommandError error = null;
                var msName = "MSToExport";
                var importedName = "MSImported";
                var description = "A test model system.";
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.IsTrue(msHeader.SetDescription(project, description, out error), error?.Message);
                Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error), error?.Message);
                var tempFile = new FileInfo(Path.GetTempFileName());
                Assert.IsFalse(project.ImportModelSystem(user, tempFile.FullName, importedName, out ModelSystemHeader importedHeader, out error),
                    "An unauthorized user was able to import a model system!");
                Assert.IsNull(importedHeader, "A model system header was created even though the import model system operation failed!");
            });
        }

        [TestMethod]
        public void ImportModelSystemValidZipFileInvalidModelSystemFile()
        {
            RunInProjectContext("ImportModelSystemBadFilePath", (user, project) =>
            {
                CommandError error = null;
                var msName = "MSToExport";
                var importedName = "MSImported";
                var description = "A test model system.";
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.IsTrue(msHeader.SetDescription(project, description, out error), error?.Message);
                Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error), error?.Message);
                var tempFile = new FileInfo(Path.GetTempFileName());
                var tempDir = new FileInfo(Path.GetTempFileName());
                tempDir.Delete();
                tempFile.Delete();
                try
                {
                    Directory.CreateDirectory(tempDir.FullName);
                    ZipFile.CreateFromDirectory(tempDir.FullName, tempFile.FullName);
                    Assert.IsFalse(project.ImportModelSystem(user, tempFile.FullName, importedName, out ModelSystemHeader importedHeader, out error),
                        "An unauthorized user was able to import a model system!");
                    Assert.IsNull(importedHeader, "A model system header was created even though the import model system operation failed!");
                }
                finally
                {
                    if (Directory.Exists(tempDir.FullName))
                    {
                        Directory.Delete(tempDir.FullName, true);
                    }
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void RenameModelSystem()
        {
            RunInProjectContext("RenameModelSystem", (user, project) =>
            {
                CommandError error = null;
                var msName = "MSToExport";
                var newName = "NewMSName";
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.AreEqual(msName, msHeader.Name);
                Assert.IsTrue(project.RenameModelSystem(user, msHeader, newName, out error), error?.Message);
                Assert.AreEqual(newName, msHeader.Name);
            });
        }
    }
}
