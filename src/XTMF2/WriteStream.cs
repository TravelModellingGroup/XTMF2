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
using System.IO;

namespace XTMF2
{
    public sealed class WriteStream : Stream
    {
        private Stream BaseStream;

        internal WriteStream(Stream baseStream)
        {
            BaseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            if(!baseStream.CanWrite)
            {
                throw new InvalidDataException("Unable to create a WriteStream from a stream that can not read!");
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => BaseStream.CanSeek;

        public override bool CanWrite => true;

        public override long Length => BaseStream.Length;

        public override long Position { get => BaseStream.Position; set => BaseStream.Position = value; }

        public override void Flush()
        {
            BaseStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException("Unable to read from a WriteStream");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return BaseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            BaseStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            BaseStream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            BaseStream.Dispose();
        }
    }
}
