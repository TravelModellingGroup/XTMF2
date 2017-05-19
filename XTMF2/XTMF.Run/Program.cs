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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus;
using XTMF2.Run;

namespace XTMF.Run
{
    class Program
    {
        static void Main(string[] args)
        {
            if(args.Length == 0)
            {
                Console.WriteLine("Usage: XTMF.Run [-remote SERVER_ADDRESS] [-namedPipe PIPE_NAME]");
                return;
            }
            string error = null;
            for(int i = 0; i < args.Length; i++)
            {
                switch(args[i].ToLowerInvariant())
                {
                    case "-remote":
                        Console.WriteLine("Remote connections are not supported yet.");
                        return;
                    case "-namedpipe":
                        if(args.Length == ++i)
                        {
                            Console.WriteLine("Expected a pipe name after getting a -namedPipe instruction!");
                            return;
                        }
                        if(!CreateStreams.CreateNamedPipeClient(args[i], out var serverStream, ref error))
                        {
                            Console.WriteLine("Error creating run client\r\n" + error);
                            return;
                        }
                        RunClient(serverStream);
                        break;
                    default:
                        Console.WriteLine($"Unknown argument '{args[i]}'!");
                        return;
                }
            }
        }

        private static void RunClient(Stream serverStream)
        {
            using (var clientBus = new RunBusClient(serverStream, true))
            {
                clientBus.ProcessRequests();
            }
        }
    }
}