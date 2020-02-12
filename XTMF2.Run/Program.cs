using System;
using System.Collections.Generic;
using System.IO;
using XTMF2.Bus;

namespace XTMF2.Run
{
    public class Program
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
                        Stream toClient = null;
                        try
                        {
                            if (!CreateStreams.CreateNamedPipeClient(args[i], out toClient, ref error))
                            {
                                Console.WriteLine("Error creating run client\r\n" + error);
                                return;
                            }
                            Run(toClient, dllsToLoad);
                        }
                        finally
                        {
                            toClient?.Dispose();
                        }
                        
                        break;
                    default:
                        Console.WriteLine($"Unknown argument '{args[i]}'!");
                        return;
                }
            }
        }

        private static void Run(Stream toClient, List<string> dllsToLoad)
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var config = runtime.SystemConfiguration;
            foreach(var dll in dllsToLoad)
            {
                config.LoadAssembly(dll);
            }
            using var runBus = new RunBus(toClient, true, runtime);
            runBus.ProcessRequests();
        }
    }
}
