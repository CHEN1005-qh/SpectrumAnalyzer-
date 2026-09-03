using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SpectrumAnalyzer.Services
{
    /// <summary>
    /// 光谱匹配和分类服务
    /// 负责KNN分类、结果解析等
    /// </summary>
    public interface ISpectrumMatchingService
    {
        /// <summary>
        /// 执行光谱匹配分类
        /// </summary>
        MatchingResult Match(double[] testSpectrum, int k = 3, int pcaComponents = 5);

        /// <summary>
        /// 批量分类
        /// </summary>
        List<MatchingResult> MatchBatch(List<double[]> testSpectra, int k = 3, int pcaComponents = 5);
    }

    /// <summary>
    /// 光谱匹配和分类服务实现
    /// </summary>
    public class SpectrumMatchingService : ISpectrumMatchingService
    {
        private readonly DatabaseService _databaseService;
        private readonly KnnClassifier _classifier;

        public SpectrumMatchingService(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _classifier = new KnnClassifier(k: 3, pcaComponents: 5);

            // 初始化分类器：加载参考库数据
            InitializeClassifier();
        }

        /// <summary>
        /// 初始化分类器 - 加载标准光谱库
        /// </summary>
        private void InitializeClassifier()
        {
            try
            {
                var referenceSpectra = _databaseService.GetReferenceLibrary();
                var trainingSamples = new List<SpectralSample>();

                foreach (var refSpectrum in referenceSpectra)
                {
                    try
                    {
                        // 从JSON反序列化光谱数据
                        double[] xData = JsonConvert.DeserializeObject<double[]>(refSpectrum.X_cm);
                        double[] yData = JsonConvert.DeserializeObject<double[]>(refSpectrum.Y_cm);

                        if (xData != null && yData != null && yData.Length > 0)
                        {
                            var sample = new SpectralSample
                            {
                                Id = refSpectrum.Id,
                                Label = refSpectrum.SubstanceName,
                                Spectrum = yData
                            };
                            trainingSamples.Add(sample);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"加载参考光谱 {refSpectrum.SubstanceName} 失败: {ex.Message}");
                    }
                }

                if (trainingSamples.Count > 0)
                {
                    _classifier.Train(trainingSamples);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化分类器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行光谱匹配分类
        /// </summary>
        public MatchingResult Match(double[] testSpectrum, int k = 3, int pcaComponents = 5)
        {
            try
            {
                if (testSpectrum == null || testSpectrum.Length == 0)
                    return new MatchingResult { Success = false, Message = "测试光谱为空" };

                var knnResult = _classifier.Predict(testSpectrum);

                if (knnResult == null)
                    return new MatchingResult { Success = false, Message = "分类失败" };

                // 构建候选匹配列表
                var candidates = new List<CandidateMatch>();
                if (knnResult.Neighbors != null && knnResult.Neighbors.Count > 0)
                {
                    double totalWeight = knnResult.Neighbors.Sum(n => n.Weight);
                    foreach (var nb in knnResult.Neighbors)
                    {
                        candidates.Add(new CandidateMatch
                        {
                            SubstanceName = nb.Label,
                            ReferenceId = nb.SourceId,
                            Distance = nb.Distance,
                            Score = totalWeight > 0 ? Math.Round(nb.Weight / totalWeight, 4) : 0.0
                        });
                    }
                }

                return new MatchingResult
                {
                    Success = true,
                    PredictedSubstance = knnResult.PredictedClass,
                    Confidence = knnResult.Confidence,
                    Candidates = candidates,
                    Message = $"识别结果: {knnResult.PredictedClass} (置信度: {knnResult.Confidence:P})"
                };
            }
            catch (Exception ex)
            {
                return new MatchingResult
                {
                    Success = false,
                    Message = $"分类异常: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 批量分类
        /// </summary>
        public List<MatchingResult> MatchBatch(List<double[]> testSpectra, int k = 3, int pcaComponents = 5)
        {
            var results = new List<MatchingResult>();

            foreach (var spectrum in testSpectra)
            {
                results.Add(Match(spectrum, k, pcaComponents));
            }

            return results;
        }
    }

    /// <summary>
    /// 光谱匹配结果
    /// </summary>
    public class MatchingResult
    {
        /// <summary>
        /// 匹配是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 预测的物质名称
        /// </summary>
        public string PredictedSubstance { get; set; }

        /// <summary>
        /// 置信度 (0-1)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 候选匹配列表（按距离升序）
        /// </summary>
        public List<CandidateMatch> Candidates { get; set; } = new List<CandidateMatch>();
    }

    public class CandidateMatch
    {
        public string SubstanceName { get; set; }
        public int? ReferenceId { get; set; }
        public double Distance { get; set; }
        public double Score { get; set; } // 归一化权重（0-1）
    }
}
