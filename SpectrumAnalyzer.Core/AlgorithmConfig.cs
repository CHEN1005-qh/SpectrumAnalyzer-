using System;

namespace SpectrumAnalyzer.Core
{
    public class AlgorithmConfig
    {
        // --- 1. 宇宙射线去除 (Despiking) ---
        private int _despikeWindow = 4;
        public bool EnableDespike { get; set; } = true;
        public int DespikeWindow
        {
            get => _despikeWindow;
            set => _despikeWindow = Math.Max(1, value);
        }

        private double _despikeZ = 8.0;
        public double DespikeZ
        {
            get => _despikeZ;
            set => _despikeZ = Math.Max(0.1, value);
        }

        // --- 2. Savitzky-Golay 平滑 (Smoothing) ---
        public bool EnableSmoothing { get; set; } = true;

        private int _sgWindowSize = 9;
        public int SgWindowSize
        {
            get => _sgWindowSize;
            set
            {
                int val = Math.Max(3, value);
                _sgWindowSize = (val % 2 == 0) ? val + 1 : val;
            }
        }

        private int _sgOrder = 3;
        public int SgOrder
        {
            get => _sgOrder;
            set
            {
                // 用 Min(Max) 替代 Clamp: 确保在 0 到 _sgWindowSize-1 之间
                int min = 0;
                int max = _sgWindowSize - 1;
                _sgOrder = Math.Min(max, Math.Max(min, value));
            }
        }

        // --- 3. 高级基线校正 (Quantile Baseline) ---
        public bool EnableBaseline { get; set; } = true;

        private int _baselineWindow = 151;

        // === 新增：基线算法选择 (默认为原有的 Quantile) ===
        public string BaselineMethod { get; set; } = "Quantile"; // "Quantile" 或 "Snip"

        // === 新增：是否裁剪 200 cm-1 以下的低频信号 ===
        public bool CropBelow200 { get; set; } = false;

        // === 新增：SNIP 算法的迭代次数 ===
        public int SnipIterations { get; set; } = 25;
        public int BaselineWindow
        {
            get => _baselineWindow;
            set => _baselineWindow = Math.Max(3, value);
        }

        private double _baselineQuantile = 0.01;
        public double BaselineQuantile
        {
            get => _baselineQuantile;
            set
            {
                // 确保在 0.0 到 1.0 之间
                _baselineQuantile = Math.Min(1.0, Math.Max(0.0, value));
            }
        }

        private int _baselineSmoothWindow = 81;
        public int BaselineSmoothWindow
        {
            get => _baselineSmoothWindow;
            set
            {
                int val = Math.Max(1, value);
                _baselineSmoothWindow = (val % 2 == 0) ? val + 1 : val;
            }
        }

        // --- 4. 寻峰控制 (Peak Detection) ---
        private double _minSnr = 5.0;
        public double MinSnr
        {
            get => _minSnr;
            set => _minSnr = Math.Max(0.1, value);
        }

        private double _minProminenceSnr = 2.0;
        public double MinProminenceSnr
        {
            get => _minProminenceSnr;
            set => _minProminenceSnr = Math.Max(0.1, value);
        }

        public double MinPeakDistanceX { get; set; } = 4.0;
        public int PeakWindow { get; set; } = 10;
        public bool EnableSubPixel { get; set; } = true;

        // --- 5. 噪声与局部控制 ---
        public bool UseLocalNoise { get; set; } = false;
        public int LocalNoiseWindow { get; set; } = 101;

        private double _minHeightFrac = 0.04;
        public double MinHeightFrac
        {
            get => _minHeightFrac;
            set
            {
                // 确保在 0.0 到 1.0 之间
                _minHeightFrac = Math.Min(1.0, Math.Max(0.0, value));
            }
        }

        // --- 6. 其他控制 ---
        public bool EnableNormalization { get; set; } = true;

        // 添加克隆方法：创建一个数值完全一样的新对象
        public AlgorithmConfig Clone()
        {
            return (AlgorithmConfig)this.MemberwiseClone();
        }
    }
}