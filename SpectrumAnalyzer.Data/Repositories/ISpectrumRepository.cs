using System;
using System.Collections.Generic;
using SpectrumAnalyzer.Core;

namespace SpectrumAnalyzer.Data.Repositories
{
    /// <summary>
    /// 光谱数据仓储接口 - 定义数据访问规范
    /// </summary>
    public interface ISpectrumRepository
    {
    }

    /// <summary>
    /// 实测拉曼光谱仓储接口
    /// </summary>
    public interface IRamanSpectrumRepository : ISpectrumRepository
    {
        /// <summary>
        /// 获取所有实测光谱
        /// </summary>
        List<RamanSpectrumModel> GetAll();

        /// <summary>
        /// 获取指定ID的光谱
        /// </summary>
        RamanSpectrumModel GetById(int id);

        /// <summary>
        /// 添加新的光谱记录
        /// </summary>
        int Add(RamanSpectrumModel spectrum);

        /// <summary>
        /// 更新光谱记录
        /// </summary>
        bool Update(RamanSpectrumModel spectrum);

        /// <summary>
        /// 删除光谱记录
        /// </summary>
        bool Delete(int id);

        /// <summary>
        /// 按名称搜索光谱
        /// </summary>
        List<RamanSpectrumModel> SearchByName(string name);
    }

    /// <summary>
    /// 标准参考光谱仓储接口
    /// </summary>
    public interface IReferenceSpectrumRepository : ISpectrumRepository
    {
        /// <summary>
        /// 获取所有标准光谱
        /// </summary>
        List<ReferenceSpectrumModel> GetAll();

        /// <summary>
        /// 获取指定ID的标准光谱
        /// </summary>
        ReferenceSpectrumModel GetById(int id);

        /// <summary>
        /// 添加新的标准光谱
        /// </summary>
        int Add(ReferenceSpectrumModel spectrum);

        /// <summary>
        /// 更新标准光谱
        /// </summary>
        bool Update(ReferenceSpectrumModel spectrum);

        /// <summary>
        /// 删除标准光谱
        /// </summary>
        bool Delete(int id);

        /// <summary>
        /// 按物质名称搜索标准光谱
        /// </summary>
        List<ReferenceSpectrumModel> SearchBySubstanceName(string substanceName);

        /// <summary>
        /// 按CAS号搜索标准光谱
        /// </summary>
        List<ReferenceSpectrumModel> SearchByCasNumber(string casNumber);
    }
}
