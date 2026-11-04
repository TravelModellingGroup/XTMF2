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
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace XTMF2.RuntimeModules;

[Module(Name = "Log", DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/Log.html",
Description = "Provides functionality for synchronizing the writing of events to a log and providing time stamps.")]
[AiModuleInstructions("Connect the required LogStream hook to a WriteStream-producing module before using this Log with WriteToLogA or another logging module.")]
public sealed class Log : BaseAction<string>, IFunction<Log>, IDisposable
{
    private const int StatusMessageQueueCapacity = 50;
    private static readonly UTF8Encoding StatusMessageEncoding = new(false, true);

    [SubModule(Required = true, Name = "LogStream", Description = "The stream to save the log to.", Index = 0)]
    public IFunction<WriteStream>? LogStream;

    private readonly Lock _writeLock = new();
    private StreamWriter? _writer;
    private RunStatusStream? _statusStream;
    private Channel<string>? _statusMessages;
    private Task? _statusSender;
    private bool _initialized;
    private bool _disposed;

    public override void Invoke(string message)
    {
        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_initialized)
            {
                if (LogStream?.Invoke() is WriteStream writeStream)
                {
                    if (writeStream is RunStatusStream statusStream)
                    {
                        _statusStream = statusStream;
                        _statusMessages = Channel.CreateBounded<string>(new BoundedChannelOptions(StatusMessageQueueCapacity)
                        {
                            FullMode = BoundedChannelFullMode.Wait,
                            SingleReader = true,
                            SingleWriter = false
                        });
                        _statusSender = Task.Run(() => SendStatusMessagesAsync(_statusMessages, statusStream));
                    }
                    else
                    {
                        _writer = new StreamWriter(writeStream, Encoding.UTF8, 0x4000, false);
                    }
                    _initialized = true;
                }
                else
                {
                    throw new XTMFRuntimeException(this, "Unable to create a write stream to store the log into!");
                }
            }
            var timestampedMessage = TimeStampMessage(message);
            if (_statusMessages is not null)
            {
                _statusMessages.Writer.WriteAsync(timestampedMessage).AsTask().GetAwaiter().GetResult();
            }
            else
            {
                _writer!.Write(timestampedMessage);
            }
        }
    }

    private static async Task SendStatusMessagesAsync(Channel<string> messages, RunStatusStream stream)
    {
        try
        {
            await foreach (var message in messages.Reader.ReadAllAsync())
            {
                var bytes = StatusMessageEncoding.GetBytes(message);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception exception)
        {
            messages.Writer.TryComplete(exception);
            throw;
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
        if (!managed)
        {
            return;
        }

        GC.SuppressFinalize(this);
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _statusMessages?.Writer.TryComplete();
            try
            {
                _statusSender?.GetAwaiter().GetResult();
            }
            finally
            {
                _writer?.Dispose();
                _statusStream?.Dispose();
                _writer = null;
                _statusStream = null;
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }
}
