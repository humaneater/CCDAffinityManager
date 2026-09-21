using CcdAffinityManager.Services;

namespace CcdAffinityManager;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length >= 2 &&
            string.Equals(args[0], "--diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            var topology = CpuTopologyService.Detect();
            File.WriteAllText(args[1], topology.ToDiagnosticText());
            return;
        }

        Application.Run(new MainForm());
    }
}
