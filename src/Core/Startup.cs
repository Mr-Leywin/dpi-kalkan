using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace DPIKalkan
{
    /// <summary>
    /// "Windows ile başlat": Görev Zamanlayıcı ile oturum açılışında en yüksek yetkiyle başlatır
    /// (her açılışta UAC sorusu çıkmaz). Pil ile çalışırken de başlar.
    /// </summary>
    static class Startup
    {
        const string TaskName = "DPI Kalkan";

        static (int Code, string Output) Schtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return (p.ExitCode, o.Trim());
                }
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }

        public static bool IsEnabled() => Schtasks("/Query /TN \"" + TaskName + "\"").Code == 0;

        public static bool Enable(out string error)
        {
            error = null;
            string user;
            using (var id = WindowsIdentity.GetCurrent()) user = id.Name;
            string exe = Application.ExecutablePath;
            string xml =
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>DPI Kalkan'ı oturum açılışında sistem tepsisinde başlatır.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{SecurityElement.Escape(user)}</UserId>
      <Delay>PT5S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{SecurityElement.Escape(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(exe)}</Command>
      <Arguments>--tray</Arguments>
      <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "dpikalkan_task.xml");
            try
            {
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                var r = Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F");
                if (r.Code != 0) { error = r.Output; return false; }
                App.Log("Windows açılışında başlatma etkinleştirildi.", LogLevel.Success);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static void Disable()
        {
            var r = Schtasks("/Delete /TN \"" + TaskName + "\" /F");
            if (r.Code == 0) App.Log("Windows açılışında başlatma kapatıldı.", LogLevel.Info);
        }
    }
}
