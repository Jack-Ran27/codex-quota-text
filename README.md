# 额度小字 · Quota Text

在 GPT 桌面版左下角看一眼，就知道还剩多少额度、什么时候重置。

这是一个 Windows 本地小组件，当前为 **v1.2.1-beta.1 测试版**。它不占任务栏，也不用单独打开看板。

[下载 Windows 安装包](https://github.com/Jack-Ran27/codex-quota-text/releases/download/v1.2.1-beta.1/QuotaText-v1.2.1-beta.1-Windows-x64.zip) · [完整源码包](https://github.com/Jack-Ran27/codex-quota-text/releases/download/v1.2.1-beta.1/QuotaText-v1.2.1-beta.1-Source.zip) · [版本页面](https://github.com/Jack-Ran27/codex-quota-text/releases)

显示示例（示意数据）：

```text
75%
重置
10/31
20:00
```

## 能做什么

- 显示本机 Codex 登录账号的剩余比例和重置时间。
- 页面在前台时每分钟刷新，切到其他程序或最小化时隐藏。
- 随 GPT 窗口移动，文字不接收鼠标点击，也不抢输入焦点。
- 安装后随当前用户登录 Windows 自动运行，等 GPT 打开再显示。

## 使用要求

Windows x64、.NET Framework 4.8，以及已登录的 GPT/Codex 桌面版。额度读取还需要桌面版附带的 Node.js 运行时和 Codex 程序。

目前实际验证的是 Windows 11、125% 缩放和本次使用的桌面客户端。其他缩放、主题、布局和版本仍需测试；客户端改版后，位置或额度接口可能需要适配。

## 安装和卸载

1. 解压完整的发行包，保留 `app` 文件夹。
2. 双击 `安装.cmd`。它会安装到当前用户的本地程序目录，并添加当前用户的登录启动项，不需要管理员权限。
3. 打开 GPT，查看左下角文字。

卸载时，在发行包中双击 `卸载.cmd`。它只删除本工具的已知程序文件和匹配的启动项，不修改 GPT 账号或聊天记录。工具在本地留下的运行状态文件会保留。

发行包未做代码签名。可核对 SHA-256 校验值，也可以从源码自行编译。

## 额度范围与隐私

额度来自本机 Codex 的 `account/rateLimits/read` 接口，属于账号的 Codex / Work 共享额度，不覆盖普通 ChatGPT 每个模型的所有消息上限。多个周期并存时，显示 `codex` 分组里剩余比例最低的周期及其重置时间。日期时间使用北京时间。

本工具不读取聊天内容，也不读取、复制或上传登录令牌文件；不要求另填 API 密钥。额度请求由本机 Codex 程序与 OpenAI 服务完成。查询子进程关闭插件和 Apps 加载，并在结束时回收自己的进程树，避免重复启动其他插件。

“待刷新”表示读取失败，文字可能是上一次数据；“未提供”表示接口没有返回对应字段。跨过重置时刻后，仍以服务端返回值为准。

## 从源码构建

完整源码目录在 Releases 的 `Source.zip` 中，解压后 `src/QuotaText.cs` 是文字组件，`src/Setup.cs` 是安装程序，`app/*.mjs` 是额度读取代码。可运行 `build.ps1`，或用 Windows 自带的 .NET Framework C# 编译器构建：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:app\QuotaText.exe src\QuotaText.cs
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /optimize+ /out:Setup.exe src\Setup.cs
```

## OpenAI 插件目录

`openai-plugin/` 是同一小字组件的本地 MCP 包装，只有启动、暂停和状态工具，不包含额度看板。它是待提交版本，尚未上架。提交材料在 `openai-submission/`。

本项目是独立的第三方小工具，与 OpenAI 无隶属或认证关系。MIT 许可证见 `LICENSE`。
