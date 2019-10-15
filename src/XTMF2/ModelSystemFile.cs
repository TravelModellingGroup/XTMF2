/*
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
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using XTMF2.Editing;
using System.IO;
using System.Diagnostics;

namespace XTMF2
{
    /// <summary>
    /// This class is designed for interacting with model systems that have been exported.
    /// </summary>
    public sealed class ModelSystemFile
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
        /// The name of the file within the archive for where the model system was stored.
        /// </summary>
        internal const string ModelSystemFilePath = "ModelSystem.xmsys";
        /// <summary>
        /// The name of the file within the archive for where the meta-data was stored.
        /// </summary>
        internal const string MetaDataFilePath = "metadata.json";

        /// <summary>
        /// The name of the model system that was exported
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The description of the model system that was exported
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The name of the user that exported the model system.
        /// </summary>
        public string ExportedBy { get; }

        /// <summary>
        /// The time that the model system was exported.
        /// </summary>
        public DateTime ExportedOn { get; }

        /// <summary>
        /// The version number (X.Y) of XTMF that exported the model system.
        /// </summary>
        public (int Major, int Minor) ExportingXTMFVersion { get; }

        /// <summary>
        /// Export the model system to the given path.
        /// </summary>
        /// <param name="projectSession">An editing session for the project that is being exported from.</param>
        /// <param name="user">The use that is exporting. Permissions are not checked.</param>
        /// <param name="modelSystemHeader">The header for the model system that is to be exported.</param>
        /// <param name="exportPath">The path to export the model system to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        internal static bool ExportModelSystem(ProjectSession projectSession, User user,
            ModelSystemHeader modelSystemHeader, string exportPath, ref string error)
        {
            var tempDirName = string.Empty;
            try
            {
                var project = projectSession.Project;
                tempDirName = Path.Combine(Path.GetTempPath(), "XTMF-" + project.Name + modelSystemHeader.Name + Guid.NewGuid());
                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                var tempDir = new DirectoryInfo(tempDirName);
                if (!tempDir.Exists)
                {
                    tempDir.Create();
                }
                // copy in the model system file
                File.Copy(modelSystemHeader.ModelSystemPath, Path.Combine(tempDir.FullName, ModelSystemFilePath));
                using (var metadataStream = File.OpenWrite(Path.Combine(tempDir.FullName, MetaDataFilePath)))
                using (var writer = new Utf8JsonWriter(metadataStream))
                {
                    writer.WriteStartObject();
                    writer.WriteString(PropertyName, modelSystemHeader.Name);
                    writer.WriteString(PropertyDescription, modelSystemHeader.Description);
                    writer.WriteString(PropertyExportedOn, DateTime.UtcNow);
                    writer.WriteString(PropertyExportedBy, user.UserName);
                    writer.WriteNumber(PropertyVersionMajor, fvi.FileMajorPart);
                    writer.WriteNumber(PropertyVersionMinor, fvi.FileMinorPart);
                    writer.WriteEndObject();
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
                // Try to clean up the temporary directory if it still exists.
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
                    // If we don't have access to the temporary storage there is nothing else that we can do.
                }
#pragma warning restore CA1031
            }
            return false;
        }
    }
}
