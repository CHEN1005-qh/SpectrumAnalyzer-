using System;
using System.Collections.Generic;
using System.Linq;
using SpectrumAnalyzer.Core;

namespace SpectrumAnalyzer.Data.Repositories
{
    /// <summary>
    /// 实测拉曼光谱仓储实现
    /// </summary>
    public class RamanSpectrumRepository : IRamanSpectrumRepository
    {
        private readonly DatabaseService _databaseService;

        public RamanSpectrumRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        /// <summary>
        /// 获取所有实测光谱
        /// </summary>
        public List<RamanSpectrumModel> GetAll()
        {
            try
            {
                return _databaseService.GetRamanLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取实测光谱失败: {ex.Message}");
                return new List<RamanSpectrumModel>();
            }
        }

        /// <summary>
        /// 获取实测光谱轻量列表（不含全谱大字段）
        /// </summary>
        public List<RamanSpectrumModel> GetAllSummary()
        {
            try
            {
                return _databaseService.GetRamanLibrarySummary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取实测光谱轻量列表失败: {ex.Message}");
                return new List<RamanSpectrumModel>();
            }
        }

        /// <summary>
        /// 获取指定ID的光谱
        /// </summary>
        public RamanSpectrumModel GetById(int id)
        {
            try
            {
                return _databaseService.GetRamanSpectrumById(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"根据ID获取光谱失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 添加新的光谱记录
        /// </summary>
        public int Add(RamanSpectrumModel spectrum)
        {
            try
            {
                if (spectrum == null) throw new ArgumentNullException(nameof(spectrum));
                _databaseService.SaveSubstance(spectrum, "RamanSpectrum");
                // TODO: 返回实际的ID（需要修改DatabaseService以支持返回生成的ID）
                return spectrum.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"添加光谱失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 更新光谱记录
        /// </summary>
        public bool Update(RamanSpectrumModel spectrum)
        {
            try
            {
                if (spectrum == null) throw new ArgumentNullException(nameof(spectrum));
                _databaseService.UpdateRamanSpectrum(spectrum);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新光谱失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除光谱记录
        /// </summary>
        public bool Delete(int id)
        {
            try
            {
                _databaseService.DeleteSubstance(id, "RamanSpectrum");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除光谱失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 按名称搜索光谱
        /// </summary>
        public List<RamanSpectrumModel> SearchByName(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return GetAll();

                return _databaseService.SearchRamanByName(name);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"按名称搜索光谱失败: {ex.Message}");
                return new List<RamanSpectrumModel>();
            }
        }
    }

    /// <summary>
    /// 标准参考光谱仓储实现
    /// </summary>
    public class ReferenceSpectrumRepository : IReferenceSpectrumRepository
    {
        private readonly DatabaseService _databaseService;

        public ReferenceSpectrumRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        /// <summary>
        /// 获取所有标准光谱
        /// </summary>
        public List<ReferenceSpectrumModel> GetAll()
        {
            try
            {
                return _databaseService.GetReferenceLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取标准光谱失败: {ex.Message}");
                return new List<ReferenceSpectrumModel>();
            }
        }

        /// <summary>
        /// 获取标准光谱轻量列表（不含全谱大字段）
        /// </summary>
        public List<ReferenceSpectrumModel> GetAllSummary()
        {
            try
            {
                return _databaseService.GetReferenceLibrarySummary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取标准光谱轻量列表失败: {ex.Message}");
                return new List<ReferenceSpectrumModel>();
            }
        }

        /// <summary>
        /// 获取指定ID的标准光谱
        /// </summary>
        public ReferenceSpectrumModel GetById(int id)
        {
            try
            {
                return _databaseService.GetReferenceSpectrumById(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"根据ID获取标准光谱失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 添加新的标准光谱
        /// </summary>
        public int Add(ReferenceSpectrumModel spectrum)
        {
            try
            {
                if (spectrum == null) throw new ArgumentNullException(nameof(spectrum));
                _databaseService.SaveSubstance(spectrum, "ReferenceSpectrum");
                // TODO: 返回实际的ID（需要修改DatabaseService以支持返回生成的ID）
                return spectrum.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"添加标准光谱失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 更新标准光谱
        /// </summary>
        public bool Update(ReferenceSpectrumModel spectrum)
        {
            try
            {
                if (spectrum == null) throw new ArgumentNullException(nameof(spectrum));
                _databaseService.UpdateReference(spectrum);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新标准光谱失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除标准光谱
        /// </summary>
        public bool Delete(int id)
        {
            try
            {
                _databaseService.DeleteSubstance(id, "ReferenceSpectrum");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除标准光谱失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 按物质名称搜索标准光谱
        /// </summary>
        public List<ReferenceSpectrumModel> SearchBySubstanceName(string substanceName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(substanceName))
                    return GetAll();

                return _databaseService.SearchReferenceBySubstanceName(substanceName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"按物质名称搜索光谱失败: {ex.Message}");
                return new List<ReferenceSpectrumModel>();
            }
        }

        /// <summary>
        /// 按CAS号搜索标准光谱
        /// </summary>
        public List<ReferenceSpectrumModel> SearchByCasNumber(string casNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(casNumber))
                    return GetAll();

                return _databaseService.SearchReferenceByCasNumber(casNumber);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"按CAS号搜索光谱失败: {ex.Message}");
                return new List<ReferenceSpectrumModel>();
            }
        }

        /// <summary>
        /// 批量导入标准参考光谱
        /// </summary>
        public void ImportReferenceSpectra(List<ReferenceSpectrumModel> list)
        {
            try
            {
                _databaseService.ImportReferenceSpectra(list);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"批量导入标准光谱失败: {ex.Message}");
                throw;
            }
        }
    }
}
