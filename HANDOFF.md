# DshLauncher 项目交接文档（HANDOFF）

> 生成时间：2026-08-16
> 用途：在新 DSH 会话（工作区 `G:\Projects\dsh-launcher`）中无缝继续本项目的开发与维护。
> 使用方法：新会话第一条消息粘贴：「读取 `G:\Projects\dsh-launcher\HANDOFF.md`，按其中"待办与可选方向"继续工作。我另有具体要求会直接告诉你。」

---

## 1. 项目是什么

**DshLauncher**：一个轻量的 Windows 托盘小工具（C# / .NET Framework 4.8，零依赖，用系统自带 csc 编译），用于托管 **DeepSeek Harness（dsh）** 的 Web 界面——一键检测/启动/停止/重启 dsh web、状态灯、托盘驻留、设置面板（端口/语言/缩放等）。

- 源码目录：`G:\Projects\dsh-launcher`
- 入口：`DshLauncher.cs`；逻辑：`DshService.cs`；设置：`SettingsForm.cs`（配置存 `%APPDATA%\DshLauncher\config.json`）；文案：`Lang.cs`（中/英）；主题：`Theme.cs`
- 编译：`powershell -ExecutionPolicy Bypass -File .\build.ps1`（csc `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，产物 `DshLauncher.exe`）
- 自检：`.\DshLauncher.exe --selftest`（在 3081 端口试验，写 `selftest.log`）
- 界面语言：中文；日志：`G:\Projects\dsh-launcher\logs\dsh-web.log`

## 2. 当前运行状态（重要）

- **dsh 主实例**：监听 `127.0.0.1:3080`，由 DshLauncher 以 cmd 拉起，真实命令：
  `node "C:\Users\TEST\AppData\Local\npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh\lib\bin.js" web --port 3080 --trusted-host dsh.evermoon.me`
- **dsh 版本**：`@deepseek-ai/dsh` 0.1.0-rc.6（npx 缓存安装）
- **cloudflared 隧道**：Docker 容器 `elegant_lamarr`（`cloudflare/cloudflared:latest`），remote-managed token 方式运行（`tunnel run --token <token>`），bridge 网络。公网域名 `dsh.evermoon.me → http://host.docker.internal:3080`（ingressRule=0）。
- **Cloudflare Access**：已为 `dsh.evermoon.me` 启用 JWT 校验（日志确认 `access: {audTag:[...], required:true, teamName:"lingering-cake-4f01"}`，配置 version=21）。Access 应用 + Allow(用户本人邮箱) 策略已生效。
- **本机也有 NetBird**（网卡 `wt0`，IP `100.73.0.1`）在跑，与 cloudflared 方案无关，未使用它暴露 dsh。

## 2.4 已完成工作：「点更新未完成」bug 定位与修复（2026-08-19）✅

**现象**：用户点主窗口「更新」→ 日志「更新未完成」。
**根因**（已在本机 100% 复现）：`DshService.Update()` 拼命令时对 npm 无条件加引号
（`cmd /c "npm" install -g ...`）。本机 PATH 里 npm 首解析到 `C:\Program Files\nodejs\npm`（bash shim，
无扩展名）/ `%APPDATA%\npm\npm`（npm 生成的 405B bash shim）。cmd 对**带引号的裸命令名**会走到这些
bash shim——脚本按 `$0` 计算 basedir（裸名 → `.`），于是执行
`node ./node_modules/npm/bin/npm-cli.js`，**按当前工作目录解析** → `MODULE_NOT_FOUND`（报错路径形如
`G:\Projects\dsh-launcher\node_modules\npm\bin\npm-cli.js`），exit 1。裸写 `npm` 或带引号**完整路径**都正常
（走 .cmd shim，实测均 exit 0）。
**修复**：`Update()` 里仅当 npm token 含 `\` 或空格（完整路径）才加引号；裸命令名裸写。
顺带加固：5 分钟超时中止改用 `taskkill /T /F` 杀整棵进程树（原来只 Kill cmd，会留孤儿 npm 继续后台装）。
**验证**：修复后同机 `npm install -g` 成功，dsh 全局升到 **0.1.0-rc.7**（`dsh --version` 确认；
注意 npm 12 新策略默认拦截依赖的 install scripts，本机已用 `--allow-scripts=@deepseek-ai/dsh-subprocess-local,koffi,node-pty,@google/genai,protobufjs` 补装，后续 launcher 更新若再见到 `npm warn install-scripts` 需同样处理，否则 node-pty 等未构建可能运行时缺件）。
编译 + selftest 通过（selftest 中 installedDsh=latestDsh=0.1.0-rc.7，更新徽标应消失）。
复现命令备忘：`cmd /c '"npm" --version'`（败） vs `cmd /c 'npm --version'`（成） vs `cmd /c '"C:\Program Files\nodejs\npm.cmd" --version'`（成）。

## 2.5 已完成工作：设置窗口 UI 重做 + 主窗口外边框（2026-08-19）✅

用户反馈设置界面"不美观"，对 `SettingsForm` 做整体重做（编译 + selftest 通过，exe 111,616 字节）：

- **动态窗高 + 展开动画**：「信任域名 / 代理」二级区 140ms smoothstep 展开/收起，`ComputeLayout()` 统一重算全部几何与窗高，消除原来固定 758 高窗下半部大面积留白。
- **分组标题**：`sec_general`/`sec_network`/`sec_appearance`（Lang.cs 新增键），三条分隔线有了语义。
- **端口行**：输入框旁实时 URL 预览（`http://127.0.0.1:{port}`）；非法输入红描边 + 红提示（`port_invalid` 键），不再无感知静默回退 3080。
- **交互**：复选框行 hover 背景 + 行首 Lucide 图标（Theme.cs 新增 `Globe`/`Wifi`/`Monitor` 常量，勾选时图标变强调色）；Esc=取消、Enter=保存（KeyPreview）；缩放胶囊点击即时重建本窗（实时预览；设置窗 `_s` 改为主窗口同款规则 `UiScale/100` + 屏幕钳制，修掉"选大之后设置窗反而变小"的不一致）。
- **视觉**：标题鲸鱼徽标（与主窗口同款渐变 + `SvgWhale`）+ 版本号；两窗口统一 1px `CardBorder` 外边框与 14px 圆角；chip hover 描边改回 `CardBorder`；复选框行高统一 26。
- 踩坑：该老 csc（v4.0.30319）的 `System.Windows.Forms.dll` **没有 `Control.SetBounds(Rectangle)` 1 参重载**，必须用 4 参 int 版；`RectangleF` 无 `(Rectangle)` 构造，须转 4 个 float。
- 注意：部署时旧托盘进程正在运行（exe 被锁），已获用户同意后结束旧进程再 build；dsh web（3080）是其 cmd 子进程，杀 launcher 后仍存活。

## 2.6 已完成工作：高 DPI（150%）会话下文字溢出窄框截断的修复（2026-08-19）✅

用户要求"用视觉审查 launcher 界面（含设置窗）"。结论与修复：

- **问题**：`Theme.Font` 用**点号单位**创建字体，GDI+ 按窗口 DPI 换算像素——150%（144dpi）RDP 会话下
  10pt 渲染成 20px，而布局是固定设计像素（640x600 窗、70px 标签框等，"固定设计尺寸"本就是设计目标）。
  结果：设置窗窄框文字被 GDI+ 在框边**硬截断、无省略号**——「界面语言」→「界面语」、「界面缩放」→「界面缩」、
  英文 `Language`→`Langu` / `UI scale`→`UI sca`、信任域名开关行「（--trusted-host）」被切、信任提示行
  「…dsh.example.com」丢尾。主窗口因框体纵向余量大未截断，但文字同样 1.5x 偏大。96dpi 会话下一切正常
  （截断只在 ≥125% 会话出现）。
- **修复**（Theme.cs / SettingsForm.cs / DshLauncher.cs / Lang.cs，编译 + selftest 通过）：
  1. `Theme.Font(size, style)` 改为**按 96dpi 像素值以 `GraphicsUnit.Pixel` 创建**（`size * 96f / 72f`），
     任意 DPI 会话下文字与设计一致；96dpi 下与原点号单位像素级一致（老用户外观零变化）。
     新增 `Theme.FontConsolas` 同规则，LogView 构造与主窗口两处 `_logView.Font` 同步改用（LogView 行高
     用 `Font.GetHeight(g)` 取自适应，无行重叠风险）。
  2. 设置窗复选框行标签改用独立 `StringFormat`（`Trimming = StringTrimming.EllipsisCharacter`，
     注意 .NET Framework 枚举名是 `EllipsisCharacter`，不是 Core 的 `CharacterEllipsis`），超长时
     尾部省略号兜底，不再硬切。
  3. 英文文案小改：`settings_port_hint` "listen port" → "listening port"。
- **验证**：当前 150% 会话下 `--shot` 截图（screenshots\fix-*.png）确认：中/英设置窗全部标签与长行
  完整、主窗口文字回到 96dpi 设计尺寸；selftest OK。
- **新增诊断开关**：`DshLauncher.exe --diag` —— 启动主窗口并把 `_s` / ClientSize / 屏幕工作区 /
  窗口 DPI 写到 `logs\dpi-diag.txt` 后退出，排查缩放/DPI 问题用（MainForm.DiagInfo()）。
- **审查中排除的疑点**（勿重复排查）：设置窗徽标与标题**无**重叠（像素扫描证实有 19px 间隙，
  低分辨率缩略图造成的错觉）；复选框行图标与勾选框**无**重叠；DPI-unaware 工具进程测量
  DPI-aware 窗口的 GetWindowRect/DwmGetWindowAttribute 会得到混空间的怪值（如 1067x656），
  非应用 bug——应用内 `--diag` 才是可信来源。
- 截图方法备忘：`--shot a.png --shot-settings b.png`（应用内 PrintWindow，6.5s 后退出）；
  不同状态通过临时改 `%APPDATA%\DshLauncher\config.json`（language / uiScale / trustedHosts /
  proxyEnabled / port）触发（trustedHosts 非空或 proxyEnabled=true 时对应二级区默认展开）。
  注意：本环境 `.\DshLauncher.exe` 前台调用**不阻塞**（GUI 进程立即返回），须
  `Start-Process -PassThru` + `WaitForExit`。

## 3. 已完成工作：为 DshLauncher 增加 `--trusted-host` 支持 ✅

**背景**：dsh 的 web 服务有 browser-trust fence——`/api` 请求的 Host 非 loopback 时需在 `trustedHosts` 白名单，否则 403。经公网域名（cloudflared 隧道）访问时 Host 是 `dsh.evermoon.me`，必须给 dsh 传 `--trusted-host dsh.evermoon.me`。原 DshLauncher 拼参写死 `web --port <port>`，无此能力。

**改动（4 个源文件，已编译并 selftest 通过）**：
- `SettingsForm.cs`：设置模型新增 `public string TrustedHosts = ""`（逗号分隔）；`ConfigStore` 读写 `trustedHosts` 键（老配置无此键自动回退空串）；`JsonMini` 新增 `Escape()` 转义助手；设置对话框新增深色自绘输入框 `_trustedBox`（标签/圆角容器/聚焦描边/提示文案），窗体高度 470→520，重排布局。
- `DshService.cs`：`Start(...)` 新增末参 `string trustedHosts`；拼 `web --port <port>` 后用 `SplitTrustedHosts()` 拆分（逗号/分号/空白，去空去重）逐个追加 ` --trusted-host <host>`；空配置不追加；新增 `SplitTrustedHosts(string)` 助手。
- `DshLauncher.cs`：三处 `DshService.Start(...)` 调用全部传入 trusted hosts（`StartAsync`≈1127、`RestartAsync`≈1171 传 `_settings.TrustedHosts`；selftest≈272 传 `""`）；`OpenSettings()` 保存时回写 `_settings.TrustedHosts`。
- `Lang.cs`：新增双语键 `settings_trusted`、`settings_trusted_hint`（提示：需要公网/隧道域名访问时填写，逗号分隔）。

**验证结果**：
- 编译成功（`$LASTEXITCODE=0`），产物 `DshLauncher.exe`（103,424 字节）。
- `--selftest` → `SELFTEST OK`，failures=0。
- 运行时确认：dsh 进程命令行实际带 `--trusted-host dsh.evermoon.me`，公网普通 RPC 200、WebSocket 事件流（`wss://dsh.evermoon.me/api/events.mux`）101 升级成功。

## 4. 关键技术结论（避免重复踩坑）

1. **dsh 特权平面 403 是设计**：`@deepseek-ai/dsh-client-connection` 的 `PRIVILEGED_METHODS`（设置 `settings.describe`、凭据 `credentials.describe`、宿主路径 `host.openPath`、LLM 模型发现等）用**空信任列表**判定，只认 loopback（`127.0.0.1`），`--trusted-host` 有意不覆盖，也没有配置开关。→ 这些功能**只能在 `http://127.0.0.1:3080` 本机用**，公网 403 属预期，不是 bug，不要试图放开（会破坏"配置即密钥/宿主文件探测"防护，且 dsh 升级会覆盖改动）。
2. **`--trusted-host` 值**：host 或 host:port，无端口条目按 hostname 匹配任意端口（WHATWG 归一化，大小写/冗余 `:80` 不影响）。`dsh.evermoon.me` 匹配公网缺省 443 正确。
3. **`--host` 保持默认 `127.0.0.1`**：不要改 `0.0.0.0`（dsh 会拒绝且危险）。
4. **隧道侧 "Enforce Access JWT validation"**：保持 **On**（让 cloudflared 在请求到达 dsh 前校验 JWT），并确保该 hostname 在 "Applications" 下拉关联了 Access 应用。Off 的话要求源站自行解析 `Cf-Access-Jwt-Assertion` 头——dsh 无此能力。
5. **Access 与 dsh 权限互补**：Access 管"谁能从公网进"（人），`--trusted-host` 管"dsh 信任哪个 Host"（传输），特权平面只管"本机"（安全）。三者都要，缺一不可。

## 5. 已知边界 / 观察项

- cloudflared 日志有**周期性 `context canceled`**（约每 2 分钟一次，ingressRule=0）：请求到达 dsh 后连接被中断，疑似长连接（WebSocket/SSE）空闲重置或外部探测，不影响功能，可忽略；若需深挖可查是否与浏览器心跳或某监控脚本有关。
- 隧道还有其它 hostname（`mcmcp.evermoon.me → 10.10.6.227:3334`、`trmcp.evermoon.me → 10.10.6.227:3333`），其中 mcmcp 目标服务当前报 `connection refused`（10.10.6.227:3334 未监听）——与本项目无关，是另一服务的问题。
- 曾误开过第二个 dsh 实例（端口 3082，工作区 dsh-launcher）用于尝试"新会话"，已关闭（3082 已释放）。**不要再用独立实例的方式满足"多会话"需求**——DSH UI 原生支持在侧边栏切换工作区并在其下新建会话。

## 6. 常用命令

```powershell
# 编译
powershell -ExecutionPolicy Bypass -File .\build.ps1
# 自检（3081 端口，不影响 3080）
.\DshLauncher.exe --selftest
# 查看 dsh 启动日志
Get-Content "G:\Projects\dsh-launcher\logs\dsh-web.log" -Tail 30
# 查看 3080 进程真实命令
Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.CommandLine -match 'dsh.*web' }
# 手动起 dsh（带 trusted-host）
node "C:\Users\TEST\AppData\Local\npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh\lib\bin.js" web --port 3080 --trusted-host dsh.evermoon.me
# 看 cloudflared 容器日志（Docker Desktop 引擎管道）
$env:DOCKER_HOST='npipe:////./pipe/dockerDesktopLinuxEngine'; docker logs --tail 30 elegant_lamarr
```

## 7. 待办与可选方向（等用户确认）

- [x] （2026-08-19）主窗口 + 设置窗视觉审查：高 DPI 截断问题已修复（见 2.6）；其余状态（停止态/展开态/英文/115% 缩放）审查未见其他问题
- [ ] （待用户提出）DshLauncher 后续功能/修复需求
- [ ] 可选：把 trusted-host 支持写进 README.md（目前 README 未提及新字段，配置说明需补充）
- [ ] 可选：DshLauncher 支持管理多个 dsh 实例/端口（当前只管理单端口 3080）
- [ ] 可选：为 `dsh.evermoon.me` 之外再暴露第二个域名（如 dsh2）时，tunnel 加 hostname + Access 加应用 + dsh 加 `--trusted-host` 三件套

---

*本文件由 DSH 会话（工作区 D:\dsh）总结生成，供新会话无缝衔接。*
