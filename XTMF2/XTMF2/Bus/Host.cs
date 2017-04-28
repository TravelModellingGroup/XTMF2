﻿/*
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace XTMF2.Bus
{
    internal abstract class Host
    {
        private List<Client> Clients { get; } = new List<Client>(1);

        internal void AddClient(Client client)
        {
            lock(Clients)
            {
                Clients.Add(client);
            }
        }

        /// <summary>
        /// Remove a client from the host
        /// </summary>
        /// <param name="client">The client to remove</param>
        /// <returns>True if it successfully removed the client</returns>
        internal bool RemoveClient(Client client)
        {
            lock(Clients)
            {
                if(Clients.Remove(client))
                {
                    client.Disconnect();
                }
                return false;
            }
        }
    }
}