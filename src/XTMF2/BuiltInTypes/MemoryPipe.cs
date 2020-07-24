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
using System.IO;
using System.Text;

namespace XTMF2
{
    /// <summary>
    /// Provides a backing to avoid writing data to file, instead keeping it in memory.
    /// </summary>
    public sealed class MemoryPipe : Stream
    {
        private MemoryStream? _stream;

        /// <summary>
        /// Get a writer to the memory pipe.
        /// This will reset the stream.
        /// </summary>
        /// <param name="caller">The module requesting write access</param>
        /// <returns>A WriteStream to the memory.</returns>
        public WriteStream GetWriteStream(IModule caller)
        {
            try
            {
                if (_stream is object)
                {
                    _stream.SetLength(0);
                }
                else
                {
                    _stream = new MemoryStream();
                }
                return new WriteStream(this);
            }
            catch(IOException e)
            {
                throw new XTMFRuntimeException(caller, null, e);
            }
        }

        /// <summary>
        /// Get a reader to the memory pipe.
        /// A stream needs to have been written to first.
        /// </summary>
        /// <param name="caller">The module requesting read access</param>
        /// <returns>A ReadStream to the memory</returns>
        public ReadStream GetReadStream(IModule caller)
        {
            try
            {
                if (_stream is null)
                {
                    throw new XTMFRuntimeException(caller, "Unable to read from a stream that has not been written to!");
                }
                _stream.Position = 0;
                return new ReadStream(this);
            }
            catch(IOException e)
            {
                throw new XTMFRuntimeException(caller, null, e);
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _stream?.Length ?? 0;

        public override long Position
        {
            get => _stream?.Position ?? 0;
            set
            {
                if (_stream is MemoryStream exists)
                {
                    exists.Position = value;
                }
            }
        }

        public override void Flush()
        {
            _stream?.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream?.Read(buffer, offset, count) ?? 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream?.Seek(offset, origin) ?? 0;
        }

        public override void SetLength(long value)
        {
            _stream?.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream?.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
           // Don't dispose anything
        }

        /// <summary>
        /// Invoke this to reclaim the memory buffer
        /// </summary>
        public void DisposeMemoryStream()
        {
            var local = _stream;
            _stream = null;
            System.Threading.Thread.MemoryBarrier();
            local?.Dispose();
        }

        ~MemoryPipe()
        {
            base.Dispose(true);
        }
    }
}
