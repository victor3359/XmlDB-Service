using System;
using System.IO;
using Topshelf;

namespace XmlDB_Service
{
    class Program
    {
        static void Main(string[] args)
        {
            var exitCode = HostFactory.Run(x =>
            {
                Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
                x.Service<TransferService>(s =>
                {
                    s.ConstructUsing(TransferService => new TransferService());
                    s.WhenStarted(TransferService => TransferService.Start());
                    s.WhenStopped(TransferService => TransferService.Stop());
                });

                x.RunAsLocalSystem();

                x.SetServiceName(@"XmlDBService");
                x.SetDisplayName(@"XmlDB-Service");
                x.SetDescription(@"Transfer XML info to Database. The Project is for Tashan by ICPSI.");



                x.EnableServiceRecovery(r =>
                {
                    r.RestartService(TimeSpan.FromSeconds(5));
                    r.RestartService(TimeSpan.FromSeconds(5));
                    r.RestartComputer(TimeSpan.FromSeconds(30), @"Computer is restarting...");
                    r.SetResetPeriod(7);
                    r.OnCrashOnly();
                });
            });
        }
    }
}
