using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Web;
using Microsoft.Win32;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace KonradSikorski.Tools.RdpProtocolHandler
{
    class Program
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private const string REGISTRY_KEY_NAME = "RDP";

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
            ConfigureNLog();
            Log.Info($"{string.Join( " | ", args)}");

            if (args.Length == 0) Install();
            else
            {
                var parameter = args[0];
                switch (parameter.ToLower())
                {
                    case "/uninstall": Uninstall(); break;
                    case "/install": Install(false); break;
                    case "/log": OpenLogFile(); break;
                    case "/help":
                    case "/?":
                        Help();
                        break;
                    default:
                        Rdp(parameter);
                        break;
                }
            }

            ConsoleWrapper.WaitForClose();
        }

        private static void ConfigureNLog()
        {
            if (LogManager.Configuration != null) return;

            var logConfiguration = new LoggingConfiguration();
            var fileTarget = new FileTarget("file")
            {
                Layout = "${longdate} ${uppercase:${level}} ${message}",
                FileName = Path.Combine(Path.GetTempPath(), @"rdppotocolhandler-logs\${shortdate}.log")
            };

            var rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
            logConfiguration.LoggingRules.Add(rule);

            LogManager.Configuration = logConfiguration;
        }

        private static void OpenLogFile()
        {
            var fileTarget = LogManager.Configuration.AllTargets.OfType<FileTarget>().FirstOrDefault();

            if (fileTarget == null) return;

            var logEventInfo = new LogEventInfo { TimeStamp = DateTime.Now };
            string fileName = fileTarget.FileName.Render(logEventInfo);
            if (File.Exists(fileName))
                Process.Start(fileName);
        }

        private static void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ConsoleWrapper.Alloc();
            Log.Error(e.ExceptionObject);
            ConsoleWrapper.WriteLine("Error occured. Please check the log file for details");
            Environment.Exit(1);
        }

        private static void Help()
        {
            ConsoleWrapper.Alloc();
            ConsoleWrapper.WriteLine("For help go to: https://github.com/konradsikorski/RdpProtocolHandler");
        }

        private static void Rdp(string parameter)
        {
            Log.Debug("Start RDP: " + parameter);

            var encodedUri = parameter.Substring("rdp://".Length).TrimEnd('/');
            string uri = HttpUtility.UrlDecode(encodedUri);
            var splitRdpParameters = uri.Split('&');

            var rdpParameters = new List<string>();

            var addressLookup = "full address=s:";
            var fullAddress=splitRdpParameters.Where(rparam => rparam.StartsWith(addressLookup)).FirstOrDefault().Substring(addressLookup.Length);
            if (fullAddress.Length > 0) {
                rdpParameters.Add($"/v:{fullAddress}");
            }

            var userNameLookup = "username=s:";
            var userName = splitRdpParameters.Where(rparam => rparam.StartsWith(userNameLookup)).FirstOrDefault().Substring(userNameLookup.Length);
            if (userNameLookup.Length > 0 && fullAddress.Length > 0)
            {
                RegistryKey serverKey = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\Microsoft\\Terminal Server Client\\Servers\\{fullAddress}");
                serverKey.SetValue("UsernameHint", userName);
            }

            var rdpParametersChain = string.Join(" ", rdpParameters);
            Log.Debug("rdpParametersChain: " + rdpParametersChain);

            Process.Start($"{Environment.GetEnvironmentVariable("systemroot")}\\system32\\mstsc.exe", rdpParametersChain);
            Log.Debug("End RDP");
        }

        private static void Uninstall()
        {
            ConsoleWrapper.Alloc();
            if (!RequireAdministratorPrivilages()) return;

            Registry.ClassesRoot.DeleteSubKeyTree(REGISTRY_KEY_NAME, false);
            ConsoleWrapper.WriteLine("RDP Protocol Handler uninstalled.");
            Log.Info("RDP Protocol Handler uninstalled."); 
        }

        private static void Install(bool prompt = true)
        {
            ConsoleWrapper.Alloc();

            if (!RequireAdministratorPrivilages()) return;

            //if (prompt)
            //{
            //    ConsoleWrapper.Write("Do you want to install RDP Protocol handler? (for details use /?) [Y]es [N]o:");
            //    var result = ConsoleWrapper.ReadLine();
            //    if (result?.ToLower() != "y") return;
            //}

            Uninstall();
            
            //-- get assembly info
            var assembly = Assembly.GetExecutingAssembly();
            var handlerLocation = assembly.Location;

            //-- create registy structure
            var rootKey = Registry.ClassesRoot.CreateSubKey(REGISTRY_KEY_NAME);
            var defaultIconKey = rootKey?.CreateSubKey("DefaultIcon");
            var commandKey = rootKey?.CreateSubKey("shell")?.CreateSubKey("open")?.CreateSubKey("command");

            rootKey?.SetValue("", "rdp:Remote Desktop Protocol" );
            rootKey?.SetValue("URL Protocol", "");
            defaultIconKey?.SetValue("", @"C:\Windows\System32\mstsc.exe");
            commandKey?.SetValue("", $@"""{handlerLocation}"" ""%1""");

            //--
            Log.Info("RDP Protocol Handler installed");
            ConsoleWrapper.WriteLine("RDP Protocol Handler installed");
            ConsoleWrapper.WriteLine($"WARNING: Do not move this '{assembly.FullName}' to other location, otherwise handler will not work. If you change the location run installation process again.");
        }

        private static bool RequireAdministratorPrivilages()
        {
            var isAdmin = IsUserAdministrator();

            if (!isAdmin)
            {
                var oldColor = ConsoleWrapper.ForegroundColor;
                ConsoleWrapper.ForegroundColor = ConsoleColor.Red;
                ConsoleWrapper.WriteLine("You must be system administrator");
                ConsoleWrapper.ForegroundColor = oldColor;
                Log.Error("You must be system administrator");
            }

            return isAdmin;
        }

        private static bool IsUserAdministrator()
        {
            using (WindowsIdentity user = WindowsIdentity.GetCurrent())
            {
                try
                {
                    //get the currently logged in user
                    WindowsPrincipal principal = new WindowsPrincipal(user);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }
    }
}
