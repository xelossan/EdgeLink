using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace EdgeLink.Services;

internal enum AutostartMode {
    Off,

    /// <summary>Started from the user's Run key.</summary>
    Normal,

    /// <summary>
    /// Started elevated by a scheduled task. Needed for the hook to keep working while a window
    /// running as administrator has the focus, because Windows hides input from lower-privileged hooks there.
    /// </summary>
    Elevated
}

internal static class Autostart {
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "EdgeLink";
    private const string StartArguments = "--tray";

    public static AutostartMode Get()
    {
        if (TaskExists()) return AutostartMode.Elevated;

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) != null ? AutostartMode.Normal : AutostartMode.Off;
    }

    /// <summary>Returns false if the change needed administrator rights and the user declined the prompt.</summary>
    public static bool Set(AutostartMode mode)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");

        if (mode == AutostartMode.Elevated) {
            if (!CreateTask(exe)) return false;
        }
        else if (TaskExists()) {
            if (!RunSchtasks($"/Delete /TN \"{Name}\" /F", elevated: true)) return false;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (mode == AutostartMode.Normal) key.SetValue(Name, $"\"{exe}\" {StartArguments}");
        else key.DeleteValue(Name, throwOnMissingValue: false);

        return true;
    }

    private static bool TaskExists() => RunSchtasks($"/Query /TN \"{Name}\"", elevated: false);

    private static bool CreateTask(string exe)
    {
        string user;
        using (var identity = WindowsIdentity.GetCurrent()) user = identity.Name;

        // registered from XML because the command-line switches cannot turn off the defaults that
        // stop a task on battery power or after three days
        var xml = $"""
                   <?xml version="1.0" encoding="UTF-16"?>
                   <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                     <RegistrationInfo>
                       <Description>Starts EdgeLink with administrator rights at logon.</Description>
                     </RegistrationInfo>
                     <Triggers>
                       <LogonTrigger>
                         <Enabled>true</Enabled>
                         <UserId>{SecurityElement.Escape(user)}</UserId>
                       </LogonTrigger>
                     </Triggers>
                     <Principals>
                       <Principal id="Author">
                         <UserId>{SecurityElement.Escape(user)}</UserId>
                         <LogonType>InteractiveToken</LogonType>
                         <RunLevel>HighestAvailable</RunLevel>
                       </Principal>
                     </Principals>
                     <Settings>
                       <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                       <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                       <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                       <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                       <AllowHardTerminate>true</AllowHardTerminate>
                       <StartWhenAvailable>false</StartWhenAvailable>
                       <Enabled>true</Enabled>
                     </Settings>
                     <Actions Context="Author">
                       <Exec>
                         <Command>{SecurityElement.Escape(exe)}</Command>
                         <Arguments>{StartArguments}</Arguments>
                       </Exec>
                     </Actions>
                   </Task>
                   """;

        var file = Path.Combine(Path.GetTempPath(), $"edgelink-task-{Guid.NewGuid():N}.xml");
        try {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{Name}\" /XML \"{file}\" /F", elevated: true);
        }
        finally {
            File.Delete(file);
        }
    }

    private static bool RunSchtasks(string arguments, bool elevated)
    {
        var info = new ProcessStartInfo("schtasks.exe", arguments) {
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = elevated
        };
        if (elevated) info.Verb = "runas";

        try {
            using var process = Process.Start(info);
            if (process == null) return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception) {
            // the user dismissed the elevation prompt
            return false;
        }
    }
}
