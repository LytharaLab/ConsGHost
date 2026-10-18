namespace ConsGHost.Host;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length == 2 && args[0] == "--ui-check")
        {
            form.Shown += async (_, _) =>
            {
                try { await form.CheckAndRenderAsync(Path.GetFullPath(args[1])); }
                catch (Exception error)
                {
                    Environment.ExitCode = 1;
                    Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "failure.txt"), error.ToString());
                }
                finally { form.Close(); }
            };
        }
        Application.Run(form);
    }
}
