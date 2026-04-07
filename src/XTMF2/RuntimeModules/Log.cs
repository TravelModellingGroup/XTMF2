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
using System.Text.Unicode;
using System.Threading;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides functionality for synchronizing the writing of events to a log and providing time stamps.")]
    public sealed class Log : BaseAction<string>, IFunction<Log>, IDisposable
    {
        [SubModule(Required = true, Name = "LogStream", Description = "The stream to save the log to.", Index = 0)]
        public IFunction<WriteStream>? LogStream;

        private readonly Lock _writeLock = new();

        private StreamWriter? _writer;

        private bool _alwaysFlush = false;

        public override void Invoke(string message)
        {
            lock (_writeLock)
            {
                if(_writer is null)
                {
                    if (LogStream?.Invoke() is WriteStream writeStream)
                    {
                        // Check to see if we need to always flush the stream.
                        _alwaysFlush = writeStream is RunStatusStream;
                        var encoding = _alwaysFlush ? new UTF8Encoding(false, true) : Encoding.UTF8;
                        _writer = new StreamWriter(writeStream, encoding, 0x4000, false);
                    }
                    else
                    {
                        throw new XTMFRuntimeException(this, "Unable to create a write stream to store the log into!");
                    }
                }
                // don't block while writing
                _writer.Write(TimeStampMessage(message));
                if (_alwaysFlush)
                {
                    _writer.Flush();
                }
            }
        }

        Log IFunction<Log>.Invoke()
        {
            return this;
        }

        private static string TimeStampMessage(string message)
        {
            var now = DateTime.Now;
            return $"[{now.Hour:D2}:{now.Minute:D2}:{now.Second:D2}] {message}";
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
