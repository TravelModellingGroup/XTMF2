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
        public string Name { get; private set; }

        /// <summary>
        /// The description of the model system that was exported
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// The name of the user that exported the model system.
        /// </summary>
        public string ExportedBy { get; private set; }

        /// <summary>
        /// The time that the model system was exported.
        /// </summary>
        public DateTime ExportedOn { get; private set; }

        /// <summary>
        /// The version number (X.Y) of XTMF that exported the model system.
        /// </summary>
        public (int Major, int Minor) ExportingXTMFVersion => (_majorVersion, _minorVersion);

        private int _majorVersion, _minorVersion;

        /// <summary>
        /// The path the model system file was loaded from.
        /// </summary>
        private readonly string _modelSystemFilePath = null;

        /// <summary>
        /// The project file archive containing this model system file.
        /// </summary>
        private readonly ZipArchive _archive;

        /// <summary>
        /// Checks if the model system file is contained within a project file.
        /// </summary>
        public bool IsContainedInProjectFile => !(_archive is null);

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

        /// <summary>
        /// Loads a reference to the model system file, and loads its meta-data.
        /// </summary>
        /// <param name="filePath">The path to the model system file.</param>
        /// <param name="msf">The resulting model system file, null if the operation fails.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        internal static bool LoadModelSystemFile(string filePath, out ModelSystemFile msf, ref string error)
        {
            msf = null;
            var toReturn = new ModelSystemFile(filePath);
            try
            {
                using var stream = File.OpenRead(filePath);
                if(!LoadModelSystemFile(toReturn, stream, ref error))
                {
                    return false;
                }
                msf = toReturn;
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            return false;
        }

        /// <summary>
        /// Load a model system file from within a project file.
        /// </summary>
        /// <param name="archive">The project file archive.</param>
        /// <param name="stream">A stream to this model system file.</param>
        /// <param name="msf">The resulting model system file.</param>
        /// <param name="error">An error message if loading the model system file fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        internal static bool LoadModelSystemFile(ZipArchive archive, string path, out ModelSystemFile msf, ref string error)
        {
            msf = null;
            try
            {
                var entry = archive.GetEntry(path);
                if (entry is null)
                {
                    error = $"No model system file was found within the project file with the name {path}";
                    return false;
                }
                using var stream = entry.Open();
                var toReturn = new ModelSystemFile(archive, path);
                if (!LoadModelSystemFile(toReturn, stream, ref error))
                {
                    return false;
                }
                msf = toReturn;
                return true;
            }
            catch(IOException e)
            {
                error = e.Message;
            }
            return false;
        }

        private static bool LoadModelSystemFile(ModelSystemFile toReturn, Stream stream, ref string error)
        {
            var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry(MetaDataFilePath);
            if (entry is null)
            {
                error = "The archive did not contain a meta-data file!";
                return false;
            }
            byte[] buffer;
            using (var entryStream = entry.Open())
            {
                buffer = new byte[entry.Length];
                var length = entryStream.Read(buffer, 0, buffer.Length);
                if (length != buffer.Length)
                {
                    error = "Unable to read the meta-data file";
                    return false;
                }
            }
            var reader = new Utf8JsonReader(buffer);
            if (!reader.Read())
            {
                error = "Unable to read the initial object.";
                return false;
            }
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals(PropertyName))
                    {
                        if (!reader.Read())
                        {
                            error = "The reader was unable to read after a property name was declared!";
                            return false;
                        }
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            error = "Expected a string token after reading a Name property!";
                        }
                        toReturn.Name = reader.GetString();
                    }
                    else if (reader.ValueTextEquals(PropertyDescription))
                    {
                        if (!reader.Read())
                        {
                            error = "The reader was unable to read after a property name was declared!";
                            return false;
                        }
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            error = "Expected a string token after reading a Description property!";
                        }
                        toReturn.Description = reader.GetString();
                    }
                    else if (reader.ValueTextEquals(PropertyExportedOn))
                    {
                        if (!reader.Read())
                        {
                            error = "The reader was unable to read after a property name was declared!";
                            return false;
                        }
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            error = "Expected a string token after reading a ExportedOn property!";
                        }
                        var tempStr = reader.GetString();
                        if (!DateTime.TryParse(tempStr, out var tempDateTime))
                        {
                            error = $"Unable to parse '{tempStr}' as a date-time.";
                            return false;
                        }
                        toReturn.ExportedOn = tempDateTime;
                    }
                    else if (reader.ValueTextEquals(PropertyExportedBy))
                    {
                        if (!reader.Read())
                        {
                            error = "The reader was unable to read after a property name was declared!";
                            return false;
                        }
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            error = "Expected a string token after reading a ExportedBy property!";
                        }
                        toReturn.ExportedBy = reader.GetString();
                    }
                    else if (reader.ValueTextEquals(PropertyVersionMajor))
                    {
                        if (!reader.Read())
                        {
                            error = "The reader was unable to read after a property name was declared!";
                            return false;
                        }
                        if (reader.TokenType != JsonTokenType.Number)
                        {
                            error = "Expected a number token after reading a VersionMajor property!";
                        }
                        if (!reader.TryGetInt32(out toReturn._majorVersion))
                        {
                            error = "Unable to read the major version number.";
                            return false;
                        }
                    }
                    else if (reader.ValueTextEquals(PropertyVersionMinor))
                    {
                        if (!reader.Read())
                        {
                            error = "The reader was unable to read after a property name was declared!";
                            return false;
                        }
                        if (reader.TokenType != JsonTokenType.Number)
                        {
                            error = "Expected a number token after reading a VersionMinor property!";
                        }
                        if (!reader.TryGetInt32(out toReturn._minorVersion))
                        {
                            error = "Unable to read the minor version number.";
                            return false;
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
            return true;
        }

        private ModelSystemFile(string path)
        {
            _modelSystemFilePath = path;
        }

        public ModelSystemFile(ZipArchive archive, string path)
        {
            _archive = archive;
            _modelSystemFilePath = path;
        }

        /// <summary>
        /// Extract the model system contained within the model system file
        /// to the given path.
        /// </summary>
        /// <param name="modelSystemPath">The path to try to save the model system to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        internal bool ExtractModelSystemTo(string modelSystemPath, ref string error)
        {
            try
            {
                using var archive = 
                    IsContainedInProjectFile ? 
                      new ZipArchive(_archive.GetEntry(_modelSystemFilePath).Open(), ZipArchiveMode.Read, false)
                    : ZipFile.OpenRead(_modelSystemFilePath);
                var entry = archive.GetEntry(ModelSystemFilePath);
                if(entry is null)
                {
                    error = "The model system file does not contain a model system within it!";
                    return false;
                }
                // Make sure that the path to the file exists, and if not create the directories.
                var destinationPath = new FileInfo(modelSystemPath);
                if(!destinationPath.Exists)
                {
                    var dir = destinationPath.Directory;
                    if(!dir.Exists)
                    {
                        dir.Create();
                    }
                }
                entry.ExtractToFile(modelSystemPath, true);
                return true;
            }
            catch(IOException e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
