# SpectrumAnalyzer · 拉曼光谱分析软件

![platform](https://img.shields.io/badge/platform-Windows-blue) ![.NET](https://img.shields.io/badge/.NET%20Framework-4.7.2-512BD4) ![UI](https://img.shields.io/badge/UI-WPF%20%2F%20MVVM-blueviolet) ![license](https://img.shields.io/badge/license-MIT-green)

> **English** · SpectrumAnalyzer is a Windows desktop application for Raman spectroscopy data processing and polymer identification, developed as the author's undergraduate graduation project (B.E. in Optoelectronic Information). It provides a complete analysis pipeline: CSV spectrum import, preprocessing (cosmic-ray spike removal, Savitzky–Golay smoothing, baseline correction, normalization), automatic peak detection with parameter auto-tuning, and matching against a local reference spectral library via similarity score and HQI (Hit Quality Index). The solution is built with C# / WPF on .NET Framework 4.7.2, organized into Core / Data / Services / UI layers.

---

## 项目简介

本项目是作者（光电信息科学与工程 · 本科毕业设计）开发的拉曼光谱分析桌面软件，面向聚合物拉曼光谱的**数据预处理、峰位提取与参考谱库匹配**，支持 PC / PE / PET / PMMA / PP / PVC 等常见聚合物的定性识别。

项目仍在**持续开发中**，欢迎 Star / Issue / PR。

## 功能特性

- **光谱导入**：CSV 数据导入与解析，实测谱库管理
- **预处理流水线**（`SpectrumAnalyzer.Core`）
  - 宇宙射线 / 毛刺去除（Modified Z-Score Despiking）
  - Savitzky–Golay 平滑
  - 基线校正（滚动分位数 / SNIP）
  - 强度归一化、低频区间裁剪
- **自动寻峰**：峰位 / 峰强 / 信噪比提取，支持算法参数自动调优
- **参考谱库匹配**：相似度评分 + HQI（Hit Quality Index）命中质量指数，匹配结果窗口展示
- **参考谱库与分类管理**：参考谱、实测谱、分类的本地管理
- **交互式谱图**：ScottPlot 谱图展示、谱图细节窗口
- **异步处理**：预处理支持异步与取消；算法参数可在设置窗口调整

## 技术栈

| 类别 | 技术 |
|---|---|
| 语言 / 框架 | C# · .NET Framework 4.7.2 |
| 界面 | WPF + MVVM |
| 绘图 | ScottPlot 4.1 (WPF) |
| 数据存储 | SQLite + Dapper |
| 数值计算 | MathNet.Numerics |
| 序列化 | Newtonsoft.Json |

## 项目结构

```
SpectrumAnalyzer.slnx
├── SpectrumAnalyzer           # WPF 主程序（主窗口、各功能窗口、ViewModel）
├── SpectrumAnalyzer.Core      # 核心算法：预处理、寻峰、相似度 / HQI 计算
├── SpectrumAnalyzer.Data      # 数据访问：SQLite + Dapper、仓储层
├── SpectrumAnalyzer.Services   # 业务服务：预处理编排、谱图数据管理
└── SpectrumAnalyzer.Tests     # 单元测试
```

## 构建与运行

**环境要求**

- Windows 10 / 11
- Visual Studio 2022（勾选“.NET 桌面开发”工作负载；`.slnx` 方案文件需 VS 2022 17.10+）
- NuGet 包还原（packages.config 模式，构建时自动还原）

**步骤**

1. 克隆仓库（或下载源码）
2. 用 Visual Studio 2022 打开 `SpectrumAnalyzer.slnx`
3. 生成解决方案（首次构建自动还原 NuGet 包）
4. 运行 `SpectrumAnalyzer` 项目

> 注意：本项目为 .NET Framework WPF 项目，XAML 标记编译依赖 Visual Studio 的完整 MSBuild，**`dotnet build` 无法直接构建**，请使用 Visual Studio 构建。

**数据库说明**：首次运行会在程序目录自动创建 SQLite 数据库文件 `RamanLibrary.db`（包含参考谱库与实测谱数据），该文件由运行时生成，不纳入版本控制。

## Roadmap

- [x] 基础分析：CSV 导入 / 预处理 / 寻峰 / 参考谱匹配
- [x] 参考谱库与分类管理
- [ ] 数据工程改造：统一 6 类标签体系、统一坐标网格、类别平衡、按类别分层划分训练 / 验证 / 测试集、导出可复现数据集（规划见 `docs/A-数据工程改造清单.md`）
- [ ] 监督学习升级：重建 KNN 基线，接入 PCA / SVM / PLS-DA 分类

## License

[MIT](LICENSE) · Copyright (c) 2026 CHEN1005-qh

## Contact

GitHub: [CHEN1005-qh](https://github.com/CHEN1005-qh)
