using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace SpectrumAnalyzer
{
    public class SpectralSample
    {
        // 可选的来源ID（例如参考库的数据库主键）
        public int? Id { get; set; }
        public string Label { get; set; }
        public double[] Spectrum { get; set; }
    }

    public class KnnResult
    {
        public string PredictedClass { get; set; }
        public double Confidence { get; set; }
        // 返回参与投票的邻居列表（按距离升序）
        public List<Neighbor> Neighbors { get; set; } = new List<Neighbor>();
    }

    public class Neighbor
    {
        public string Label { get; set; }
        public int? SourceId { get; set; }
        public double Distance { get; set; }
        public double Weight { get; set; }
    }

    public class KnnClassifier
    {
        private readonly int _k;
        private readonly int _pcaComponents;

        private Matrix<double> _projectionMatrix;
        private Vector<double> _meanVector;
        private List<PcaProjectedSample> _trainedSamples;

        private class PcaProjectedSample
        {
            public string Label { get; set; }
            public Vector<double> ProjectedFeatures { get; set; }
            public int? SourceId { get; set; }
        }

        public KnnClassifier(int k = 3, int pcaComponents = 5)
        {
            _k = k;
            _pcaComponents = pcaComponents;
            _trainedSamples = new List<PcaProjectedSample>();
        }

        /// <summary>
        /// 对单条光谱进行标准正态变量变换 (SNV)
        /// </summary>
        private double[] ApplySnv(double[] rawSpectrum)
        {
            double mean = rawSpectrum.Average();
            double sumOfSquares = rawSpectrum.Sum(val => Math.Pow(val - mean, 2));
            double stdDev = Math.Sqrt(sumOfSquares / rawSpectrum.Length);

            if (stdDev < 1e-8) return rawSpectrum;

            double[] snv = new double[rawSpectrum.Length];
            for (int i = 0; i < rawSpectrum.Length; i++)
            {
                snv[i] = (rawSpectrum[i] - mean) / stdDev;
            }
            return snv;
        }

        /// <summary>
        /// 训练分类器：计算PCA投影矩阵并转换训练样本
        /// </summary>
        public void Train(List<SpectralSample> trainingSamples)
        {
            if (trainingSamples == null || trainingSamples.Count == 0)
                throw new ArgumentException("训练数据集不可为空。");

            int numSamples = trainingSamples.Count;
            int numFeatures = trainingSamples[0].Spectrum.Length;

            var matrixData = new double[numSamples][];
            for (int i = 0; i < numSamples; i++)
            {
                matrixData[i] = ApplySnv(trainingSamples[i].Spectrum);
            }

            var rawMatrix = Matrix<double>.Build.DenseOfRowArrays(matrixData);

            var meanArray = new double[numFeatures];
            for (int j = 0; j < numFeatures; j++)
            {
                meanArray[j] = rawMatrix.Column(j).Average();
            }
            _meanVector = Vector<double>.Build.Dense(meanArray);

            var meanCenteredMatrix = Matrix<double>.Build.Dense(numSamples, numFeatures);
            for (int i = 0; i < numSamples; i++)
            {
                meanCenteredMatrix.SetRow(i, rawMatrix.Row(i) - _meanVector);
            }

            var svd = meanCenteredMatrix.Svd(computeVectors: true);
            var vMatrix = svd.VT.Transpose();

            int actualComponents = Math.Min(_pcaComponents, Math.Min(numSamples - 1, numFeatures));
            if (actualComponents < 1) actualComponents = 1;

            _projectionMatrix = vMatrix.SubMatrix(0, numFeatures, 0, actualComponents);

            _trainedSamples.Clear();
            var projectedMatrix = meanCenteredMatrix * _projectionMatrix;

            for (int i = 0; i < numSamples; i++)
            {
                _trainedSamples.Add(new PcaProjectedSample
                {
                    Label = trainingSamples[i].Label,
                    ProjectedFeatures = projectedMatrix.Row(i),
                    SourceId = trainingSamples[i].Id
                });
            }
        }

        /// <summary>
        /// 预测未知光谱物相归属 (实例方法，返回KnnResult)
        /// </summary>
        public KnnResult Predict(double[] rawSpectrum)
        {
            // 复用底层实现并返回包含邻居信息的结果
            string predictedClass = Predict(rawSpectrum, out double confidence, out List<Neighbor> neighbors);
            return new KnnResult
            {
                PredictedClass = predictedClass,
                Confidence = confidence,
                Neighbors = neighbors
            };
        }

        /// <summary>
        /// 预测未知光谱物相归属 (实例方法)
        /// </summary>
        public string Predict(double[] rawSpectrum, out double confidence)
        {
            // 旧方法保持向后兼容，调用新的内部实现并忽略 neighbors 输出
            return Predict(rawSpectrum, out confidence, out _);
        }

        /// <summary>
        /// 扩展预测方法：同时返回邻居列表（含距离与权重）
        /// </summary>
        public string Predict(double[] rawSpectrum, out double confidence, out List<Neighbor> neighbors)
        {
            neighbors = new List<Neighbor>();
            confidence = 0.0;
            if (_trainedSamples == null || _trainedSamples.Count == 0 || _projectionMatrix == null)
            {
                throw new InvalidOperationException("分类器尚未完成初始化训练。");
            }

            double[] snvSpectrum = ApplySnv(rawSpectrum);
            var testVector = Vector<double>.Build.Dense(snvSpectrum);
            var centeredVector = testVector - _meanVector;

            var projectedVector = centeredVector * _projectionMatrix;

            var distances = new List<(PcaProjectedSample Sample, double Distance)>();
            foreach (var trainSample in _trainedSamples)
            {
                double dist = CalculateCosineDistance(projectedVector, trainSample.ProjectedFeatures);
                distances.Add((trainSample, dist));
            }

            var kNearest = distances
                .OrderBy(d => d.Distance)
                .Take(Math.Min(_k, distances.Count))
                .ToList();

            if (kNearest.Count == 0) return "Unknown";

            var voteWeights = new Dictionary<string, double>();
            double totalWeight = 0.0;
            double epsilon = 1e-6;

            foreach (var neighbor in kNearest)
            {
                double weight = 1.0 / (neighbor.Distance + epsilon);
                if (voteWeights.ContainsKey(neighbor.Sample.Label))
                {
                    voteWeights[neighbor.Sample.Label] += weight;
                }
                else
                {
                    voteWeights[neighbor.Sample.Label] = weight;
                }
                totalWeight += weight;

                neighbors.Add(new Neighbor
                {
                    Label = neighbor.Sample.Label,
                    SourceId = neighbor.Sample.SourceId,
                    Distance = neighbor.Distance,
                    Weight = weight
                });
            }

            var winner = voteWeights.OrderByDescending(kv => kv.Value).First();
            confidence = winner.Value / totalWeight;

            // 按距离升序返回邻居（便于上层显示）
            neighbors = neighbors.OrderBy(n => n.Distance).ToList();

            return winner.Key;
        }

        private double CalculateCosineDistance(Vector<double> v1, Vector<double> v2)
        {
            double dotProduct = v1.DotProduct(v2);
            double normV1 = v1.L2Norm();
            double normV2 = v2.L2Norm();

            if (normV1 < 1e-8 || normV2 < 1e-8) return 1.0;

            double similarity = dotProduct / (normV1 * normV2);
            similarity = Math.Max(-1.0, Math.Min(1.0, similarity));

            return 1.0 - similarity;
        }

        /// <summary>
        /// 线性插值重采样
        /// </summary>
        private static double[] Resample(double[] sourceX, double[] sourceY, double[] targetX)
        {
            double[] resampledY = new double[targetX.Length];
            for (int i = 0; i < targetX.Length; i++)
            {
                double tx = targetX[i];
                if (tx <= sourceX[0])
                {
                    resampledY[i] = sourceY[0];
                }
                else if (tx >= sourceX[sourceX.Length - 1])
                {
                    resampledY[i] = sourceY[sourceY.Length - 1];
                }
                else
                {
                    int idx = Array.BinarySearch(sourceX, tx);
                    if (idx >= 0)
                    {
                        resampledY[i] = sourceY[idx];
                    }
                    else
                    {
                        int nextIdx = ~idx;
                        int prevIdx = nextIdx - 1;
                        double x0 = sourceX[prevIdx];
                        double x1 = sourceX[nextIdx];
                        double y0 = sourceY[prevIdx];
                        double y1 = sourceY[nextIdx];
                        resampledY[i] = y0 + (tx - x0) * (y1 - y0) / (x1 - x0);
                    }
                }
            }
            return resampledY;
        }

        #region 静态预测与留一法交叉验证(LOOCV)评估引擎

        /// <summary>
        /// 静态预测方法，保持 MainWindow 兼容性
        /// </summary>
        public static KnnResult Predict(double[] targetX, double[] targetY, List<ReferenceSpectrumModel> library, int k)
        {
            if (targetX == null || targetY == null || library == null || library.Count == 0)
                throw new ArgumentException("输入数据或标准库为空。");

            var trainSamples = new List<SpectralSample>();
            foreach (var item in library)
            {
                if (string.IsNullOrEmpty(item.X_cm) || string.IsNullOrEmpty(item.Y_cm)) continue;
                try
                {
                    double[] refX = Newtonsoft.Json.JsonConvert.DeserializeObject<double[]>(item.X_cm);
                    double[] refY = Newtonsoft.Json.JsonConvert.DeserializeObject<double[]>(item.Y_cm);
                    if (refX == null || refY == null || refX.Length < 2) continue;

                    double[] alignedY = Resample(refX, refY, targetX);
                    trainSamples.Add(new SpectralSample
                    {
                        Label = item.SubstanceName,
                        Spectrum = alignedY
                    });
                }
                catch { }
            }

            if (trainSamples.Count == 0)
                throw new InvalidOperationException("参考库中无可供比对的拉曼全谱数据。");

            int pcaComps = Math.Min(5, trainSamples.Count - 1);
            if (pcaComps < 1) pcaComps = 1;

            var classifier = new KnnClassifier(k: k, pcaComponents: pcaComps);
            classifier.Train(trainSamples);

            string predicted = classifier.Predict(targetY, out double confidence);
            return new KnnResult
            {
                PredictedClass = predicted,
                Confidence = confidence
            };
        }

        /// <summary>
        /// 留一法交叉验证 (LOOCV) 跑分引擎
        /// 完全使用实测数据库 testSet 自作为数据集，训练和测试内部闭环，排除 referenceSet 人工标准库干预。
        /// </summary>
        public static string RunIndependentValidation(
            System.Collections.IEnumerable testSet,       // 实测数据集（LOOCV唯一数据源）
            string csvPath,
            object currentConfig)
        {
            if (testSet == null)
            {
                return "评估失败：实测数据集为空。";
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 1. 提取实测数据集中的所有光谱元数据
            var allRawSamples = new List<(string Label, double[] XData, double[] YData)>();
            foreach (var item in testSet)
            {
                var (label, xData, yData) = ExtractLabelAndSpectrum(item);
                if (xData != null && yData != null && xData.Length > 1)
                {
                    allRawSamples.Add((label, xData, yData));
                }
            }

            int n = allRawSamples.Count;
            if (n < 3)
            {
                return $"评估失败：实测样本量不足（N = {n}），无法执行留一法交叉验证。";
            }

            // 2. 建立统一工作基准轴 (使用第一条实测光谱作为 commonX)
            double[] commonX = allRawSamples[0].XData;

            // 3. 对所有实测样本在内存中应用当前物理预处理（去噪、SG平滑、基线扣除）并重采样对齐
            var preprocessedSamples = new List<SpectralSample>();
            for (int i = 0; i < n; i++)
            {
                var sample = allRawSamples[i];
                double[] preprocessedY = PreprocessSpectrum(sample.XData, sample.YData, currentConfig);
                double[] alignedY = Resample(sample.XData, preprocessedY, commonX);

                preprocessedSamples.Add(new SpectralSample
                {
                    Label = sample.Label,
                    Spectrum = alignedY
                });
            }

            // 4. 执行留一法交叉验证 (Leave-One-Out Cross-Validation Loop)
            int correctCount = 0;
            var csvLines = new List<string> { "Index,ActualLabel,PredictedLabel,Confidence,IsCorrect" };

            for (int i = 0; i < n; i++)
            {
                // 4.1 抽出第 i 个样本作为测试验证集 (Validation Set)
                var testSample = preprocessedSamples[i];

                // 4.2 剩下的 N-1 个实测样本作为当前轮次的训练集 (Training Set)
                var trainingSamples = new List<SpectralSample>();
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    trainingSamples.Add(preprocessedSamples[j]);
                }

                // 4.3 构建临时分类器并进行 PCA 特征空间训练 (K=3)
                int pcaComps = Math.Min(5, trainingSamples.Count - 1);
                if (pcaComps < 1) pcaComps = 1;

                var classifier = new KnnClassifier(k: 3, pcaComponents: pcaComps);
                try
                {
                    classifier.Train(trainingSamples);
                }
                catch (Exception ex)
                {
                    return $"在交叉迭代第 {i + 1} 轮训练模型失败: {ex.Message}";
                }

                // 4.4 预测抽出的测试样本并输出判定置信度
                string predictedLabel = classifier.Predict(testSample.Spectrum, out double confidence);

                // 4.5 评判判定正确性 (中英文及子串模糊比对)
                bool isCorrect = IsLabelMatch(testSample.Label, predictedLabel);
                if (isCorrect) correctCount++;

                csvLines.Add($"{i + 1},{testSample.Label},{predictedLabel},{confidence:F4},{(isCorrect ? "1" : "0")}");
            }

            stopwatch.Stop();
            double elapsedTimeMs = stopwatch.ElapsedMilliseconds;

            // 5. 写入本地 CSV 独立跑分报告
            try
            {
                File.WriteAllLines(csvPath, csvLines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return $"警告：LOOCV交叉验证已完成，但 CSV 文件写入失败: {ex.Message}";
            }

            // 6. 生成学术级自验证汇总报告
            double accuracy = n > 0 ? (double)correctCount / n * 100.0 : 0.0;

            StringBuilder report = new StringBuilder();
            report.AppendLine("=== 实测数据库留一法交叉验证 (LOOCV) 学术评估报告 ===");
            report.AppendLine($"数据校准声明 : 本次评估完全基于实测光谱数据库，已排除人工录入标准库干预。");
            report.AppendLine($"执行时刻     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"样本总数 (N) : {n} 个（互为测试集与训练集迭代）");
            report.AppendLine($"正确归类数量 : {correctCount} 个");
            report.AppendLine($"内部分类准确率 (LOOCV Accuracy) : {accuracy:F2}%");
            report.AppendLine($"算法累积交叉计算耗时             : {elapsedTimeMs:F1} ms");
            report.AppendLine($"单次测试迭代平均耗时             : {(n > 0 ? elapsedTimeMs / n : 0):F3} ms");
            report.AppendLine($"跑分详细混淆表保存路径           : {csvPath}");
            report.AppendLine("=====================================================");

            return report.ToString();
        }

        /// <summary>
        /// 利用反射机制兼容不同实体类的光谱提取且自动解码 JSON 数据 (解耦 SubstanceModel)
        /// </summary>
        private static (string Label, double[] XData, double[] YData) ExtractLabelAndSpectrum(object item)
        {
            if (item == null) return (null, null, null);
            var type = item.GetType();

            string label = "Unknown";
            var labelProp = type.GetProperty("SubstanceName") ?? type.GetProperty("Name") ?? type.GetProperty("Label");
            if (labelProp != null)
            {
                label = labelProp.GetValue(item)?.ToString() ?? "Unknown";
            }

            double[] xData = null;
            var xProp = type.GetProperty("X_cm") ?? type.GetProperty("XData") ?? type.GetProperty("SpectrumX");
            if (xProp != null)
            {
                var val = xProp.GetValue(item);
                if (val is string jsonStr)
                {
                    try { xData = Newtonsoft.Json.JsonConvert.DeserializeObject<double[]>(jsonStr); } catch { }
                }
                else if (val is double[] arr) xData = arr;
                else if (val is List<double> list) xData = list.ToArray();
            }

            double[] yData = null;
            var yProp = type.GetProperty("Y_cm") ?? type.GetProperty("YData") ?? type.GetProperty("SpectrumY") ?? type.GetProperty("YRaw") ?? type.GetProperty("Spectrum");
            if (yProp != null)
            {
                var val = yProp.GetValue(item);
                if (val is string jsonStr)
                {
                    try { yData = Newtonsoft.Json.JsonConvert.DeserializeObject<double[]>(jsonStr); } catch { }
                }
                else if (val is double[] arr) yData = arr;
                else if (val is List<double> list) yData = list.ToArray();
            }

            return (label, xData, yData);
        }

        /// <summary>
        /// 动态调用底层的 PreprocessingService 去除测试集基线及噪声干扰
        /// </summary>
        private static double[] PreprocessSpectrum(double[] xs, double[] ys, object config)
        {
            if (config == null) return ys;
            double[] processedY = (double[])ys.Clone();

            try
            {
                var configType = config.GetType();

                bool enableDespike = (bool)(configType.GetProperty("EnableDespike")?.GetValue(config) ?? false);
                int despikeWindow = (int)(configType.GetProperty("DespikeWindow")?.GetValue(config) ?? 15);
                double despikeZ = Convert.ToDouble(configType.GetProperty("DespikeZ")?.GetValue(config) ?? 6.0);

                bool enableSmoothing = (bool)(configType.GetProperty("EnableSmoothing")?.GetValue(config) ?? false);
                int sgWindowSize = (int)(configType.GetProperty("SgWindowSize")?.GetValue(config) ?? 15);
                int sgOrder = (int)(configType.GetProperty("SgOrder")?.GetValue(config) ?? 2);

                bool enableBaseline = (bool)(configType.GetProperty("EnableBaseline")?.GetValue(config) ?? false);
                string baselineMethod = configType.GetProperty("BaselineMethod")?.GetValue(config)?.ToString() ?? "Snip";
                int snipIterations = (int)(configType.GetProperty("SnipIterations")?.GetValue(config) ?? 50);
                int baselineWindow = (int)(configType.GetProperty("BaselineWindow")?.GetValue(config) ?? 50);
                double baselineQuantile = Convert.ToDouble(configType.GetProperty("BaselineQuantile")?.GetValue(config) ?? 0.05);
                int baselineSmoothWindow = (int)(configType.GetProperty("BaselineSmoothWindow")?.GetValue(config) ?? 15);

                bool enableNormalization = (bool)(configType.GetProperty("EnableNormalization")?.GetValue(config) ?? false);

                var prepType = Type.GetType("SpectrumAnalyzer.PreprocessingService") ??
                               AppDomain.CurrentDomain.GetAssemblies()
                                        .SelectMany(a => a.GetTypes())
                                        .FirstOrDefault(t => t.FullName == "SpectrumAnalyzer.PreprocessingService");

                if (prepType != null)
                {
                    if (enableDespike)
                    {
                        var method = prepType.GetMethod("RemoveSpikes");
                        if (method != null) processedY = (double[])method.Invoke(null, new object[] { processedY, despikeWindow, despikeZ });
                    }

                    if (enableSmoothing)
                    {
                        var method = prepType.GetMethod("SavitzkyGolaySmooth");
                        if (method != null) processedY = (double[])method.Invoke(null, new object[] { processedY, sgWindowSize, sgOrder });
                    }

                    if (enableBaseline)
                    {
                        double[] baseline = null;
                        if (baselineMethod == "Snip")
                        {
                            var method = prepType.GetMethod("SnipBaseline");
                            if (method != null) baseline = (double[])method.Invoke(null, new object[] { processedY, snipIterations });
                        }
                        else
                        {
                            var methodQuantile = prepType.GetMethod("QuantileBaseline");
                            var methodSmooth = prepType.GetMethod("SavitzkyGolaySmooth");
                            if (methodQuantile != null && methodSmooth != null)
                            {
                                baseline = (double[])methodQuantile.Invoke(null, new object[] { processedY, baselineWindow, baselineQuantile });
                                baseline = (double[])methodSmooth.Invoke(null, new object[] { baseline, baselineSmoothWindow, 2 });
                            }
                        }

                        if (baseline != null)
                        {
                            for (int i = 0; i < processedY.Length; i++)
                            {
                                processedY[i] = Math.Max(0, processedY[i] - baseline[i]);
                            }
                        }
                    }

                    if (enableNormalization)
                    {
                        var method = prepType.GetMethod("Normalize");
                        if (method != null) processedY = (double[])method.Invoke(null, new object[] { processedY });
                    }
                }
            }
            catch
            {
                // 反射降级
            }

            return processedY;
        }

        private static bool IsLabelMatch(string actual, string predicted)
        {
            if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(predicted)) return false;

            string a = actual.Trim().ToLower();
            string p = predicted.Trim().ToLower();

            return a == p || a.Contains(p) || p.Contains(a);
        }

        #endregion
    }
}