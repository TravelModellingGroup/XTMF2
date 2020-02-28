/*
    Copyright 2017-2020 University of Toronto

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
using System.IO;
using System.Threading.Tasks;

namespace XTMF2
{
    public sealed class ReadStream : Stream
    {
        private readonly Stream _baseStream;

        internal ReadStream(Stream baseStream)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            if (!baseStream.CanRead)
            {
                throw new InvalidDataException("Unable to create a ReadStream from a stream that can not read!");
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => _baseStream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _baseStream.Length;

        public override long Position { get => _baseStream.Position; set => _baseStream.Position = value; }

        private object _sync = new object();

        public override void Flush()
        {
            lock (_sync)
            {
                _baseStream.Flush();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                return _baseStream.Read(buffer, offset, count);
            }
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            lock (_sync)
            {
                return _baseStream.BeginRead(buffer, offset, count, callback, state);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_sync)
            {
                return _baseStream.Seek(offset, origin);
            }
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new InvalidOperationException("Unable to write to a ReadStream.");
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException("Unable to set the length of a ReadStream");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException("Unable to write to a ReadStream");
        }

        public override ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                var ret = _baseStream.DisposeAsync();
                Dispose(true);
                return ret;
            }
        }

        public override void Close()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            lock (_sync)
            {
                try
                {
                    _baseStream.Close();
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }
        }
    }
}
