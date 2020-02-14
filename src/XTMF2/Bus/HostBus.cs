/*
    Copyright 2017 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Editing;

namespace XTMF2.Bus
{
    /// <summary>
    /// Provides communication with the client process
    /// </summary>
    public sealed class HostBus : IDisposable
    {
        private readonly Stream _HostStream;
        private readonly bool _Owner;
        private volatile bool _Exit = false;
        private volatile bool _Exited = false;

        /// <summary>
        /// Create a host on a given stream.
        /// </summary>
        /// <param name="hostStream">The stream to host.</param>
        /// <param name="streamOwner">Should this bus assume ownership over the stream?</param>
        public HostBus(Stream hostStream, bool streamOwner)
        {
            _Owner = streamOwner;
            _HostStream = hostStream ?? throw new ArgumentNullException(nameof(hostStream));
            StartListenner();
        }

        ~HostBus()
        {
            Dispose(false);
        }

        private void Dispose(bool managed)
        {
            if (managed)
            {
                GC.SuppressFinalize(this);
            }
            _Exit = true;
            while (!_Exited)
            {
                Interlocked.MemoryBarrier();
                if (!_Exited)
                {
                    Task.WaitAll(Task.Delay(50));
                }
                Interlocked.MemoryBarrier();
            }
            if (_Owner)
            {
                _HostStream.Dispose();
            }
        }

        /// <summary>
        /// Disconnect from the client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        private enum In
        {
            Heartbeat = 0,
            ClientReady = 1,
            ClientExiting = 2,
            ClientFinishedModelSystem = 3,
            ClientErrorWhenRunningModelSystem = 4,
            ClientErrorValidatingModelSystem = 5,
            ProgressUpdate = 6,
            SendModelSystemResult = 7,
            ClientReportedStatus = 8
        }

        /// <summary>
        /// This event is signalled when a client finishes running a model system.
        /// The parameter is the name of the completed model system.
        /// </summary>
        public event EventHandler<string> ClientFinishedModelSystem;

        /// <summary>
        /// Used to report that a model system has had a run error.
        /// </summary>
        /// <param name="sender">The object reporting the event.</param>
        /// <param name="runID">The ID of the run that failed.</param>
        /// <param name="errorMessage">The error message from the error.</param>
        /// <param name="stack">The stack trace at the point of the error.</param>
        public delegate void RunError(object sender, string runID, string errorMessage, string stack);

        /// <summary>
        /// Used to trigger a status update from a model system.
        /// </summary>
        /// <param name="sender">The object reporting the event.</param>
        /// <param name="runID">The ID of the run that is sending the update.</param>
        /// <param name="status">The status message from the model system.</param>
        public delegate void ClientStatusUpdate(object sender, string runID, string status);

        /// <summary>
        /// This event is signalled when a client runs into an error.
        /// </summary>
        public event RunError ClientErrorWhenRunningModelSystem;

        /// <summary>
        /// This event is triggered when the client has sent an update for the run's status message.
        /// </summary>
        public event ClientStatusUpdate ClientReportedStatus;

        private static void IgnoreWarnings(Action toRun)
        {
            /*
* We are disabling the warning to catch a specific error since we are going to
* be called into unknown code.
*/
#pragma warning disable CA1031
            try
            {
                toRun();
            }
            catch { }
#pragma warning restore CA1031
        }

        /// <summary>
        /// Invoke this to start listening on a separate thread.
        /// </summary>
        public void StartListenner()
        {
            Task.Factory.StartNew((token) =>
            {
                try
                {
                    using var reader = new BinaryReader(_HostStream, Encoding.UTF8, true);
                    while (!_Exit)
                    {
                        var command = (In)reader.ReadInt32();
                        switch (command)
                        {
                            case In.Heartbeat:
                                // Read in the ID of the run that issued the Heartbeat.
                                reader.ReadString(); 
                                break;
                            case In.ClientReady:
                                break;
                            case In.ClientExiting:
                                _Exit = true;
                                break;
                            case In.ClientErrorValidatingModelSystem:
                                IgnoreWarnings(() => ClientErrorWhenRunningModelSystem?.Invoke(this, reader.ReadString(), reader.ReadString(), String.Empty));
                                break;
                            case In.ClientFinishedModelSystem:
                                IgnoreWarnings(() => ClientFinishedModelSystem?.Invoke(this, reader.ReadString()));
                                break;
                            case In.ClientErrorWhenRunningModelSystem:
                                IgnoreWarnings(() => ClientErrorWhenRunningModelSystem?.Invoke(this, reader.ReadString(), reader.ReadString(), reader.ReadString()));
                                break;
                            case In.ClientReportedStatus:
                                IgnoreWarnings(() => ClientReportedStatus?.Invoke(this, reader.ReadString(), reader.ReadString()));
                                break;
                            default:
                                throw new Exception($"Unsupported command: {Enum.GetName(typeof(In), command)}");
                        }
                        System.Threading.Interlocked.MemoryBarrier();
                    }
                }
                finally
                {
                    _Exited = true;
                }
            }, TaskCreationOptions.LongRunning);
        }

        private enum Out
        {
            Heartbeat = 0,
            RunModelSystem = 1,
            CancelModelRun = 2,
            KillModelRun = 3
        }

        /// <summary>
        /// Take this lock before writing anything to the out stream.
        /// </summary>
        private readonly object _outLock = new object();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="modelSystem">The model system to execute</param>
        /// <param name="cwd">The directory to run in.</param>
        /// <param name="startToExecute">The starting point for the model system run</param>
        /// <param name="id">The ID given to this model run.</param>
        /// <param name="error">An error message if there is an issue creating the model system.</param>
        /// <returns>True if the model system was sent</returns>
        public bool RunModelSystem(ModelSystemSession modelSystem, string cwd, string startToExecute, out string id, ref string error)
        {
            id = null;
            lock (_outLock)
            {
                try
                {
                    using var memStream = new MemoryStream();
                    using var write = new BinaryWriter(memStream, Encoding.UTF8, true);
                    if (!modelSystem.Save(ref error, memStream))
                    {
                        return false;
                    }
                    id = Guid.NewGuid().ToString();
                    // int64
                    using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                    writer.Write((int)Out.RunModelSystem);
                    writer.Write(id);
                    writer.Write(cwd);
                    writer.Write(startToExecute);
                    writer.Write(memStream.Length);
                    memStream.WriteTo(_HostStream);
                    return true;
                }
                catch (IOException e)
                {
                    error = e.Message;
                    return false;
                }
            }
        }
    }
}
