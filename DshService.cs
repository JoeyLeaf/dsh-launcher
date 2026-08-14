using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DshLauncher
{
    /// <summary>
    /// dsh 核心服务：环境检测、运行状态、启动 / 停止 / 重启、更新。
    /// 全部通过独立子进程实现，不依赖 PowerShell 脚本。
    /// </summary>
    public static class DshService
    {
        public const string PkgName = "@deepseek-ai/dsh";

        // ---------- 环境解析（系统 + 便携 Node） ----------

        /// <summary>便携 Node.js 目录：%LocalAppData%\DshLauncher\node\node-v*-win-x64（一键安装的产物）。</summary>
        public static string PortableNodeDir()
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshLauncher", "node");
                if (!Directory.Exists(root)) return null;
                string best = null;
                string[] dirs = Directory.GetDirectories(root);
                foreach (string d in dirs)
                {
                    if (File.Exists(Path.Combine(d, "node.exe")))
                    {
                        if (best == null) best = d;
                        else if (string.Compare(Path.GetFileName(d), Path.GetFileName(best), StringComparison.OrdinalIgnoreCase) > 0) best = d;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>版本形校验：避免把 cmd 报错文本（如"不是内部或外部命令"）误当版本号。</summary>
        private static bool LooksLikeVersion(string s, bool nodeStyle)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (nodeStyle) return s.Length > 1 && (s[0] == 'v' || s[0] == 'V');
            return char.IsDigit(s[0]);
        }

        /// <summary>
        /// 解析安装 dsh 用的 npm 命令：能跑通 npm --version 就用系统 npm；
        /// 否则回退便携 Node 自带 npm；都没有返回 null（调用方决定是否安装）。
        /// 不做 PATH 扫描 / where 探测——命令本身即探测。
        /// </summary>
        private static string ResolveNpmForCommand()
        {
            string s = RunCapture("cmd.exe", "/c npm --version", 8000);
            if (LooksLikeVersion(s, false)) return "npm";
            string dir = PortableNodeDir();
            if (dir != null && File.Exists(Path.Combine(dir, "npm.cmd"))) return Path.Combine(dir, "npm.cmd");
            return null;
        }

        /// <summary>一键安装便携 Node.js（最新 LTS，免管理员，不动系统 PATH）。</summary>
        public static bool InstallPortableNode(Action<string> log)
        {
            try
            {
                if (PortableNodeDir() != null)
                {
                    Log(log, "已存在便携 Node.js，无需重复安装。");
                    return true;
                }
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshLauncher", "node");
                Directory.CreateDirectory(root);

                Log(log, "查询 Node.js 最新 LTS 版本…");
                string index = HttpGet("https://nodejs.org/dist/index.json", 30000);
                string ver = ParseLatestLts(index);
                if (ver == null)
                {
                    Log(log, "无法获取 Node.js 版本信息（网络不可用？）。");
                    return false;
                }

                string url = "https://nodejs.org/dist/v" + ver + "/node-v" + ver + "-win-x64.zip";
                string zip = Path.Combine(root, "node-v" + ver + "-win-x64.zip");
                Log(log, "正在下载 Node.js v" + ver + "（约 30MB）：" + url);
                if (!DownloadFile(url, zip, log)) return false;

                Log(log, "正在解压…");
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, root);
                try { File.Delete(zip); } catch { }

                if (PortableNodeDir() == null)
                {
                    Log(log, "解压后未找到 node.exe，安装失败。");
                    return false;
                }
                Log(log, "Node.js v" + ver + " 已安装到：" + PortableNodeDir());
                return true;
            }
            catch (Exception e)
            {
                Log(log, "安装 Node.js 失败：" + e.Message);
                return false;
            }
        }

        private static string ParseLatestLts(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            List<string> versions = new List<string>();
            List<string> lts = new List<string>();
            System.Text.RegularExpressions.Regex rv = new System.Text.RegularExpressions.Regex("\"version\":\"v([0-9]+\\.[0-9]+\\.[0-9]+)\"");
            System.Text.RegularExpressions.Regex rl = new System.Text.RegularExpressions.Regex("\"lts\":(\"[^\"]*\"|false)");
            foreach (System.Text.RegularExpressions.Match m in rv.Matches(json)) versions.Add(m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in rl.Matches(json)) lts.Add(m.Groups[1].Value);
            int n = Math.Min(versions.Count, lts.Count);
            for (int i = 0; i < n; i++)
            {
                if (lts[i] != "false") return versions[i];
            }
            return versions.Count > 0 ? versions[0] : null;
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = timeoutMs;
                req.UserAgent = "DshLauncher/0.1";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch { return null; }
        }

        private static bool DownloadFile(string url, string dest, Action<string> log)
        {
            long total = 0;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 150000;
                req.ReadWriteTimeout = 150000;
                req.UserAgent = "DshLauncher/0.1";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream inS = resp.GetResponseStream())
                using (FileStream outS = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = inS.Read(buf, 0, buf.Length)) > 0)
                    {
                        outS.Write(buf, 0, n);
                        total += n;
                    }
                }
                Log(log, "下载完成（" + (total / 1024 / 1024) + " MB）。");
                return true;
            }
            catch (Exception e)
            {
                Log(log, "下载失败：" + e.Message);
                return false;
            }
        }

        // ---------- 版本检测 ----------

        public static string NpmVersion()
        {
            string s = RunCapture("cmd.exe", "/c npm --version", 15000);
            if (LooksLikeVersion(s, false)) return s;
            string dir = PortableNodeDir();
            if (dir != null) return RunCapture("cmd.exe", "/c \"" + Path.Combine(dir, "npm.cmd") + "\" --version", 15000);
            return null;
        }

        public static string NodeVersion()
        {
            string s = RunCapture("node.exe", "--version", 15000);
            if (LooksLikeVersion(s, true)) return s;
            string dir = PortableNodeDir();
            if (dir != null) return RunCapture(Path.Combine(dir, "node.exe"), "--version", 15000);
            return null;
        }

        /// <summary>已安装 dsh 版本：解析 PATH / npx 缓存 / 全局安装中的 package.json。</summary>
        public static string InstalledDshVersion()
        {
            string dir = ResolvePackageDir();
            if (dir == null) return null;
            string pj = Path.Combine(dir, "package.json");
            if (!File.Exists(pj)) return null;
            try
            {
                string text = File.ReadAllText(pj);
                int i = text.IndexOf("\"version\"", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                i = text.IndexOf('"', i + 9);
                if (i < 0) return null;
                int j = text.IndexOf('"', i + 1);
                if (j < 0) return null;
                return text.Substring(i + 1, j - i - 1).Trim();
            }
            catch { return null; }
        }

        /// <summary>npm 上的最新版本；失败返回 null。</summary>
        public static string LatestDshVersion()
        {
            string s = RunCapture("cmd.exe", "/c npm view " + PkgName + " version", 25000);
            if (LooksLikeVersion(s, false)) return s;
            string dir = PortableNodeDir();
            if (dir != null) return RunCapture("cmd.exe", "/c \"" + Path.Combine(dir, "npm.cmd") + "\" view " + PkgName + " version", 25000);
            return null;
        }

        // ---------- 运行状态 ----------

        /// <summary>三重确认：端口监听 + HTTP 有应答 + 进程像 dsh（node 且/或命令行含 dsh）。</summary>
        public static bool IsRunning(int port, out int pid)
        {
            pid = 0;
            int p = FindListenerPid(port);
            if (p == 0) return false;
            if (!HttpAlive(port)) return false;
            string name = ProcessName(p);
            string cmd = ProcessCommandLine(p);
            bool nameOk = name != null && name.IndexOf("node", StringComparison.OrdinalIgnoreCase) >= 0;
            bool cmdOk = cmd != null && cmd.IndexOf("dsh", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!nameOk && !cmdOk) return false;
            pid = p;
            return true;
        }

        // ---------- 动作 ----------

        /// <summary>启动 dsh web（独立进程，日志落盘），轮询等待端口就绪。</summary>
        public static bool Start(int port, string workDir, string logPath, Action<string> log)
        {
            int pid;
            if (IsRunning(port, out pid))
            {
                Log(log, "dsh 已在运行（PID " + pid + "，端口 " + port + "），无需重复启动。");
                return true;
            }
            string invocation = ResolveInvocation();
            if (invocation == null)
            {
                Log(log, "未找到 dsh 命令。请先点击「更新 dsh」安装，或确认 npm 环境正常。");
                return false;
            }
            string args = "/c " + invocation + " web --port " + port + " > \"" + logPath + "\" 2>&1";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = workDir;
                Process.Start(psi);
            }
            catch (Exception e)
            {
                Log(log, "启动失败：" + e.Message);
                return false;
            }
            Log(log, "已发起启动（端口 " + port + "），等待就绪…");
            bool ok = WaitPort(port, true, 25000, log);
            if (!ok) Log(log, "等待超时。请查看日志文件：\n" + logPath);
            return ok;
        }

        /// <summary>停止 dsh：找到监听进程后 taskkill /T /F，轮询等待端口释放。</summary>
        public static bool Stop(int port, Action<string> log)
        {
            int pid;
            if (!IsRunning(port, out pid))
            {
                Log(log, "dsh 未在运行（端口 " + port + "）。");
                return true;
            }
            Log(log, "停止 dsh（PID " + pid + "）…");
            string r = RunCapture("taskkill.exe", "/PID " + pid + " /T /F", 15000);
            if (!string.IsNullOrEmpty(r)) Log(log, r.Trim());
            bool ok = WaitPort(port, false, 8000, log);
            if (!ok) Log(log, "端口 " + port + " 仍被占用，进程可能未完全退出。");
            return ok;
        }

        /// <summary>更新/安装 dsh：全局安装 latest（自动选用系统或便携 npm），日志实时回显。</summary>
        public static bool Update(string workDir, string logPath, Action<string> log)
        {
            string npm = ResolveNpmForCommand();
            if (npm == null)
            {
                // 验证不了就装：自动安装便携 Node.js 再试
                Log(log, "未找到可用的 npm，自动安装便携 Node.js…");
                if (!InstallPortableNode(log)) return false;
                npm = ResolveNpmForCommand();
                if (npm == null)
                {
                    Log(log, "仍无法获得 npm，安装中止。请检查网络后重试。");
                    return false;
                }
            }
            Log(log, "开始更新 " + PkgName + "（npm install -g @latest）…");
            string args = "/c \"" + npm + "\" install -g " + PkgName + "@latest > \"" + logPath + "\" 2>&1";
            Process proc = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = workDir;
                proc = Process.Start(psi);
            }
            catch (Exception e)
            {
                Log(log, "更新失败：" + e.Message);
                return false;
            }
            DateTime started = DateTime.Now;
            long pos = 0;
            while (!proc.HasExited)
            {
                pos = TailFile(logPath, pos, log);
                Thread.Sleep(300);
                if ((DateTime.Now - started).TotalSeconds > 300)
                {
                    try { proc.Kill(); } catch { }
                    Log(log, "更新超时（5 分钟），已中止。");
                    return false;
                }
            }
            pos = TailFile(logPath, pos, log);
            int code;
            try { code = proc.ExitCode; }
            catch { code = -1; }
            Log(log, code == 0 ? "更新命令执行完成（exit 0）。" : "更新命令返回 exit code " + code + "，请查看上方日志。");
            return code == 0;
        }

        // ---------- 内部工具 ----------

        private static void Log(Action<string> log, string msg)
        {
            if (log != null) log(msg);
        }

        private static long TailFile(string path, long pos, Action<string> log)
        {
            try
            {
                if (!File.Exists(path)) return pos;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length <= pos) return pos;
                    fs.Seek(pos, SeekOrigin.Begin);
                    byte[] buf = new byte[fs.Length - pos];
                    int n = fs.Read(buf, 0, buf.Length);
                    pos = fs.Position;
                    string text = Encoding.UTF8.GetString(buf, 0, n);
                    string[] lines = text.Replace("\r\n", "\n").Split('\n');
                    foreach (string line in lines)
                    {
                        string t = line.TrimEnd('\r');
                        if (t.Length > 0) Log(log, t);
                    }
                }
            }
            catch { }
            return pos;
        }

        private static bool WaitPort(int port, bool wantRunning, int timeoutMs, Action<string> log)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int pid;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool now = IsRunning(port, out pid);
                if (now == wantRunning)
                {
                    if (wantRunning) Log(log, "端口 " + port + " 就绪（PID " + pid + "）。");
                    else Log(log, "端口 " + port + " 已释放。");
                    return true;
                }
                Thread.Sleep(400);
            }
            return false;
        }

        private static int FindListenerPid(int port)
        {
            string netstat = RunCapture("netstat.exe", "-ano -p tcp", 10000);
            if (string.IsNullOrEmpty(netstat)) return 0;
            string needle = ":" + port;
            string[] lines = netstat.Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                if (parts[0] != "TCP") continue;
                if (parts[3] != "LISTENING") continue;
                if (!parts[1].EndsWith(needle, StringComparison.Ordinal)) continue;
                int p;
                if (int.TryParse(parts[4], out p) && p > 0) return p;
            }
            return 0;
        }

        private static bool HttpAlive(int port)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/");
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;
                req.UserAgent = "DshLauncher/1.0";
                try
                {
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        resp.Close();
                        return true;
                    }
                }
                catch (WebException we)
                {
                    return we.Response != null; // 服务器有应答即视为存活
                }
            }
            catch { return false; }
        }

        private static string ProcessName(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName; }
            catch { return null; }
        }

        private static string ProcessCommandLine(int pid)
        {
            string script = "(Get-CimInstance Win32_Process -Filter \"ProcessId=" + pid + "\").CommandLine";
            string cmd = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + EncodeBase64Unicode(script);
            return RunCapture("powershell.exe", cmd, 8000);
        }

        private static string EncodeBase64Unicode(string s)
        {
            return Convert.ToBase64String(Encoding.Unicode.GetBytes(s));
        }

        /// <summary>解析 dsh 包目录（where 结果 / npx 缓存 / 全局安装，取最新）。</summary>
        private static string ResolvePackageDir()
        {
            List<string> candidates = new List<string>();
            string where = RunCapture("where.exe", "dsh", 10000);
            if (!string.IsNullOrEmpty(where))
            {
                string[] lines = where.Split('\n');
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    int idx = line.IndexOf("node_modules", StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    string root = line.Substring(0, idx);
                    string rest = line.Substring(idx);
                    if (rest.StartsWith("node_modules\\.bin", StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(root + "node_modules\\@deepseek-ai\\dsh");
                    }
                    else
                    {
                        int pkg = rest.IndexOf("@deepseek-ai\\dsh", StringComparison.OrdinalIgnoreCase);
                        if (pkg >= 0) candidates.Add(root + rest.Substring(0, pkg + "@deepseek-ai\\dsh".Length));
                    }
                }
            }
            try
            {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache", "_npx");
                if (Directory.Exists(cache))
                {
                    string[] hashes = Directory.GetDirectories(cache);
                    foreach (string hashDir in hashes)
                    {
                        string p = Path.Combine(hashDir, "node_modules", "@deepseek-ai", "dsh");
                        if (Directory.Exists(p)) candidates.Add(p);
                    }
                }
            }
            catch { }
            string global = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@deepseek-ai", "dsh");
            if (Directory.Exists(global)) candidates.Add(global);

            // 便携 Node 自带的全局安装目录（一键安装 Node 后 dsh 装在这里）
            string pdir = PortableNodeDir();
            if (pdir != null)
            {
                string pp = Path.Combine(pdir, "node_modules", "@deepseek-ai", "dsh");
                if (Directory.Exists(pp)) candidates.Add(pp);
            }

            string best = null;
            foreach (string c in candidates)
            {
                if (!Directory.Exists(c)) continue;
                if (best == null) best = c;
                else
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(c) > Directory.GetLastWriteTimeUtc(best)) best = c;
                    }
                    catch { }
                }
            }
            return best;
        }

        /// <summary>解析启动命令：优先 PATH 上的 dsh（cmd shim），否则用 node 直接跑 bin.js。</summary>
        private static string ResolveInvocation()
        {
            string where = RunCapture("where.exe", "dsh", 10000);
            if (!string.IsNullOrEmpty(where))
            {
                string[] lines = where.Split('\n');
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.IndexOf("node_modules\\.bin\\dsh", StringComparison.OrdinalIgnoreCase) >= 0) return "dsh";
                    if (line.EndsWith("\\dsh.cmd", StringComparison.OrdinalIgnoreCase)) return "dsh";
                    if (line.EndsWith("\\dsh.bat", StringComparison.OrdinalIgnoreCase)) return "dsh";
                }
            }
            string dir = ResolvePackageDir();
            if (dir != null)
            {
                string bin = Path.Combine(dir, "lib", "bin.js");
                if (File.Exists(bin))
                {
                    // 便携 Node 环境：用其自带 node.exe 全路径，避免依赖系统 PATH
                    string pdir = PortableNodeDir();
                    if (pdir != null) return "\"" + Path.Combine(pdir, "node.exe") + "\" \"" + bin + "\"";
                    return "node \"" + bin + "\"";
                }
            }
            return null;
        }

        private static string RunCapture(string file, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    // 并发读取两个流，避免管道缓冲区互堵
                    Task<byte[]> tso = ReadAllAsync(p.StandardOutput.BaseStream);
                    Task<byte[]> tse = ReadAllAsync(p.StandardError.BaseStream);
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return null;
                    }
                    byte[] so = null, se = null;
                    try { so = tso.Result; } catch { }
                    try { se = tse.Result; } catch { }
                    byte[] pick = (so != null && so.Length > 0) ? so : se;
                    return DecodeSmart(pick);
                }
            }
            catch { return null; }
        }

        private static Task<byte[]> ReadAllAsync(Stream s)
        {
            MemoryStream ms = new MemoryStream();
            return Task.Run(delegate
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            });
        }

        /// <summary>优先按 UTF-8 解码；若出现替换符（非 UTF-8 输出，如 GBK 的 taskkill），退回系统 ANSI 解码。</summary>
        private static string DecodeSmart(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            string utf = Encoding.UTF8.GetString(bytes);
            if (utf.IndexOf('\uFFFD') >= 0)
            {
                try { return Encoding.Default.GetString(bytes); }
                catch { }
            }
            return utf;
        }
    }
}
