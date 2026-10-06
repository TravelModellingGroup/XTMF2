namespace XTMF2.RunServer.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        UpdaterArguments options;
        try
        {
            options = UpdaterArguments.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        return DeploymentUpdater.Run(options);
    }
}
