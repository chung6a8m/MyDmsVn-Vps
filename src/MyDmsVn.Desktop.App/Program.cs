using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using MyDmsVn.Desktop.WinForms;

namespace MyDmsVn.Desktop.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            var smokeTest = IsSmokeTest(args);
            HttpDesktopOptions options;
            HttpDesktopOperationSettings operations;
            try
            {
                options = HttpDesktopOptions.FromEnvironment();
                operations = HttpDesktopOperationSettings.FromEnvironment();
            }
            catch (InvalidOperationException error)
            {
                return ReportStartupFailure(error.Message,
                    "Configure the authorized HTTPS API origin and operation mode.", smokeTest);
            }

            var uiContext = SynchronizationContext.Current ??
                new System.Windows.Forms.WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(uiContext);
            ServiceProvider provider;
            try
            {
                provider = CreateServiceProvider(uiContext, options, operations);
                // Eagerly inspect the owner-protected file BEFORE any login, UI, or write.
                // Corrupted/unauthorized records fail closed rather than silently resetting.
                provider.GetRequiredService<IHttpPendingWriteStore>().Load();
            }
            catch (Exception error) when (error is InvalidOperationException ||
                error is System.IO.IOException || error is UnauthorizedAccessException)
            {
                return ReportStartupFailure("Desktop.PendingRecoveryUnavailable",
                    "Protected pending-write state is unavailable. No writes are allowed; contact the operator.",
                    smokeTest);
            }
            using (provider)
            {
                return DesktopHostRunner.Run(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    provider.GetRequiredService<IDesktopSession>(),
                    smokeTest);
            }
        }

        private static bool IsSmokeTest(string[] args)
        {
            return Array.Exists(args ?? Array.Empty<string>(),
                argument => string.Equals(argument, "--smoke-test", StringComparison.Ordinal));
        }

        private static ServiceProvider CreateServiceProvider(
            SynchronizationContext uiContext,
            HttpDesktopOptions options,
            HttpDesktopOperationSettings operations)
        {
            var services = new ServiceCollection();
            services.AddHttpDesktopAdapter(options, operations);
            services.AddMyDmsVnDesktopWinForms(uiContext);
            return services.BuildServiceProvider();
        }

        private static int ReportStartupFailure(
            string code, string safeMessage, bool smokeTest)
        {
            var message = code + Environment.NewLine + safeMessage;
            if (smokeTest)
                Console.Error.WriteLine(message);
            else
                System.Windows.Forms.MessageBox.Show(message, "MyDmsVn",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            return 2;
        }
    }
}
