using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using SpectrumAnalyzer;

namespace SpectrumAnalyzer
{
    public class DatabaseService
    {
    // 数据库连接字符串
    private string dbPath = "Data Source=RamanLibrary.db;Version=3;";

    public DatabaseService()
    {
        using (var conn = new SQLiteConnection(dbPath))
        {
            conn.Open();

            // 1. 创建实测光谱表 (RamanSpectrum)
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS RamanSpectrum (
                    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name                 TEXT    NOT NULL,
                    NameAuto             TEXT    NOT NULL,
                    AcquiredAt           TEXT    NOT NULL,
                    Points               INTEGER NOT NULL,
                    X_nm                 BLOB    NOT NULL,
                    Y_nm                 BLOB    NOT NULL,
                    X_cm                 BLOB    NOT NULL,
                    Y_cm                 BLOB    NOT NULL,
                    Note                 TEXT,
                    IntegrationTime      REAL,
                    Grating              TEXT,
                    LaserPower           REAL,
                    ExcitationWavelength INTEGER,
                    PinholeSize          REAL,
                    Detector             TEXT,
                    ProcessMethod        TEXT,
                    AvgCount             INTEGER,
                    AccumCount           INTEGER,
                    Reserved1            TEXT,
                    Reserved2            TEXT,
                    Reserved3            TEXT
                )");

            // 2. 创建标准参考表 (ReferenceSpectrum)
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS ReferenceSpectrum (
                    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    SubstanceName        TEXT    NOT NULL UNIQUE,
                    ChemicalFormula      TEXT,
                    CasNumber            TEXT,
                    ExcitationWavelength INTEGER,
                    Grating              TEXT,
                    PeaksJson            TEXT,
                    Note                 TEXT,
                    CreatedAt            TEXT    NOT NULL,
                    Reserved1            TEXT,
                    Reserved2            TEXT,
                    X_cm                 TEXT,
                    Y_cm                 TEXT
                )");
        }

        // 3. 自动检测升级现有本地旧版数据库结构，追加缺失的列
        try
        {
            using (var conn = new SQLiteConnection(dbPath))
            {
                conn.Open();
                // 升级补丁：追加必要列
                conn.Execute("ALTER TABLE ReferenceSpectrum ADD COLUMN X_cm TEXT");
                conn.Execute("ALTER TABLE ReferenceSpectrum ADD COLUMN Y_cm TEXT");
                conn.Execute("ALTER TABLE ReferenceSpectrum ADD COLUMN Reserved1 TEXT");
                conn.Execute("ALTER TABLE ReferenceSpectrum ADD COLUMN Reserved2 TEXT");
            }
        }
        catch
        {
            // 忽略因列已存在导致的错误
        }
    }

    // --- 支持全谱列的通用保存方法 ---
    public void SaveSubstance<T>(T model, string tableName)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            string sql = "";
            if (tableName == "RamanSpectrum")
            {
                // 核心修改：补充写入必填的非空字段 @X_nm 和 @Y_nm
                sql = @"INSERT INTO RamanSpectrum (
                            Name, NameAuto, Note, AcquiredAt, Points, 
                            X_nm, Y_nm, X_cm, Y_cm, 
                            IntegrationTime, Grating, LaserPower, ExcitationWavelength, ProcessMethod
                        ) 
                        VALUES (
                            @Name, @NameAuto, @Note, @AcquiredAt, @Points, 
                            @X_nm, @Y_nm, @X_cm, @Y_cm, 
                            @IntegrationTime, @Grating, @LaserPower, @ExcitationWavelength, @ProcessMethod
                        )";
            }
            else if (tableName == "ReferenceSpectrum")
            {
                // 核心修改：使用 INSERT OR REPLACE 避免主键/唯一索引冲突崩溃
                sql = @"INSERT OR REPLACE INTO ReferenceSpectrum (
                            SubstanceName, ChemicalFormula, CasNumber, ExcitationWavelength, 
                            Grating, PeaksJson, Note, CreatedAt, X_cm, Y_cm
                        ) 
                        VALUES (
                            @SubstanceName, @ChemicalFormula, @CasNumber, @ExcitationWavelength, 
                            @Grating, @PeaksJson, @Note, @CreatedAt, @X_cm, @Y_cm
                        )";
            }

            if (!string.IsNullOrEmpty(sql))
            {
                conn.Execute(sql, model);
            }
        }
    }

    // 更新标准参考谱
    public void UpdateReference(ReferenceSpectrumModel model)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            string sql = @"UPDATE ReferenceSpectrum SET 
                        SubstanceName = @SubstanceName, 
                        ChemicalFormula = @ChemicalFormula, 
                        CasNumber = @CasNumber, 
                        ExcitationWavelength = @ExcitationWavelength, 
                        Grating = @Grating, 
                        PeaksJson = @PeaksJson,  
                        Note = @Note,
                        X_cm = @X_cm,
                        Y_cm = @Y_cm
                      WHERE Id = @Id";
            conn.Execute(sql, model);
        }
    }

    // 更新实测光谱记录
    public void UpdateRamanSpectrum(RamanSpectrumModel model)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            string sql = @"UPDATE RamanSpectrum SET 
                        Name = @Name, 
                        NameAuto = @NameAuto, 
                        Note = @Note,
                        X_cm = @X_cm,
                        Y_cm = @Y_cm,
                        IntegrationTime = @IntegrationTime, 
                        Grating = @Grating, 
                        LaserPower = @LaserPower, 
                        ExcitationWavelength = @ExcitationWavelength,
                        ProcessMethod = @ProcessMethod
                      WHERE Id = @Id";
            conn.Execute(sql, model);
        }
    }

    // 获取实测库
    public List<RamanSpectrumModel> GetRamanLibrary()
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            // 此时 X_cm/Y_cm 映射到 C# 中的 byte[] 属性，Dapper 正常解析不再崩溃
            return conn.Query<RamanSpectrumModel>("SELECT * FROM RamanSpectrum ORDER BY Id DESC").ToList();
        }
    }

    // 获取标准库
    public List<ReferenceSpectrumModel> GetReferenceLibrary()
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            return conn.Query<ReferenceSpectrumModel>("SELECT * FROM ReferenceSpectrum ORDER BY Id DESC").ToList();
        }
    }

    // 通用的物理删除方法
    public void DeleteSubstance(int id, string tableName)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            string sql = $"DELETE FROM {tableName} WHERE Id = @id";
            conn.Execute(sql, new { id });
        }
    }

    // 导入标准光谱
    public void ImportReferenceSpectra(List<ReferenceSpectrumModel> list)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            conn.Open();
            using (var transaction = conn.BeginTransaction())
            {
                try
                {
                    // 核心修改：使用 INSERT OR REPLACE，防止由于同名重合导致的主键/唯一约束失败崩溃
                    string sql = @"INSERT OR REPLACE INTO ReferenceSpectrum (
                                      SubstanceName, ChemicalFormula, CasNumber, ExcitationWavelength, 
                                      Grating, PeaksJson, Note, CreatedAt, X_cm, Y_cm
                                  ) 
                                  VALUES (
                                      @SubstanceName, @ChemicalFormula, @CasNumber, @ExcitationWavelength, 
                                      @Grating, @PeaksJson, @Note, @CreatedAt, @X_cm, @Y_cm
                                  )";

                    conn.Execute(sql, list, transaction: transaction);
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    System.Diagnostics.Debug.WriteLine("导入标准库失败: " + ex.Message);
                    throw;
                }
            }
        }
    }
}}