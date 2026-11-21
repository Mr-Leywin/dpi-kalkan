using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Windows.Forms;
using System.Xml;

namespace DPIKalkan
{
    /// <summary>
    /// "Windows ile başlat": Görev Zamanlayıcı ile oturum açılışında en yüksek yetkiyle başlatır
    /// (her açılışta UAC sorusu çıkmaz). Görev, geçici dosya kullanılmadan doğrudan Görev Zamanlayıcı'nın
    /// COM arabirimiyle (Schedule.Service) kaydedilir. Yalnızca korumalı (kurulumlu) klasörde izin verilir.
    /// </summary>
    static class Startup
    {
        const string TaskName = "DPI Kalkan";
        const int TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_INTERACTIVE_TOKEN = 3;

        static object Call(object o, string method, params object[] args) =>
            o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args);

        static object Prop(object o, string name) =>
            o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);

        static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { }
        }

        /// <summary>Görev Zamanlayıcı kök klasörüyle bir işlem yapar.</summary>
        static T WithRoot<T>(Func<object, T> f)
        {
            object svc = null, root = null;
            try
            {
                svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
                Call(svc, "Connect");
                root = Call(svc, "GetFolder", "\\");
                return f(root);
            }
            finally { Release(root); Release(svc); }
        }

        static string Unwrap(Exception ex) => (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;

        /// <summary>Görevin XML tanımı (yoksa null).</summary>
        static string TaskXml()
        {
            try
            {
                return WithRoot(root =>
                {
                    object task = null;
                    try { task = Call(root, "GetTask", TaskName); return Prop(task, "Xml") as string; }
                    catch { return null; }
                    finally { Release(task); }
                });
            }
            catch { return null; }
        }

        public static bool IsEnabled() => TaskXml() != null;

        /// <summary>Görev Zamanlayıcı'ya COM ile bağlanılabiliyor mu (test için).</summary>
        internal static bool Probe(out string error)
        {
            error = "";
            try { return WithRoot(root => Prop(root, "Path") as string == "\\"); }
            catch (Exception ex) { error = Unwrap(ex); return false; }
        }

        /// <summary>Görevin çalıştırdığı exe yolu (görev yoksa null).</summary>
        public static string CommandPath()
        {
            string xml = TaskXml();
            if (xml == null) return null;
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.LoadXml(xml);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                return doc.SelectSingleNode("//t:Exec/t:Command", ns)?.InnerText.Trim().Trim('"');
            }
            catch { return null; }
        }

        public static bool Enable(out string error)
        {
            error = null;
            if (!InstallGuard.IsProtected) { error = InstallGuard.NeedInstall; return false; }
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
      <Command>{SecurityElement.Escape("\"" + exe + "\"")}</Command>
      <Arguments>--tray</Arguments>
      <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
            try
            {
                WithRoot(root =>
                {
                    object task = null;
                    try { task = Call(root, "RegisterTask", TaskName, xml, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null); }
                    finally { Release(task); }
                    return true;
                });
                App.Log("Windows açılışında başlatma etkinleştirildi.", LogLevel.Success);
                return true;
            }
            catch (Exception ex) { error = Unwrap(ex); return false; }
        }

        public static void Disable()
        {
            try
            {
                bool removed = WithRoot(root =>
                {
                    try { Call(root, "DeleteTask", TaskName, 0); return true; }
                    catch { return false; }   // görev yok
                });
                if (removed) App.Log("Windows açılışında başlatma kapatıldı.", LogLevel.Info);
            }
            catch (Exception ex) { App.Log("Açılış görevi kaldırılamadı: " + Unwrap(ex), LogLevel.Warn); }
        }
    }
}
