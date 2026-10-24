using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("1.2.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.1.0")]

internal static class Program
{
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string Runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaText", "state");
    [STAThread]
    private static void Main(string[] args)
    {
        try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        if (args.Length > 0 && args[0] == "--stop")
        {
            try { using (EventWaitHandle e = EventWaitHandle.OpenExisting("Local\\CodexQuotaTextStop.v1")) e.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            return;
        }
        Directory.CreateDirectory(Runtime);
        if (args.Length > 0 && args[0] == "--probe")
        {
            Native.Target t = Native.FindTarget();
            File.WriteAllText(Path.Combine(Runtime, "probe.json"), new JavaScriptSerializer().Serialize(t));
            return;
        }
        if (args.Length == 2 && args[0] == "--render")
        {
            QuotaLabel label = QuotaLabel.Read();
            using (Bitmap bitmap = new Bitmap(55, 80))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(32,32,32));
                QuotaForm.DrawLabel(g, new Rectangle(0,0,55,80), 1.25f, label);
                bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            }
            return;
        }
        bool owns;
        using (Mutex mutex = new Mutex(true,"Local\\CodexQuotaText.v1",out owns))
        {
            if (!owns) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.AutoReset,"Local\\CodexQuotaTextStop.v1"))
            using (QuotaForm form = new QuotaForm(stop)) Application.Run(form);
        }
    }
}

internal sealed class QuotaLabel
{
    public string Percent = "—";
    public string Date = "";
    public string Time = "";
    public string Note = "读取中";
    public string UpdatedAt = "";
    public bool Stale;

    public static QuotaLabel Read()
    {
        string node = Environment.GetEnvironmentVariable("CODEX_MCP_NODE_PATH");
        if (String.IsNullOrEmpty(node) || !File.Exists(node)) node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "node", "bin", "node.exe");
        if (!File.Exists(node)) throw new InvalidOperationException("本机 Node.js 未找到");
        ProcessStartInfo info = new ProcessStartInfo(node, "\"" + Path.Combine(Program.Root,"fetch-quota.mjs") + "\"");
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        info.WorkingDirectory = Program.Root;
        string json;
        using (Process p = Process.Start(info))
        {
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            Task<string> error = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(33000)) { try { p.Kill(); } catch { } throw new TimeoutException("额度读取超时"); }
            json = output.Result;
            if (p.ExitCode != 0) throw new InvalidOperationException("额度读取失败");
        }
        Dictionary<string,object> data = (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(json);
        object[] buckets = data["buckets"] as object[];
        if (buckets == null || buckets.Length == 0) throw new InvalidOperationException("额度未提供");
        Dictionary<string,object> bucket = buckets.OfType<Dictionary<string,object>>().FirstOrDefault(b => Convert.ToString(b["id"]) == "codex") ?? (Dictionary<string,object>)buckets[0];
        object[] windows = bucket["windows"] as object[];
        Dictionary<string,object> window = windows == null ? null : windows.OfType<Dictionary<string,object>>().Where(w => w["remainingPercent"] != null).OrderBy(w => Convert.ToDouble(w["remainingPercent"])).FirstOrDefault();
        QuotaLabel result = new QuotaLabel();
        result.UpdatedAt = Convert.ToString(data["fetchedAt"]);
        bool blocked = (data["ordinaryUsageAllowed"] is bool && !(bool)data["ordinaryUsageAllowed"]) || (bucket["blocked"] is bool && (bool)bucket["blocked"]);
        result.Percent = blocked ? "受限" : window == null ? "—" : Convert.ToString(window["remainingPercent"]) + "%";
        result.Note = "重置";
        if (window != null && window["resetsAt"] != null)
        {
            DateTime utc = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(window["resetsAt"]));
            DateTime beijing = TimeZoneInfo.ConvertTimeFromUtc(utc,TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
            result.Date = beijing.ToString("MM/dd"); result.Time = beijing.ToString("HH:mm");
            if (utc < DateTime.UtcNow) result.Note = "待更新";
        }
        else { result.Date = "未提供"; result.Time = ""; }
        return result;
    }
}

internal sealed class QuotaForm : Form
{
    private readonly System.Windows.Forms.Timer timer;
    private readonly EventWaitHandle stop;
    private QuotaLabel label = new QuotaLabel();
    private DateTime nextRead = DateTime.MinValue;
    private DateTime lastState = DateTime.MinValue;
    private bool reading, allowVisible;
    private float scale = 1f;
    private Native.Target target;
    private string lastError = "";
    public QuotaForm(EventWaitHandle stopEvent)
    {
        stop = stopEvent;
        Text = "CodexQuotaText"; Name = "CodexQuotaText";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(32,32,32); TransparencyKey = BackColor;
        DoubleBuffered = true;
        IntPtr unused = Handle;
        timer = new System.Windows.Forms.Timer(); timer.Interval = 300;
        timer.Tick += Tick; timer.Start();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams p = base.CreateParams; p.ExStyle &= ~0x00040000; p.ExStyle |= 0x08000000 | 0x00000020 | 0x00080000 | 0x00000080; return p; }
    }
    protected override void SetVisibleCore(bool value) { base.SetVisibleCore(value && allowVisible); }
    private void Tick(object sender, EventArgs e)
    {
        if (stop.WaitOne(0)) { Close(); return; }
        target = Native.FindTarget();
        bool active = target != null && target.Handle != 0 && Native.GetForegroundWindow().ToInt64() == target.Handle && !Native.IsIconic(new IntPtr(target.Handle));
        timer.Interval = active ? 300 : 1200;
        if (!active || target.Height < 400)
        {
            allowVisible = false; if (Visible) Hide();
            SaveState(false); return;
        }
        scale = Math.Max(1f, target.Dpi / 96f);
        int width = (int)Math.Round(44 * scale), height = (int)Math.Round(64 * scale);
        Rectangle bounds = new Rectangle(target.Left + (int)Math.Round(8*scale), target.Top + target.Height - (int)Math.Round(174*scale), width,height);
        // Remain an independent tool window, so closing ChatGPT cannot destroy this watcher.
        if (Bounds != bounds) Bounds = bounds;
        allowVisible = true; if (!Visible) Show();
        Native.SetWindowPos(Handle,new IntPtr(-1),Left,Top,Width,Height,0x0010 | 0x0040);
        if (!reading && DateTime.UtcNow >= nextRead) RefreshQuota();
        SaveState(true);
    }
    private async void RefreshQuota()
    {
        reading = true; nextRead = DateTime.UtcNow.AddSeconds(60);
        try { QuotaLabel next = await Task.Run(() => QuotaLabel.Read()); if (!IsDisposed) { label=next; lastError=""; Invalidate(); } }
        catch (Exception ex) { if (!IsDisposed) { label.Stale=true; label.Note="待刷新"; lastError=ex.Message; Invalidate(); } }
        finally { reading=false; lastState=DateTime.MinValue; }
    }
    private void SaveState(bool shown)
    {
        if ((DateTime.UtcNow-lastState).TotalSeconds < 3) return;
        lastState=DateTime.UtcNow;
        try
        {
            var state = new {visible=Visible && Native.IsWindowVisible(Handle), percent=label.Percent, resetDate=label.Date, resetTime=label.Time, stale=label.Stale, updatedAt=label.UpdatedAt,
                left=Left,top=Top,width=Width,height=Height, target=target, error=lastError, processId=Process.GetCurrentProcess().Id,
                windowHandle=Handle.ToInt64(), version="1.2.1", noActivate=true, clickThrough=true};
            File.WriteAllText(Path.Combine(Program.Runtime,"status.json"),new JavaScriptSerializer().Serialize(state));
            if (shown)
                using(Bitmap bitmap=new Bitmap(Math.Max(1,Width),Math.Max(1,Height)))
                using(Graphics g=Graphics.FromImage(bitmap)) { g.Clear(BackColor); DrawLabel(g,new Rectangle(Point.Empty,bitmap.Size),scale,label); bitmap.Save(Path.Combine(Program.Runtime,"render.png"),System.Drawing.Imaging.ImageFormat.Png); }
        } catch { }
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); DrawLabel(e.Graphics,ClientRectangle,scale,label); }
    internal static void DrawLabel(Graphics g, Rectangle area, float scale, QuotaLabel data)
    {
        // GDI+ preserves the alpha channel of the buffered, color-keyed layered window.
        // GDI TextRenderer can clear that alpha channel and make the live text disappear.
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        using(StringFormat format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap })
        using(Font main=new Font("Segoe UI",(float)Math.Round(16*scale),FontStyle.Bold,GraphicsUnit.Pixel))
        using(Font small=new Font("Segoe UI",(float)Math.Round(12*scale),FontStyle.Regular,GraphicsUnit.Pixel))
        using(Font caption=new Font("Microsoft YaHei UI",(float)Math.Round(12*scale),FontStyle.Bold,GraphicsUnit.Pixel))
        {
            using(Brush bright=new SolidBrush(data.Stale?Color.FromArgb(180,180,180):Color.FromArgb(245,245,245)))
            using(Brush muted=new SolidBrush(Color.FromArgb(205,205,205)))
            {
                g.DrawString(data.Percent,main,bright,LineRect(area,scale,0,20),format);
                g.DrawString(data.Note,caption,bright,LineRect(area,scale,20,16),format);
                g.DrawString(data.Date,small,muted,LineRect(area,scale,36,14),format);
                g.DrawString(data.Time,small,muted,LineRect(area,scale,50,14),format);
            }
        }
    }
    private static Rectangle LineRect(Rectangle area,float scale,int top,int height)
    {
        int y=(int)Math.Round(top*scale);
        int bottom=Math.Min(area.Height,(int)Math.Round((top+height)*scale));
        return new Rectangle(0,y,area.Width,Math.Max(1,bottom-y));
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg==0x0084) {m.Result=new IntPtr(-1);return;}
        if (m.Msg==0x0021) {m.Result=new IntPtr(3);return;}
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing) { if(disposing)timer.Dispose(); base.Dispose(disposing); }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; }
    internal sealed class Target { public long Handle; public int ProcessId,Left,Top,Width,Height,Dpi; }
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd,out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd,ref POINT point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    internal static void SetOwner(IntPtr hwnd,IntPtr target) {SetWindowLongPtr(hwnd,-8,target);}
    internal static Target FindTarget()
    {
        IntPtr foreground=GetForegroundWindow();
        Target fallback=null;
        foreach(string name in new[]{"ChatGPT","Codex"})
        foreach(Process p in Process.GetProcessesByName(name))
        using(p)
        {
            try
            {
                IntPtr hwnd=p.MainWindowHandle;
                if(hwnd==IntPtr.Zero || !IsWindowVisible(hwnd))continue;
                RECT r; POINT origin=new POINT();
                if(!GetClientRect(hwnd,out r) || !ClientToScreen(hwnd,ref origin) || r.Right-r.Left<400 || r.Bottom-r.Top<300)continue;
                int dpi=(int)GetDpiForWindow(hwnd); if(dpi<=0)dpi=96;
                Target target=new Target {Handle=hwnd.ToInt64(),ProcessId=p.Id,Left=origin.X,Top=origin.Y,Width=r.Right-r.Left,Height=r.Bottom-r.Top,Dpi=dpi};
                if(hwnd==foreground)return target;
                if(fallback==null)fallback=target;
            } catch { }
        }
        return fallback;
    }
}
