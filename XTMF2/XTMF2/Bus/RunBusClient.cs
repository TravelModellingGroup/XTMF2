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

namespace XTMF2.Bus
{
    /// <summary>
    /// Provides communication to the XTMF Interface
    /// </summary>
    public sealed class RunBusClient : IDisposable
    {
        private Stream ClientHost;
        private bool Owner;
        private volatile bool Exit = false;

        public RunBusClient(Stream serverStream, bool owner)
        {
            ClientHost = serverStream;
            Owner = owner;
        }

        private void Dispose(bool managed)
        {
            if(managed)
            {
                GC.SuppressFinalize(this);
            }
            if(Owner)
            {
                ClientHost.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        ~RunBusClient()
        {
            Dispose(false);
        }

        private enum In
        {
            Heartbeat = 0,
            RunModelSystem = 1,
            CancelModelRun = 2,
            KillModelRun = 3
        }

        private enum Out
        {
            Heartbeat = 0,
            ClientReady = 1,
            ClientExiting = 2,
            ClientFinishedModelSystem = 3,
            ClientErrorWhenRunningModelSystem = 4,
            ProgressUpdate = 5,
            SendModelSystemResult = 6
        }

        /// <summary>
        /// Consumes the current thread to answer requests from the host
        /// </summary>
        public void ProcessRequests()
        {
            try
            {
                BinaryWriter writer = new BinaryWriter(ClientHost, Encoding.Unicode, true);
                while (!Exit)
                {
                    writer.Write((int)Out.ClientReady);
                    Exit = true;
                    Interlocked.MemoryBarrier();
                }
            }
            catch(IOException)
            {

            }
        }
    }
}
