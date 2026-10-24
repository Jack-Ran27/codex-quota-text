using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class Setup
{
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","CodexQuotaText");
    static readonly string[] AppFiles={"QuotaText.exe","quota.mjs","fetch-quota.mjs"};
    static readonly string Shortcuts=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"GPT-Codex桌面版额度显示器");
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
                UpdateShortcuts(true);
                if(Directory.Exists(Destination)&&!Directory.EnumerateFileSystemEntries(Destination).Any())Directory.Delete(Destination);
                Console.WriteLine("已卸载GPT/Codex 桌面版额度显示器并取消自动运行。账号和聊天内容未作修改。");
            }
            else
            {
                Directory.CreateDirectory(Destination);
                foreach(string file in AppFiles)File.Copy(Path.Combine(Root,"app",file),SafePath(file),true);
                File.WriteAllText(SafePath("使用说明.txt"),"GPT/Codex 桌面版额度显示器\r\n\r\n右键点击左下角的额度文字：\r\n- 关闭本次显示：现在关闭，下次登录 Windows 后仍可自动运行。\r\n- 取消自动启动并关闭：关闭并取消本工具的登录启动项。\r\n\r\n也可在 Windows 开始菜单搜索“关闭额度显示器”或“启动额度显示器”。\r\n",new UTF8Encoding(true));
                UpdateShortcuts(false);
                if(!args.Contains("--no-autostart"))
                    using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunKey))key.SetValue("CodexQuotaText","\""+Path.Combine(Destination,"QuotaText.exe")+"\"",RegistryValueKind.String);
                Process.Start(new ProcessStartInfo(Path.Combine(Destination,"QuotaText.exe")){UseShellExecute=false,CreateNoWindow=true});
                Console.WriteLine(args.Contains("--no-autostart")?"GPT/Codex 桌面版额度显示器已启动，原有自动启动设置保持不变。":"GPT/Codex 桌面版额度显示器已安装。打开 GPT 时会显示，下次登录 Windows 后也会自动运行。");
                Console.WriteLine("右键点击额度文字可关闭；开始菜单中也有启动和关闭入口。");
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
    static void UpdateShortcuts(bool remove)
    {
        Type shellType=Type.GetTypeFromProgID("WScript.Shell");
        if(shellType==null)throw new IOException("无法创建开始菜单入口。");
        object shell=Activator.CreateInstance(shellType);
        try
        {
            if(!remove)Directory.CreateDirectory(Shortcuts);
            foreach(string name in new[]{"启动额度显示器","关闭额度显示器"})
            {
                string link=Path.Combine(Shortcuts,name+".lnk");
                if(remove&&!File.Exists(link))continue;
                object shortcut=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
                try
                {
                    Type type=shortcut.GetType();
                    string target=Path.Combine(Destination,"QuotaText.exe"),arguments=name.StartsWith("关闭")?"--stop":"";
                    if(remove)
                    {
                        string actualTarget=Convert.ToString(type.InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null));
                        string actualArguments=Convert.ToString(type.InvokeMember("Arguments",BindingFlags.GetProperty,null,shortcut,null));
                        if(String.Equals(actualTarget,target,StringComparison.OrdinalIgnoreCase)&&actualArguments==arguments)File.Delete(link);
                    }
                    else
                    {
                        type.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{target});
                        type.InvokeMember("Arguments",BindingFlags.SetProperty,null,shortcut,new object[]{arguments});
                        type.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{Destination});
                        type.InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{"GPT/Codex 桌面版额度显示器"});
                        type.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
                    }
                }
                finally{Marshal.FinalReleaseComObject(shortcut);}
            }
            if(remove&&Directory.Exists(Shortcuts)&&!Directory.EnumerateFileSystemEntries(Shortcuts).Any())Directory.Delete(Shortcuts);
        }
        finally{Marshal.FinalReleaseComObject(shell);}
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
