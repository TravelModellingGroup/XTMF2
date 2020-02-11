﻿using System;
using System.Collections.Generic;

namespace XTMF2.Run
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: XTMF.Run [-loadDLL dllPath] [-config CONFIGURATION] [-remote SERVER_ADDRESS] [-namedPipe PIPE_NAME]");
                return;
            }
            List<string> dllsToLoad = new List<string>();
            string error = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-loaddll":
                        if (i + 1 < args.Length)
                        {
                            dllsToLoad.Add(args[++i]);
                        }
                        else
                        {
                            Console.WriteLine("No second argument for a dll to load!");
                        }
                        break;
                    case "-config":
                        Console.WriteLine("Custom configurations are not supported yet.");
                        return;
                    case "-remote":
                        Console.WriteLine("Remote connections are not supported yet.");
                        return;
                    case "-namedpipe":
                        if (args.Length == ++i)
                        {
                            Console.WriteLine("Expected a pipe name after getting a -namedPipe instruction!");
                            return;
                        }
                        /*Stream clientStream = null;
                        try
                        {
                            if (!CreateStreams.CreateNamedPipeClient(args[i], out serverStream, ref error))
                            {
                                Console.WriteLine("Error creating run client\r\n" + error);
                                return;
                            }
                            RunClient(serverStream, dllsToLoad);
                        }
                        finally
                        {
                            serverStream?.Dispose();
                        }
                        */
                        break;
                    default:
                        Console.WriteLine($"Unknown argument '{args[i]}'!");
                        return;
                }
            }
        }
    }
}
