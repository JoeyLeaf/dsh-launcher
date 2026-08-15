using System;
using System.Collections.Generic;

namespace DshLauncher
{
    /// <summary>
    /// 界面语言：简单键值表（zh / en 两列），默认中文。
    /// T(key) 取当前语言文案；F(key, args) 支持 {0} 占位。
    /// </summary>
    public static class Lang
    {
        public const string Zh = "zh";
        public const string En = "en";

        public static string Current = Zh;

        private static readonly Dictionary<string, string[]> Tbl = new Dictionary<string, string[]>
        {
            // 标题 / 副标题
            { "title",         new[] { "DSH 启动器", "DSH Launcher" } },
            { "tag",           new[] { "DeepSeek Harness · Web GUI 托管 · v{0}", "DeepSeek Harness · Web GUI Manager · v{0}" } },
            // 状态胶囊 / 状态行
            { "pill_running",  new[] { "运行中", "Running" } },
            { "pill_stopped",  new[] { "未运行", "Stopped" } },
            { "pill_checking", new[] { "检测中", "Checking" } },
            { "status_running",   new[] { "dsh 正在运行", "dsh is running" } },
            { "status_stopped",   new[] { "dsh 未运行", "dsh is not running" } },
            { "status_checking",  new[] { "检测中…", "Checking…" } },
            { "status_starting",  new[] { "启动中…", "Starting…" } },
            { "status_stopping",  new[] { "停止中…", "Stopping…" } },
            { "status_restarting",new[] { "重启中…", "Restarting…" } },
            { "status_start_sub", new[] { "正在拉起 dsh web，请稍候", "Starting dsh web, please wait" } },
            { "status_stop_sub",  new[] { "正在结束 dsh 进程并释放端口", "Stopping dsh and releasing the port" } },
            { "status_restart_sub", new[] { "停止 → 启动", "Stop → Start" } },
            { "sub_port_free", new[] { "端口 {0} 空闲，可点击「启动」", "Port {0} is free, click start" } },
            { "sub_install_dsh", new[] { "未检测到 dsh，可点击「更新 dsh」一键安装", "dsh not detected, click \"Install dsh\"" } },
            { "sub_npm_missing", new[] { "未检测到 npm / Node.js，请先安装（nodejs.org）", "npm / Node.js not detected, install first (nodejs.org)" } },
            // 信息卡片
            { "card_npm",      new[] { "npm 版本", "npm version" } },
            { "card_node",     new[] { "node 版本", "node version" } },
            { "card_inst",     new[] { "dsh 已装版本", "dsh installed" } },
            { "card_latest",   new[] { "dsh 最新版本", "dsh latest" } },
            { "card_unavailable", new[] { "无法获取", "unavailable" } },
            { "card_not_installed", new[] { "未安装", "not installed" } },
            { "install_node",  new[] { "安装 Node.js", "Install Node.js" } },
            { "install_dsh",   new[] { "安装 dsh", "Install dsh" } },
            { "need_node_first", new[] { "需先安装 Node.js", "Install Node.js first" } },
            { "update_chip",   new[] { "更新", "Update" } },
            { "refresh",       new[] { "刷新", "Refresh" } },
            // 主操作按钮（图标 + 文字）
            { "btn_start",     new[] { "启动", "Start" } },
            { "btn_stop",      new[] { "停止", "Stop" } },
            { "btn_restart",   new[] { "重启", "Restart" } },
            { "btn_open",      new[] { "打开界面", "Open UI" } },
            // 日志区
            { "log_title",     new[] { "日志", "Log" } },
            { "log_check",     new[] { "---- 检测环境 ----", "---- Checking environment ----" } },
            { "log_start",     new[] { "---- 启动 dsh ----", "---- Starting dsh ----" } },
            { "log_stop",      new[] { "---- 停止 dsh ----", "---- Stopping dsh ----" } },
            { "log_restart",   new[] { "---- 重启 dsh ----", "---- Restarting dsh ----" } },
            { "log_update",    new[] { "---- 更新 dsh ----", "---- Updating dsh ----" } },
            { "log_install_node", new[] { "---- 安装 Node.js（便携版）----", "---- Installing Node.js (portable) ----" } },
            { "hint_npm_missing", new[] { "提示：未检测到 npm / Node.js，请先安装 Node.js（https://nodejs.org）。", "Hint: npm / Node.js not detected. Install Node.js first (https://nodejs.org)." } },
            { "hint_dsh_missing", new[] { "提示：未检测到 dsh，可点击「更新 dsh」一键安装。", "Hint: dsh not detected. Click \"Install dsh\" to install." } },
            { "log_env_summary", new[] { "npm {0} · node {1} · dsh 已装 {2} · 最新 {3}", "npm {0} · node {1} · dsh {2} · latest {3}" } },
            { "log_update_available", new[] { " —— 有新版本可更新", " — update available" } },
            { "need_node_first_log", new[] { "请先安装 Node.js：点击「npm 版本」卡片的一键安装。", "Install Node.js first: click the install button on the npm card." } },
            // 托盘
            { "tray_open",     new[] { "打开主界面", "Open main window" } },
            { "tray_start",    new[] { "启动 dsh", "Start dsh" } },
            { "tray_stop",     new[] { "停止 dsh", "Stop dsh" } },
            { "tray_web",      new[] { "打开 Web 界面", "Open web UI" } },
            { "tray_exit",     new[] { "退出", "Exit" } },
            { "balloon_tray",  new[] { "已最小化到托盘，双击图标可重新打开。", "Minimized to tray. Double-click the icon to reopen." } },
            // 设置
            { "settings_title", new[] { "设置", "Settings" } },
            { "settings_port",  new[] { "端口", "Port" } },
            { "settings_port_hint", new[] { "dsh web 监听端口（默认 3080）", "dsh web listen port (default 3080)" } },
            { "settings_auto_open", new[] { "启动 dsh 成功后自动打开浏览器", "Open browser after dsh starts" } },
            { "settings_min_tray", new[] { "关闭窗口时最小化到托盘", "Minimize to tray on close" } },
            { "settings_auto_check", new[] { "程序启动时自动检测环境", "Check environment on startup" } },
            { "settings_lang", new[] { "界面语言", "Language" } },
            { "settings_lang_zh", new[] { "中文", "中文" } },
            { "settings_lang_en", new[] { "English", "English" } },
            { "settings_scale", new[] { "界面缩放", "UI scale" } },
            { "scale_small", new[] { "小", "Small" } },
            { "scale_medium", new[] { "中", "Medium" } },
            { "scale_large", new[] { "大", "Large" } },
            { "settings_tip", new[] { "更多设置将在后续版本中提供。", "More settings in future versions." } },
            { "settings_save", new[] { "保存", "Save" } },
            { "settings_cancel", new[] { "取消", "Cancel" } },
            { "settings_saved", new[] { "设置已保存：端口 {0} · 自动开浏览器 {1} · 关闭最小化 {2}", "Settings saved: port {0} · auto-open browser {1} · minimize on close {2}" } },
            { "on", new[] { "开", "on" } },
            { "off", new[] { "关", "off" } },
            // 安装 Node.js 流程
            { "node_downloading", new[] { "正在下载 Node.js…", "Downloading Node.js…" } },
            { "node_download_sub", new[] { "最新 LTS，约 30MB，请稍候", "Latest LTS, ~30MB, please wait" } },
            { "node_done", new[] { "Node.js 安装完成，重新检测环境…", "Node.js installed. Re-checking…" } },
            { "node_fail", new[] { "Node.js 安装未完成，请检查网络后重试。", "Node.js install incomplete. Check network and retry." } },
        };

        public static string T(string key)
        {
            string[] pair;
            if (!Tbl.TryGetValue(key, out pair)) return key;
            return Current == En && pair.Length > 1 ? pair[1] : pair[0];
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        public static bool IsEn
        {
            get { return Current == En; }
        }
    }
}
