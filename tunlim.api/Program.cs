using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;


namespace tunlim.api
{
    class Program
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static void LoadLogConfig()
        {
            var repo = log4net.LogManager.CreateRepository(Assembly.GetEntryAssembly(), typeof(log4net.Repository.Hierarchy.Hierarchy));
            log4net.GlobalContext.Properties["pid"] = Process.GetCurrentProcess().Id;

            log4net.Config.XmlConfigurator.ConfigureAndWatch(repo, new FileInfo("log4net.config"));
        }

        static void Main(string[] args)
        {
            LoadLogConfig();

            log.Debug("Main");

            var api = new WebAPI();
            api.Listen();

            Console.ReadLine();
        }
    }
}
