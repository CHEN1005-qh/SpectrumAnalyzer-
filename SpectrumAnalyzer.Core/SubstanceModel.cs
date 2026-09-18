using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectrumAnalyzer.Core
{
    // 对应 RamanSpectrum 表 (实测库)
    public class RamanSpectrumModel
    {
        // === 用于多谱同屏勾选状态绑定 ===
        public bool IsChecked { get; set; } = false;
        public int Id { get; set; }
        public string Name { get; set; }
        public string NameAuto { get; set; }
        public string AcquiredAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public int Points { get; set; }

        // === 核心修改：对接数据库中的 BLOB NOT NULL 类型 ===
        public byte[] X_nm { get; set; } = new byte[0]; // 纳米 X 轴二进制数据
        public byte[] Y_nm { get; set; } = new byte[0]; // 纳米 Y 轴二进制数据
        public byte[] X_cm { get; set; } = new byte[0]; // 波数 X 轴二进制数据
        public byte[] Y_cm { get; set; } = new byte[0]; // 强度 Y 轴二进制数据

        public string Note { get; set; }
        public double IntegrationTime { get; set; }
        public string Grating { get; set; }
        public double LaserPower { get; set; }
        public string ExcitationWavelength { get; set; }
        public string ProcessMethod { get; set; }

        // === 核心修改：用于在 UI 表格中安全渲染二进制大小 ===
        public string X_cm_Display => X_cm != null ? $"[{X_cm.Length} Bytes]" : "0 Bytes";
        public string Y_cm_Display => Y_cm != null ? $"[{Y_cm.Length} Bytes]" : "0 Bytes";
        public string Note_Display => Truncate(Note, 15);
        public string ProcessMethod_Display => Truncate(ProcessMethod, 15);

        private string Truncate(string val, int maxLength)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Length <= maxLength) return val;
            return val.Substring(0, maxLength) + "...";
        }
    }

    // 对应 ReferenceSpectrum 表 (标准库)
    public class ReferenceSpectrumModel
    {
        // === 用于多谱同屏勾选状态绑定 ===
        public bool IsChecked { get; set; } = false;
        public int Id { get; set; }
        public string SubstanceName { get; set; }
        public string ChemicalFormula { get; set; }
        public string CasNumber { get; set; }
        public string ExcitationWavelength { get; set; }
        public string Grating { get; set; }
        public string PeaksJson { get; set; } // 存储寻峰结果 [1332.1, 1580.4]
        public string Note { get; set; }
        public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 分类：NULL 表示未分类；对应 Category 表的 Id
        public int? CategoryId { get; set; }

        // 非数据库字段：列表展示用，显示分类路径（如 "材料 / 金属"），由 ViewModel 填充
        public string CategoryDisplay { get; set; }

        // 标准库对应为 TEXT 类型，故保留 string
        public string X_cm { get; set; } // 存储标准的 X 轴 JSON
        public string Y_cm { get; set; } // 存储标准的 Y 轴 JSON

        // === 用于在 UI 表格中安全截断显示的只读属性 ===
        public string PeaksJson_Display => Truncate(PeaksJson, 15);
        public string X_cm_Display => Truncate(X_cm, 15);
        public string Y_cm_Display => Truncate(Y_cm, 15);
        public string Note_Display => Truncate(Note, 15);

        private string Truncate(string val, int maxLength)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Length <= maxLength) return val;
            return val.Substring(0, maxLength) + "...";
        }
    }

    // 对应 Category 表 (标准库分类，自引用的 ParentId 实现树状结构)
    public class CategoryModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? ParentId { get; set; }   // NULL = 顶级分类
        public int SortOrder { get; set; }   // 同级排序
        public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 非数据库字段：供 UI 构建树/缩进展示使用
        public List<CategoryModel> Children { get; set; } = new List<CategoryModel>();
        public string DisplayPath { get; set; } // 如 "材料 -> 金属"
    }

    // --- 双引擎匹配结果模型 ---
    public class MatchResult
    {
        public string Name { get; set; }        // 物质名称
        public string Formula { get; set; }     // 化学式

        public double PeakScore { get; set; }   // 特征峰匹配得分 (0-100)
        public double HqiScore { get; set; }    // 全谱匹配得分 (0-100)

        // 综合得分
        public double CombinedScore => Math.Round((PeakScore + HqiScore) / 2.0, 1);

        public int HitCount { get; set; }       // 命中峰数
        public int TotalRefPeaks { get; set; }  // 标准库中该物质的总峰数
        public string PeaksJson { get; set; }   // 峰位信息 JSON

        public string PeakMatchRatio => $"{HitCount}/{TotalRefPeaks}";
    }
}