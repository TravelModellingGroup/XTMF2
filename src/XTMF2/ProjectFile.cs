﻿/*
    Copyright 2019 University of Toronto

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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using XTMF2.Editing;

namespace XTMF2
{
    /// <summary>
    /// This class is designed to handle the importing and exporting of a project
    /// </summary>
    public sealed class ProjectFile
    {
        /// <summary>
        /// A string used for the meta-data to gather the name of the model system.
        /// </summary>
        internal const string PropertyName = "Name";
        /// <summary>
        /// A string used in the meta-data to provide a description of the model system.
        /// </summary>
        internal const string PropertyDescription = "Description";
        /// <summary>
        /// A string used in the meta-data to give the name of the user that exported the model system
        /// </summary>
        internal const string PropertyExportedBy = "ExportedBy";
        /// <summary>
        /// A string used by the meta-data to give what time in UTC that the model system was exported at.
        /// </summary>
        internal const string PropertyExportedOn = "ExportedOn";
        /// <summary>
        /// A string used by the meta-data to indicate what version of XTMF exported the model system.
        /// </summary>
        internal const string PropertyVersionMajor = "VersionMajor";
        /// <summary>
        /// A string used by the meta-data to indicate what version of XTMF exported the model system.
        /// </summary>
        internal const string PropertyVersionMinor = "VersionMinor";
        /// <summary>
        /// The number of model systems contained within the project.
        /// </summary>
        private const string PropertyModelSystems = "ModelSystems";
        /// <summary>
        /// The name of the file within the archive for where the meta-data was stored.
        /// </summary>
        internal const string MetaDataFilePath = "metadata.json";

        /// <summary>
        /// Have a user export the project to a given path.  The project must not have any active editing sessions.
        /// </summary>
        /// <param name="projectSession">The project session to export.</param>
        /// <param name="user">The user that is exporting the project.</param>
        /// <param name="exportPath">The path to export the project to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation completes successfully, false otherwise with an error message.</returns>
        internal static bool ExportProject(ProjectSession projectSession, User user, string exportPath, ref string error)
        {
            if (projectSession is null)
            {
                throw new ArgumentNullException(nameof(projectSession));
            }
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var project = projectSession.Project;
            // we need this declared outside of the
            string tempDirName = Path.Combine(Path.GetTempPath(), "XTMF-" + project.Name + Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(tempDirName);
                var headers = project.ModelSystems;
                // Write meta-data
                WriteMetaData(tempDirName, project, user, headers);
                if(!WriteModelSystems(projectSession, tempDirName, user, headers, ref error))
                {
                    return false;
                }
                // Zip the temporary directory and store it.
                ZipFile.CreateFromDirectory(tempDirName, exportPath);
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            finally
            {
                try
                {
                    var tempDir = new DirectoryInfo(tempDirName);
                    if (tempDir.Exists)
                    {
                        tempDir.Delete(true);
                    }
                }
                /*
                 * This will warn that we should catch a more specific exception however there is no recovery in any case.
                 * The operation has already been successful even if we are unable to clean up the temporary storage.
                 */
#pragma warning disable CA1031
                catch (IOException)
                {

                }
#pragma warning restore CA1031
            }
            return false;
        }

        private static bool WriteModelSystems(ProjectSession projectSession, string tempDirName, User user,
            ReadOnlyObservableCollection<ModelSystemHeader> headers, ref string error)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                if (!ModelSystemFile.ExportModelSystem(projectSession, user, headers[i], Path.Combine(tempDirName, $"{i}.xmsys"), ref error))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Write a meta-data file for the 
        /// </summary>
        /// <param name="tempDirName"></param>
        private static void WriteMetaData(string tempDirName, Project project, User user, 
            ReadOnlyObservableCollection<ModelSystemHeader> modelSystemHeaders)
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            using var stream = File.OpenWrite(Path.Combine(tempDirName, MetaDataFilePath));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString(PropertyName, project.Name);
            writer.WriteString(PropertyDescription, project.Description);
            writer.WriteString(PropertyExportedOn, DateTime.UtcNow);
            writer.WriteString(PropertyExportedBy, user.UserName);
            writer.WriteNumber(PropertyVersionMajor, fvi.FileMajorPart);
            writer.WriteNumber(PropertyVersionMinor, fvi.FileMinorPart);
            writer.WriteStartArray(PropertyModelSystems);
            for (int i = 0; i < modelSystemHeaders.Count; i++)
            {
                writer.WriteStringValue($"{i}.xmsys");
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
