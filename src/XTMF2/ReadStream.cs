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
    public sealed class ReadStream : Stream
    {
        private Stream BaseStream;

        internal ReadStream(Stream baseStream)
        {
            BaseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            if(!baseStream.CanRead)
            {
                throw new InvalidDataException("Unable to create a ReadStream from a stream that can not read!");
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => BaseStream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => BaseStream.Length;

        public override long Position { get => BaseStream.Position; set => BaseStream.Position = value; }

        public override void Flush()
        {
            BaseStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return BaseStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return BaseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException("Unable to set the length of a ReadStream");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException("Unable to write to a ReadStream");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            BaseStream.Dispose();
        }
    }
}
