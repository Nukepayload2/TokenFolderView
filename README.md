# TokenFolderView（代码名 TokenVisualizer）

基于 **VB.NET + Avalonia** 的桌面应用，用于可视化大语言模型分词器（tokenizer）的切分结果：打开一个文件夹即可统计每个文件 / 子目录的 token 数，并逐 token 着色查看任意文本文件的切分细节。

核心库 `TokenVisualizer.Core`（命名空间 `Tokenizers`）实现了与 HuggingFace [tokenizers](https://github.com/huggingface/tokenizers) 相同的分词管线语义（normalization / pre-tokenization / model / truncation / post-processing / padding），分词结果与参考实现一致（有 golden vector 与 parity 测试校验），并支持 `tokenizer.json` 的加载与序列化，可直接作为库使用。

> 性能：实测仅统计 token 数的用例下，本库单线程速度约为 Python 版 hf tokenizers（Rust 参考实现, 提交 828e4830f7c9e0ff8b75a2433d9814b802b43c3d）的[五倍](tests/performance/)。

## 目标平台

| 项目 | 说明 |
|------|------|
| 目标框架 | `.NET 10`（`net10.0`） |
| 运行平台 | Windows / Linux / macOS（Avalonia Desktop） |
| UI 技术 | Avalonia 12 + FluentAvalonia，界面语言为中文 |

## 编译环境

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)（命令行 `dotnet` 可用即可）
- 解决方案文件：`TokenVisualizer.slnx`

仓库结构：

```
TokenVisualizer/               # Avalonia 桌面应用（WinExe）
TokenVisualizer.Core/          # 分词核心库（命名空间 Tokenizers）
TokenVisualizer.Core.Tests/    # MSTest 单元测试
tests/performance/             # 性能基准（dev-only，不在 slnx 中）
deepseek-v4-flash/             # 内置分词器（tokenizer.json + tokenizer_config.json）
```

## 构建与运行

```bash
# 构建整个解决方案
dotnet build TokenVisualizer.slnx

# 运行桌面应用
dotnet run --project TokenVisualizer

# 运行单元测试
dotnet test TokenVisualizer.Core.Tests

# 发布（按目标平台选择 RID）
dotnet publish TokenVisualizer -c Release -r win-x64
dotnet publish TokenVisualizer -c Release -r linux-x64
dotnet publish TokenVisualizer -c Release -r osx-arm64
```

首次启动时，应用会自动注册仓库内置的 `deepseek-v4-flash/tokenizer.json` 作为默认分词器（按绝对路径引用，不复制文件）。

## 用法

### 1. 词元统计（Explorer 页）

- 点击 **打开文件夹** 选择要扫描的目录；扫描完成后左侧树显示每个文件 / 文件夹的 token 计数，底部状态栏显示总计、扫描 / 跳过文件数。
- 点选任意文件，右侧以等宽字体逐 token 着色展示切分结果（不同颜色区分不同 token）。
- 顶部标题栏搜索框可按名称过滤文件 / 文件夹；**重新扫描** 可在修改过滤设置后重扫。

### 2. 设置（Settings 页）

- **分词器**：管理 tokenizer 模型——添加（选择 tokenizer.json 与 tokenizer_config.json）、设为当前使用、删除（内置 deepseek 不可删）。
- **扫描**：最大文件大小（MB，默认 10，超过跳过）、是否跳过二进制文件（默认开启）、文件夹黑名单（每行一个，默认含 `bin`、`obj`、`node_modules`、`.git` 等）。
- **外观**：主题（跟随系统 / 浅色 / 深色）。

所有设置持久化在系统的 LocalApplicationData 目录下的 `TokenVisualizer\settings.json`：

- Windows：`%LocalAppData%\TokenVisualizer\settings.json`
- Linux：`~/.local/share/TokenVisualizer/settings.json`
- macOS：`~/Library/Application Support/TokenVisualizer/settings.json`

### 3. 核心库（TokenVisualizer.Core）

```vb
Imports Tokenizers

' 从 HuggingFace tokenizer.json 加载
Dim tok As Tokenizer = Tokenizer.FromFile("deepseek-v4-flash/tokenizer.json")

Dim count As Integer = tok.EncodeCount("hello 世界")          ' 快速统计 token 数
Dim enc As Encoding = tok.Encode("hello 世界")                 ' 完整编码（含 offsets 等）
Dim text As String = tok.Decode(enc.Ids)                       ' 解码回文本
```

常用 API：`Encode` / `EncodeFast` / `EncodeCount` / `EncodeBatch` / `EncodeCharOffsets`、`Decode` / `DecodeBatch` / `DecodeStream`、`Tokenizer.Load` / `FromFile`。

### 4. 性能基准（dev-only）

`tests/performance/bench.ps1` 将 `TokenVisualizer.Core` 与 Python `tokenizers` 参考实现跑同一份确定性文本并对比（`tests/performance/bench` 控制台工程不参与 slnx 构建）：

```powershell
./tests/performance/bench.ps1                                # 合成 ~2 MB 混合文本
./tests/performance/bench.ps1 -Path .\TokenVisualizer.Core   # 真实文件夹 parity + 性能
```

## 贡献
允许 AI 生成的代码，但必须先提出 issue 讨论用例、方案，和验收条件。无关联 issue 的 PR 通常不会被合并。

默认验收条件

- 能很好地实现提出的需求
- 不做多余的事情
- 软件本体遵守 [VB Coding Conventions](https://learn.microsoft.com/en-us/dotnet/visual-basic/programming-guide/program-structure/coding-conventions)

## 许可证

- 代码：MIT，见 [License.txt](License.txt)
- `deepseek-v4-flash/` 分词器数据：遵循其自身许可证，见 [deepseek-v4-flash/LICENSE](deepseek-v4-flash/LICENSE)
