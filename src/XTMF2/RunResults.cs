/*
    Copyright 2020 University of Toronto

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
using System.IO;

namespace XTMF2
{
    /// <summary>
    /// Contains the results from a previously executed run.
    /// </summary>
    public sealed class RunResults
    {

        public bool Completed { get; private set; }

        public bool CompletedSuccessfully => !HasError && Completed;

        public bool HasError { get; private set; }


        public string? Error
        {
            get
            {
                if (!HasError)
                {
                    return null;
                }
                return ErrorModuleName is null ?
                       ErrorMessage + "\r\n" + ErrorStackTrace :
                       ErrorModuleName + " -> " + ErrorMessage + "\r\n" + ErrorStackTrace;
            }
        }

        public string? ErrorMessage { get; private set; }

        public string? ErrorStackTrace { get; private set; }

        public string? ErrorModuleName { get; private set; }

        public string RunDirectory { get; private set; }

        private const string ResultsFile = "XTMF.RunResults.json";

        /// <summary>
        /// Construct the run results from a run directory.
        /// </summary>
        /// <param name="runDirectory">The directory that the run was executed in.</param>
        public RunResults(string runDirectory)
        {
            RunDirectory = runDirectory;
            try
            {
                FileInfo info = new FileInfo(Path.Combine(runDirectory, ResultsFile));
                if (info.Exists)
                {
                    byte[] buffer = File.ReadAllBytes(info.FullName);
                    var reader = new Utf8JsonReader(buffer.AsSpan());
                    while(reader.Read())
                    {
                        if(reader.TokenType == JsonTokenType.PropertyName)
                        {
                            if(reader.ValueTextEquals(nameof(ErrorMessage)))
                            {
                                if (!reader.Read() && reader.TokenType != JsonTokenType.String)
                                {
                                    continue;
                                }
                                ErrorMessage = reader.GetString();
                            }
                            else if(reader.ValueTextEquals(nameof(ErrorModuleName)))
                            {
                                if (!reader.Read() && reader.TokenType != JsonTokenType.String)
                                {
                                    continue;
                                }
                                ErrorModuleName = reader.GetString();
                            }
                            else if(reader.ValueTextEquals(nameof(ErrorStackTrace)))
                            {
                                if (!reader.Read() && reader.TokenType != JsonTokenType.String)
                                {
                                    continue;
                                }
                                ErrorStackTrace = reader.GetString();
                            }
                            else if(reader.ValueTextEquals(nameof(Completed)))
                            {
                                if(!reader.Read() && reader.TokenType != JsonTokenType.String)
                                {
                                    continue;
                                }
                                Completed = reader.GetBoolean();
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                    // We have an error if at least one of the following are not null
                    HasError = !(ErrorMessage is null) || !(ErrorModuleName is null) || !(ErrorStackTrace is null);
                }
                else
                {
                    Completed = false;
                    HasError = false;
                }
            }
            catch
            {

            }
        }

        /// <summary>
        /// Store that the run completed successfully.
        /// </summary>
        /// <param name="runDirectory">The directory that the run was completed in.</param>
        /// <returns></returns>
        internal static void WriteRunCompleted(string runDirectory)
        {
            using var stream = File.OpenWrite(Path.Combine(runDirectory, ResultsFile));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteBoolean(nameof(Completed), true);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Store that the run failed with an error.
        /// </summary>
        /// <param name="runDirectory">The directory that the run was completed in.</param>
        /// <param name="error">The error that should be stored.</param>
        internal static void WriteError(string runDirectory, Exception error)
        {
            while(error is AggregateException && error.InnerException != null)
            {
                error = error.InnerException;
            }
            using var stream = File.OpenWrite(Path.Combine(runDirectory, ResultsFile));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteBoolean(nameof(Completed), false);
            writer.WriteString(nameof(ErrorMessage), error.Message);
            writer.WriteString(nameof(ErrorStackTrace), error.StackTrace);
            if(error is XTMFRuntimeException xtmfError)
            {
                writer.WriteString(nameof(ErrorModuleName),
                    xtmfError.FailingModule?.Name ?? String.Empty);
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Store that the run had a validation error.
        /// </summary>
        /// <param name="runDirectory">The directory that the run was executed in.</param>
        /// <param name="moduleName">The name of the module that had the error.</param>
        /// <param name="errorMessage">A description of the error.</param>
        internal static void WriteValidationError(string runDirectory, string? moduleName, string? errorMessage)
        {
            using var stream = File.OpenWrite(Path.Combine(runDirectory, ResultsFile));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteBoolean(nameof(Completed), false);
            writer.WriteString(nameof(ErrorMessage), errorMessage ?? string.Empty);           
            writer.WriteString(nameof(ErrorModuleName),
                moduleName ?? string.Empty);
            writer.WriteEndObject();
        }
    }
}
