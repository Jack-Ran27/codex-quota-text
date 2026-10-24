using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

internal static class Setup
{
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","CodexQuotaText");
    static readonly string[] AppFiles={"QuotaText.exe","quota.mjs","fetch-quota.mjs"};
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    static int Main(string[] args)
    {
        Console.OutputEncoding=Encoding.UTF8;
        try
        {
            if(args.Contains("--verify")){VerifyPayload();Console.WriteLine("安装文件校验通过。");return 0;}
            bool remove=args.Contains("--uninstall");
            if(!remove)VerifyPayload();
            StopInstalled();
            if(remove)
            {
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunKey,true))
                {
                    string expected="\""+Path.Combine(Destination,"QuotaText.exe")+"\"";
                    if(key!=null && String.Equals(Convert.ToString(key.GetValue("CodexQuotaText")),expected,StringComparison.OrdinalIgnoreCase))key.DeleteValue("CodexQuotaText",false);
                }
                foreach(string file in AppFiles.Concat(new[]{"使用说明.txt","停止显示.cmd"}))
                {
                    string path=SafePath(file);
                    if(File.Exists(path))File.Delete(path);
                }
                if(Directory.Exists(Destination)&&!Directory.EnumerateFileSystemEntries(Destination).Any())Directory.Delete(Destination);
                Console.WriteLine("已卸载额度小字并取消自动运行。账号和聊天内容未作修改。");
            }
            else
            {
                Directory.CreateDirectory(Destination);
                foreach(string file in AppFiles)File.Copy(Path.Combine(Root,"app",file),SafePath(file),true);
                if(!args.Contains("--no-autostart"))
                    using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunKey))key.SetValue("CodexQuotaText","\""+Path.Combine(Destination,"QuotaText.exe")+"\"",RegistryValueKind.String);
                Process.Start(new ProcessStartInfo(Path.Combine(Destination,"QuotaText.exe")){UseShellExecute=false,CreateNoWindow=true});
                Console.WriteLine(args.Contains("--no-autostart")?"额度小字已启动，原有自动启动设置保持不变。":"额度小字已安装。打开 GPT 时会显示，下次登录 Windows 后也会自动运行。");
            }
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine("未完成："+e.Message);return 1;}
    }
    static string SafePath(string file)
    {
        string root=Path.GetFullPath(Destination)+Path.DirectorySeparatorChar;
        string target=Path.GetFullPath(Path.Combine(Destination,file));
        if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(file)!=file)throw new IOException("安装路径无效。");
        return target;
    }
    static void VerifyPayload()
    {
        foreach(string name in AppFiles)if(!File.Exists(Path.Combine(Root,"app",name)))throw new FileNotFoundException("安装文件不完整："+name);
    }
    static void StopInstalled()
    {
        try{using(EventWaitHandle stop=EventWaitHandle.OpenExisting("Local\\CodexQuotaTextStop.v1"))stop.Set();}catch(WaitHandleCannotBeOpenedException){}
        foreach(Process p in Process.GetProcessesByName("QuotaText"))using(p)
        {
            string path;try{path=p.MainModule.FileName;}catch{continue;}
            if(String.Equals(path,Path.Combine(Destination,"QuotaText.exe"),StringComparison.OrdinalIgnoreCase)&&!p.WaitForExit(10000))throw new IOException("组件正在使用中，请先关闭后重试。");
        }
    }
}
