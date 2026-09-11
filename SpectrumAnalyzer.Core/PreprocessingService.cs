using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;

namespace SpectrumAnalyzer
{
    public class PeakInfo
    {
        public double X { get; set; }      // 峰位 (cm-1)
        public double Y { get; set; }      // 峰强
        public int Index { get; set; }     // 原始索引
        public double Snr { get; set; }    // 该峰的信噪比
    }

    public static class PreprocessingService
    {
        // --- 1. 宇宙射线/毛刺去除 (Modified Z-Score Despiking) ---
        public static double[] RemoveSpikes(double[] y, int window, double zThreshold)
        {
            if (y == null || y.Length < window * 2 + 1) return y;
            double[] result = (double[])y.Clone();

            for (int i = window; i < y.Length - window; i++)
            {
                double[] segment = new double[window * 2 + 1];
                Array.Copy(y, i - window, segment, 0, segment.Length);

                double median = Statistics.Median(segment);

                double[] absoluteDeviations = segment.Select(v => Math.Abs(v - median)).ToArray();
                double mad = Statistics.Median(absoluteDeviations);

                double zScore = 0.6745 * Math.Abs(y[i] - median) / (mad + 1e-9);

                if (zScore > zThreshold)
                {
                    result[i] = median;
                }
            }
            return result;
        }

        // --- 2. Savitzky-Golay 平滑 ---
        public static double[] SavitzkyGolaySmooth(double[] data, int windowSize, int order)
        {
            if (data == null || data.Length < windowSize) return data;

            int n = (windowSize - 1) / 2;
            var matrixA = Matrix<double>.Build.Dense(windowSize, order + 1, (i, j) => Math.Pow(i - n, j));
            var matrixC = matrixA.PseudoInverse();
            var coefficients = matrixC.Row(0).ToArray();

            double[] smoothed = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                double sum = 0;
                for (int j = -n; j <= n; j++)
                {
                    int idx = Math.Min(data.Length - 1, Math.Max(0, i + j));
                    sum += data[idx] * coefficients[j + n];
                }
                smoothed[i] = sum;
            }
            return smoothed;
        }

        // --- 3. 滚动分位数基线校正 (Quantile Rolling Baseline) ---
        public static double[] QuantileBaseline(double[] y, int window, double quantile)
        {
            if (y == null || y.Length < window) return new double[y.Length];
            double[] baseline = new double[y.Length];

            for (int i = 0; i < y.Length; i++)
            {
                int start = Math.Max(0, i - window / 2);
                int end = Math.Min(y.Length - 1, i + window / 2);
                int count = end - start + 1;

                double[] segment = new double[count];
                Array.Copy(y, start, segment, 0, count);

                baseline[i] = Statistics.Quantile(segment, quantile);
            }
            return baseline;
        }

        public static double[] SnipBaseline(double[] y, int maxIterations)
        {
            if (y == null || y.Length < 3) return new double[y.Length];

            int n = y.Length;
            double[] baseline = (double[])y.Clone();

            for (int i = 1; i <= maxIterations; i++)
            {
                double[] temp = (double[])baseline.Clone();
                for (int j = i; j < n - i; j++)
                {
                    double localBg = (baseline[j - i] + baseline[j + i]) / 2.0;
                    if (temp[j] > localBg)
                    {
                        temp[j] = localBg;
                    }
                }
                baseline = temp;
            }

            return baseline;
        }

        // --- 4. 归一化 ---
        public static double[] Normalize(double[] y)
        {
            double min = y.Min();
            double max = y.Max();
            double range = max - min;

            if (range < 1e-9) return y;

            return y.Select(v => (v - min) / range).ToArray();
        }

        // --- 5. 专业寻峰 (Multi-Criteria Peak Finding) ---
        public static List<PeakInfo> FindPeaksAdvanced(double[] x, double[] y, AlgorithmConfig config)
        {
            List<PeakInfo> peaks = new List<PeakInfo>();
            if (y == null || y.Length < config.PeakWindow * 2) return peaks;

            double globalStd = 1.4826 * Statistics.Median(y.Select(v => Math.Abs(v - Statistics.Median(y))).ToArray());

            double p95 = Statistics.Percentile(y, 95);
            double heightThreshold = p95 * config.MinHeightFrac;

            int window = config.PeakWindow;
            for (int i = window; i < y.Length - window; i++)
            {
                bool isLocalMax = true;
                for (int j = i - window; j <= i + window; j++)
                {
                    if (y[j] > y[i]) { isLocalMax = false; break; }
                }

                if (isLocalMax && y[i] > heightThreshold)
                {
                    double snr = y[i] / (globalStd + 1e-9);
                    if (snr < config.MinSnr) continue;

                    PeakInfo peak = new PeakInfo { Snr = snr, Index = i };

                    if (config.EnableSubPixel && i > 0 && i < y.Length - 1)
                    {
                        double y1 = y[i - 1], y2 = y[i], y3 = y[i + 1];
                        double x1 = x[i - 1], x2 = x[i], x3 = x[i + 1];
                        double denom = (y1 - 2 * y2 + y3);

                        if (Math.Abs(denom) > 1e-9)
                        {
                            double offset = 0.5 * (y1 - y3) / denom;
                            peak.X = x2 + offset * (x2 - x1);

                            double estimatedY = y2 - 0.125 * Math.Pow(y3 - y1, 2) / denom;

                            if (config.EnableNormalization)
                            {
                                peak.Y = Math.Max(0, Math.Min(1.0, estimatedY));
                            }
                            else
                            {
                                peak.Y = Math.Max(0, estimatedY);
                            }
                        }
                        else
                        {
                            peak.X = x[i];
                            peak.Y = y[i];
                        }
                    }
                    else
                    {
                        peak.X = x[i];
                        peak.Y = y[i];
                    }

                    if (peaks.Count > 0 && (peak.X - peaks.Last().X) < config.MinPeakDistanceX)
                    {
                        if (peak.Y > peaks.Last().Y)
                            peaks.RemoveAt(peaks.Count - 1);
                        else
                            continue;
                    }

                    peaks.Add(peak);
                    i += window;
                }
            }
            return peaks;
        }

        // --- 6. 参数自动调优 ---
        public static void AutoTuneParameters(double[] y, AlgorithmConfig config)
        {
            if (y == null || y.Length < 50) return;

            double[] diffs = new double[y.Length - 1];
            for (int i = 0; i < y.Length - 1; i++) diffs[i] = y[i + 1] - y[i];
            double noiseStd = Statistics.StandardDeviation(diffs) / Math.Sqrt(2);

            double maxVal = y.Max();
            double minVal = y.Min();
            double range = maxVal - minVal;

            double snrMetric = range / (noiseStd + 1e-9);

            if (snrMetric > 150)
            {
                config.SgWindowSize = 7;
                config.SgOrder = 3;
            }
            else
            {
                config.SgWindowSize = 11;
                config.SgOrder = 2;
            }

            if (snrMetric > 100)
            {
                config.MinSnr = 2.8;
            }
            else
            {
                config.MinSnr = 1.8;
            }

            config.MinHeightFrac = 0.005;
            config.MinPeakDistanceX = 3.0;
            config.DespikeZ = 6.0;
        }

        // --- 7. 特征峰相似度计算 (保留原峰位比对) ---
        public static (double Score, int HitCount) CalculateSimilarity(
    List<PeakInfo> samplePeaks,
    List<dynamic> refPeaks,
    double tolerance = 8.0,
    bool enforcePureComponent = false) // === 新增：默认为 false，保证向后兼容 ===
        {
            if (refPeaks == null || refPeaks.Count == 0 || samplePeaks.Count == 0) return (0, 0);

            double weightedHits = 0;
            double totalRefWeight = 0;

            foreach (var rp in refPeaks)
            {
                double intensity = rp.I ?? 1.0;
                totalRefWeight += intensity;
            }

            int hitCount = 0;

            // 建立哈希集合，用于追踪哪些实测峰已被标准库成功“认领”
            HashSet<PeakInfo> matchedSamplePeaks = new HashSet<PeakInfo>();

            foreach (var rp in refPeaks)
            {
                double refW = (double)rp.W;
                double refI = (double)(rp.I ?? 1.0);

                var matchedPeak = samplePeaks
                    .Where(sp => Math.Abs(sp.X - refW) <= tolerance)
                    .OrderBy(sp => Math.Abs(sp.X - refW))
                    .FirstOrDefault();

                if (matchedPeak != null)
                {
                    hitCount++;
                    matchedSamplePeaks.Add(matchedPeak); // 记录已被认领的峰

                    double peakScore = 1.0;
                    double intensityDiff = Math.Abs(matchedPeak.Y - refI);
                    double intensityFactor = Math.Max(0, 1.0 - intensityDiff);

                    weightedHits += (0.7 * peakScore + 0.3 * intensityFactor) * refI;
                }
            }

            double finalScore = (weightedHits / totalRefWeight) * 100;

            // ==========================================================
            // === 🚀 核心逻辑升级：条件性激活“未匹配强特征峰惩罚机制” ===
            // ==========================================================
            if (enforcePureComponent)
            {
                // 找出实测谱中，那些没有被标准谱认领、且归一化后强度依然极强的主特征峰（Y > 0.35）
                var unmatchedStrongPeaks = samplePeaks
                    .Where(sp => !matchedSamplePeaks.Contains(sp) && sp.Y > 0.35)
                    .ToList();

                if (unmatchedStrongPeaks.Count > 0)
                {
                    // 每一个多出来的、未命中的强特征峰，都会按比例削减最终分数（例如每个扣 15%）
                    double penaltyFactor = 1.0 - (unmatchedStrongPeaks.Count * 0.15);
                    penaltyFactor = Math.Max(0.15, penaltyFactor); // 设定惩罚下限，防止扣到负数

                    finalScore *= penaltyFactor;
                }
            }
            // ==========================================================

            if (samplePeaks.Count > refPeaks.Count * 2)
                finalScore *= 0.9;

            finalScore = Math.Min(100, finalScore);

            return (finalScore, hitCount);
        }

        // --- 8. 全谱相关性匹配 (HQI - 夹角余弦相似度) ---
        public static double CalculateHQI(double[] sampleX, double[] sampleY, double[] refX, double[] refY)
        {
            if (sampleX == null || sampleY == null || refX == null || refY == null) return 0;
            if (sampleX.Length < 10 || refX.Length < 10) return 0;

            try
            {
                // 1. 获取交集范围，防止插值算法在边界产生外推异常
                double minX = Math.Max(sampleX.Min(), refX.Min());
                double maxX = Math.Min(sampleX.Max(), refX.Max());

                if (minX >= maxX || (maxX - minX) < 10.0) return 0;

                // 2. 提取在这个范围内的实测光谱数据点
                List<double> targetXList = new List<double>();
                List<double> targetYList = new List<double>();
                for (int i = 0; i < sampleX.Length; i++)
                {
                    if (sampleX[i] >= minX && sampleX[i] <= maxX)
                    {
                        targetXList.Add(sampleX[i]);
                        targetYList.Add(sampleY[i]);
                    }
                }

                if (targetXList.Count < 10) return 0;

                // 3. 对参考光谱数据排序并滤除重复的横坐标点
                var refSortedIndices = Enumerable.Range(0, refX.Length).OrderBy(idx => refX[idx]).ToArray();
                double[] sortedRefX = refSortedIndices.Select(idx => refX[idx]).ToArray();
                double[] sortedRefY = refSortedIndices.Select(idx => refY[idx]).ToArray();

                List<double> uniqueRefX = new List<double>();
                List<double> uniqueRefY = new List<double>();
                for (int i = 0; i < sortedRefX.Length; i++)
                {
                    if (i == 0 || Math.Abs(sortedRefX[i] - sortedRefX[i - 1]) > 1e-5)
                    {
                        uniqueRefX.Add(sortedRefX[i]);
                        uniqueRefY.Add(sortedRefY[i]);
                    }
                }

                if (uniqueRefX.Count < 2) return 0;

                // 4. 创建一维线性插值计算模型
                var interpolator = MathNet.Numerics.Interpolation.LinearSpline.InterpolateSorted(uniqueRefX.ToArray(), uniqueRefY.ToArray());

                // 5. 对标准谱线重采样，使其横坐标网格对齐到当前实测的坐标集上
                double[] alignedSampleY = targetYList.ToArray();
                double[] alignedRefY = new double[targetXList.Count];
                for (int i = 0; i < targetXList.Count; i++)
                {
                    alignedRefY[i] = Math.Max(0.0, interpolator.Interpolate(targetXList[i]));
                }

                // 6. 余弦夹角公式计算相关系数
                double dotProduct = 0;
                double normSample = 0;
                double normRef = 0;

                for (int i = 0; i < alignedSampleY.Length; i++)
                {
                    dotProduct += alignedSampleY[i] * alignedRefY[i];
                    normSample += alignedSampleY[i] * alignedSampleY[i];
                    normRef += alignedRefY[i] * alignedRefY[i];
                }

                if (normSample <= 1e-9 || normRef <= 1e-9) return 0;

                double hqi = (dotProduct / (Math.Sqrt(normSample) * Math.Sqrt(normRef))) * 100.0;
                return Math.Min(100.0, Math.Max(0.0, hqi));
            }
            catch
            {
                return 0;
            }
        }
    }
}