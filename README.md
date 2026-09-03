# AutoElective Orb

一个面向 Windows 的本地悬浮选课助手，提供课程读取、余量监控、本地验证码识别、候选分课优先级、定时安全预热和带回滚记录的换课功能。

> 非官方工具。使用前请确认学校规则允许自动请求。刷新间隔被强制限制为不低于 4 秒。自动换课不是原子操作，退掉原课程后无法保证成功选到目标课程，也无法保证回滚成功。

## 下载即用（推荐）

1. 打开仓库右侧 **Releases**。
2. 下载 `AutoElectiveOrb-windows-x64.zip`，解压到普通文件夹。
3. 双击 `AutoElectiveOrb.exe`。

Release 便携包内含隔离的 Python 3.12 运行时和全部 Python 依赖，不需要安装 Python，也不需要管理员权限。第一次启动本地 OCR 时可能需要几秒钟加载模型。

请解压完整 ZIP 后再运行，不能只复制 EXE；`engine/`、`runtime/` 和 `assets/` 必须与 EXE 保持在同一目录中。

卸载时双击 `UninstallAutoElectiveOrb.exe`。它可以关闭悬浮球、移除开机启动，并由用户选择是否删除保存的统一认证凭据、本地设置、日志、换课历史及便携程序文件。为避免容易触发安全软件的自删除行为，卸载器自身会保留，关闭后直接删除剩余文件夹即可。

## 一键更新

- 在设置页、悬浮球右键菜单或托盘菜单中点击“检查更新”，即可下载并原地升级。
- 更新器会核对 GitHub Release 提供的 SHA-256，校验通过才会安装；失败时自动恢复旧版。
- 更新只替换程序文件，不会删除 `%LOCALAPPDATA%\AutoElectiveOrb` 中的设置、日志、换课记录，也不会删除 Windows 凭据。
- 从 `v1.2.0` 起发布包已内置更新器。仍在使用更早版本的用户，只需从最新版 Release 单独下载 `UpdateAutoElectiveOrb.exe`，放进原程序目录并双击一次，以后无需删除旧目录。
- 源码仓库不会被自动覆盖；开发者请使用 Git 更新源码。

目标课程表中的单元格仍可直接编辑。选中任意单元格后点击“删除所选行”，程序会删除该单元格所在的整行；按住 `Ctrl` 选择多行中的单元格即可批量删除，也可使用键盘 `Delete`。

不要只从 GitHub 的 **Source code** 压缩包里双击 EXE；源代码包不包含便携运行时。如需从源代码运行，请使用下一节的一键安装。

## 从源代码安装

环境要求：

- Windows 10 或 Windows 11，64 位
- Python 3.10、3.11 或 3.12（建议 3.12）
- 可访问北京大学统一认证和选课系统的网络环境
- Windows PowerShell 5.1 与 .NET Framework 4.x（Windows 10/11 通常自带）

操作步骤：

1. 下载并解压仓库的 Source code，或克隆仓库。
2. 双击 `install.cmd`。
3. 安装完成后双击 `AutoElectiveOrb.exe`，以后也可使用 `run.cmd`。

安装器会在项目内创建 `.venv`，安装依赖、编译 WinForms 程序并运行离线测试，不需要管理员权限。若电脑有多个 Python，可设置 `AUTOELECTIVE_BOOTSTRAP_PYTHON` 指向希望使用的 `python.exe`。

## 使用方法

1. 输入学号和统一认证密码。密码只保存在 Windows 凭据管理器。
2. 点击“读取课程 / 智能换课”，程序自动识别预选或补退选阶段；补选阶段会直接读取选课计划中的全部课程，并逐页补充教师和余量信息。
3. 课程较多时可按课程名、教师、班号或开课单位即时搜索，多个关键词用空格分隔；勾选后可直接“添加普通监控”，也可在左侧选择旧课并“创建自动换课”。
4. 同名课程的多个分课会组成候选组，优先级数字越小越先尝试，例如张老师填 1、李老师填 2；成功选中一个分课后，同名候选自动停止。不同课程名分别计算优先级，不会互相排斥。
5. 点击“开始监控”。设置窗口可以关闭，悬浮球和托盘仍会运行。

定时启动：勾选“定时启动”并设置时间。程序会立即加载本地 OCR，并通过只读帮助页验证账号和选课系统登录，然后显示倒计时。到点前不会扫描补退选课程或提交课程操作。如果设定时间今天已经过去，则等待次日同一时间。

快捷操作：

- 单击悬浮球：打开设置
- 中键单击：开始或停止
- `Ctrl + Alt + E`：唤出设置
- 右键悬浮球或托盘：显示、隐藏、开机启动、日志和换课历史

## 数据、隐私与日志

所有运行数据都位于 `%LOCALAPPDATA%\AutoElectiveOrb`，不会写入仓库目录：

- `settings.json`：不含密码的界面设置
- `engine.ini`：不含密码的临时引擎配置
- `swap-history.log`：用户可直接阅读的永久换课历史
- `swap-history.jsonl`：用于故障恢复判断的结构化换课历史
- `log/`：按日期轮转的错误日志

验证码仅在本地使用 `ddddocr`，不会上传第三方识图服务。密码通过进程环境变量短暂传递给本地引擎，不写入配置或日志。默认不会保存学校页面正文、Cookie、Token 或请求头；详细说明见 [SECURITY.md](SECURITY.md)。

公开 issue 或截图前，请主动遮挡学号、课程选择、Cookie、Token 和其他个人信息。

## 稳定性设计

- 课程表按表头语义识别，不依赖固定表格顺序。
- 课程读取有 90 秒硬超时并支持手动取消。
- 首轮完整扫描，之后仅刷新目标课程页，每 5 分钟全量校准。
- 本地验证码连续失败最多重试 5 次。
- 换课前后记录准备、退课请求、退课确认、目标提交、成功、失败和回滚。
- 发现上次换课可能中途结束时，下次启动主动告警。
- 所有真实退选和补选只会在补退选入口开放后进行。

## 开发与测试

```powershell
install.cmd
.venv\Scripts\python.exe -m unittest discover -s tests -v
build.cmd
```

主要目录：

- `src/`：WinForms 桌面界面和 Windows 凭据管理器封装
- `assets/`：悬浮球与托盘共用的界面图形资源
- `engine/`：登录、解析、监控、本地 OCR 与换课状态机
- `tests/`：完全离线的页面解析和换课日志测试
- `.github/workflows/release.yml`：构建自带运行时的 Windows 便携包

测试不得使用真实账号，不得提交真实验证码、退课或选课请求。页面兼容测试应使用经过脱敏的最小 HTML fixture。

## 发布

推送 `v*` 标签（例如 `v1.0.1`）会运行测试、编译主程序和卸载器、加入官方 CPython 3.12 embeddable runtime，并发布 ZIP 与 SHA-256 校验文件。配置证书时会对两个 EXE 进行 Authenticode 签名和校验；没有证书时仍允许发布，但包内会包含 `UNSIGNED_BUILD.txt`，Windows 可能显示未知发布者警告。

### 配置 Windows 代码签名

公开发布应使用受信任 CA 颁发的 OV/EV 代码签名证书，或使用 SignPath Foundation 等面向开源项目的受信任签名服务。自签名证书仅适合本机开发测试，不能让其他电脑自动信任发布者，也不能可靠消除 SmartScreen 提示。

使用可导出的 PFX 证书时，在仓库 **Settings → Secrets and variables → Actions** 中配置：

- Secret `WINDOWS_CERTIFICATE_BASE64`：PFX 文件的 Base64 内容。
- Secret `WINDOWS_CERTIFICATE_PASSWORD`：PFX 密码。
- 可选 Variable `WINDOWS_TIMESTAMP_URL`：证书提供商给出的 RFC 3161 时间戳地址；未配置时使用 DigiCert 公共时间戳服务。

不要把 PFX、私钥、密码或 Base64 内容提交到仓库、Issue、聊天记录或构建产物。工作流只在 GitHub 托管的临时 Windows 运行器中恢复证书，签名完成后会删除证书文件和证书存储中的临时副本。

可在 Windows 中右键发布包内的 `AutoElectiveOrb.exe`，打开 **属性 → 数字签名** 查看发布者和时间戳；也可核对同一 Release 中的 `.sha256` 文件。已发布的 `v1.0.0` 是历史未签名版本，配置证书后应发布新的版本标签，不要覆盖旧文件。

## 许可证

项目由 Studyer-Tang 开发并使用 [MIT License](LICENSE)。运行时依赖及其许可证说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
