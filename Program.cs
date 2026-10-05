namespace RBLXHubLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--apply-hosts")
        {
            return HostsFileInstaller.RunPrivilegedWorker(args[1], args[2]);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow());
        return 0;
    }
}
