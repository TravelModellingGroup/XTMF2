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
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides functionality for synchronizing the writing of events to a log and providing time stamps.")]
    public sealed class Log : BaseAction<string>, IDisposable
    {
        [SubModule(Required = true, Name = "LogStream", Description = "The stream to save the log to.", Index = 0)]
        public IFunction<WriteStream>? LogStream;

        private readonly object _writeLock = new object();

        private StreamWriter? _writer;

        public override void Invoke(string message)
        {
            lock (_writeLock)
            {
                if(_writer is null)
                {
                    if (LogStream?.Invoke() is WriteStream writeStream)
                    {
                        _writer = new StreamWriter(writeStream, Encoding.Unicode, 0x4000, false);
                    }
                    else
                    {
                        throw new XTMFRuntimeException(this, "Unable to create a write stream to store the log into!");
                    }
                }
                // don't block while writing
                _writer.WriteLineAsync(TimeStampMessage(message));
            }
        }

        private static string TimeStampMessage(string message)
        {
            var now = DateTime.Now;
            return $"[{now.Hour}:{now.Minute}:{now.Second}] {message}";
        }

        private void Dispose(bool managed)
        {
            if(managed)
            {
                GC.SuppressFinalize(this);
            }
            _writer?.Dispose();
            _writer = null;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        ~Log()
        {
            Dispose(false);
        }
    }
}
