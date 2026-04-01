/*
    Copyright 2026 University of Toronto

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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2;

public sealed class RunStatusStream : WriteStream
{
    private XTMFRuntime _runtime; 
    
    internal RunStatusStream(XTMFRuntime runtime)
    {
        _runtime = runtime;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    private long _length = 0;

    public override long Length => _length;

    public override long Position { get => _length; set => throw new InvalidOperationException("Unable to set the position of a RunStatusStream."); }

    private readonly Lock _sync = new();

    public override void Flush()
    {
        // Do nothing, we're always flushed.
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException("Unable to read from a RunStatusStream.");
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        throw new InvalidOperationException("Unable to read from a RunStatusStream.");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new InvalidOperationException("Unable to seek in a RunStatusStream.");
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        lock (_sync)
        {
            var task = Task.Run(() =>
            {
                _runtime.RunBus?.SendStatusMessage(Encoding.UTF8.GetString(buffer, offset, count));
            });
            return TaskToAsyncResult.Begin(task, null, null);
        }
    }

    public override void SetLength(long value)
    {
        throw new InvalidOperationException("Unable to set the length of a RunStatusStream");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var message = Encoding.UTF8.GetString(buffer);
        lock (_sync)
        {
            _runtime.RunBus?.SendStatusMessage(message);
        }
    }

    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;    
    }

    public override void Close()
    {
        Dispose(true);
    }

    protected override void Dispose(bool disposing)
    {
        
    }

}

