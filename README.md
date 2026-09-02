# AutoElective Orb

一个面向 Windows 的本地悬浮选课助手，提供课程读取、余量监控、本地验证码识别、候选分课优先级、定时安全预热和带回滚记录的换课功能。

> 非官方工具。使用前请确认学校规则允许自动请求。刷新间隔被强制限制为不低于 4 秒。自动换课不是原子操作，退掉原课程后无法保证成功选到目标课程，也无法保证回滚成功。

## 下载即用（推荐）

1. 打开仓库右侧 **Releases**。
2. 下载 `AutoElectiveOrb-windows-x64.zip`，解压到普通文件夹。
3. 双击 `AutoElectiveOrb.exe`。

Release 便携包内含隔离的 Python 3.12 运行时和全部 Python 依赖，不需要安装 Python，也不需要管理员权限。第一次启动本地 OCR 时可能需要几秒钟加载模型。

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
2. 点击“读取课程 / 智能换课”，程序自动识别预选或补退选阶段并扫描课程。
3. 普通监控可直接添加目标课程；换课则在左侧选择原课程、右侧勾选候选分课。
4. 候选优先级数字越小越先尝试，例如张老师填 1、李老师填 2；同一换课组成功一个后，其他候选自动作废。
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
- `engine/`：登录、解析、监控、本地 OCR 与换课状态机
- `tests/`：完全离线的页面解析和换课日志测试
- `.github/workflows/release.yml`：构建自带运行时的 Windows 便携包

测试不得使用真实账号，不得提交真实验证码、退课或选课请求。页面兼容测试应使用经过脱敏的最小 HTML fixture。

## 发布

推送 `v*` 标签（例如 `v1.0.0`）会运行测试、编译程序、加入官方 CPython 3.12 embeddable runtime，并发布 `AutoElectiveOrb-windows-x64.zip`。也可在 Actions 页面手动运行工作流生成构建产物。

## 许可证与致谢

项目使用 [MIT License](LICENSE)。Python 选课引擎整理自 Rabbit 的 MIT 许可 PKUAutoElective 项目；第三方组件说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
