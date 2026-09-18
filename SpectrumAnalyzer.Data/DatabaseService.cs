using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using SpectrumAnalyzer.Core;

namespace SpectrumAnalyzer.Data
{
    public class DatabaseService
    {
        // 静态事件：当数据库中光谱数据发生变化（新增/更新/删除）时触发，便于 UI 实时刷新
        public static event Action DataChanged;

        private static void NotifyDataChanged()
        {
            try { DataChanged?.Invoke(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

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

            // 3. 创建标准库分类表 (Category)，ParentId 自引用实现树状分类
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS Category (
                    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name       TEXT    NOT NULL,
                    ParentId   INTEGER,
                    SortOrder  INTEGER DEFAULT 0,
                    CreatedAt  TEXT    NOT NULL
                )");
        }

        // 3. 自动检测并升级现有本地旧版数据库结构，仅追加缺失的列（用 PRAGMA table_info 判定，而非靠 ALTER 报错猜测）
        using (var conn = new SQLiteConnection(dbPath))
        {
            conn.Open();
            EnsureColumnExists(conn, "ReferenceSpectrum", "X_cm", "TEXT");
            EnsureColumnExists(conn, "ReferenceSpectrum", "Y_cm", "TEXT");
            EnsureColumnExists(conn, "ReferenceSpectrum", "Reserved1", "TEXT");
            EnsureColumnExists(conn, "ReferenceSpectrum", "Reserved2", "TEXT");
            EnsureColumnExists(conn, "ReferenceSpectrum", "CategoryId", "INTEGER");
        }
    }

    /// <summary>
    /// 若数据表缺少指定列，则通过 ALTER TABLE 追加；已存在则跳过
    /// </summary>
    private static void EnsureColumnExists(IDbConnection conn, string table, string column, string columnDef)
    {
        var names = new HashSet<string>(conn.Query<string>($"SELECT name FROM pragma_table_info('{table}')"));
        if (!names.Contains(column))
            conn.Execute($"ALTER TABLE {table} ADD COLUMN {column} {columnDef}");
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
                            Grating, PeaksJson, Note, CategoryId, CreatedAt, X_cm, Y_cm
                        ) 
                        VALUES (
                            @SubstanceName, @ChemicalFormula, @CasNumber, @ExcitationWavelength, 
                            @Grating, @PeaksJson, @Note, @CategoryId, @CreatedAt, @X_cm, @Y_cm
                        )";
            }

            if (!string.IsNullOrEmpty(sql))
            {
                conn.Execute(sql, model);
                // 通知订阅者数据变更
                NotifyDataChanged();
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
                        CategoryId = @CategoryId,
                        X_cm = @X_cm,
                        Y_cm = @Y_cm
                      WHERE Id = @Id";
            conn.Execute(sql, model);
            NotifyDataChanged();
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
            NotifyDataChanged();
        }
    }

    // --- 标准库分类 (Category) ---

    // 获取全部分类（扁平，按同级排序；树的层级在 UI 层由 ParentId 构建）
    public List<CategoryModel> GetCategories()
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            return conn.Query<CategoryModel>("SELECT * FROM Category ORDER BY ParentId, SortOrder, Id").ToList();
        }
    }

    // 按 ID 获取分类
    public CategoryModel GetCategoryById(int id)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            return conn.QueryFirstOrDefault<CategoryModel>("SELECT * FROM Category WHERE Id = @id", new { id });
        }
    }

    // 新增分类
    public void AddCategory(CategoryModel category)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            conn.Execute(@"INSERT INTO Category (Name, ParentId, SortOrder, CreatedAt)
                           VALUES (@Name, @ParentId, @SortOrder, @CreatedAt)", category);
            NotifyDataChanged();
        }
    }

    // 更新分类；会阻止把分类移动到其自身或子孙之下（防止成环）
    public void UpdateCategory(CategoryModel category)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            if (category.ParentId.HasValue && WouldCreateCycle(conn, category.Id, category.ParentId.Value))
                throw new InvalidOperationException("不能把分类移动到其自身的下级分类中");

            conn.Execute(@"UPDATE Category SET Name = @Name, ParentId = @ParentId, SortOrder = @SortOrder
                           WHERE Id = @Id", category);
            NotifyDataChanged();
        }
    }

    // 删除分类；若其下仍有子分类或已被光谱引用，则拒绝删除，避免产生孤儿数据
    public void DeleteCategory(int id)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            int childCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Category WHERE ParentId = @id", new { id });
            if (childCount > 0)
                throw new InvalidOperationException("该分类下还有子分类，无法删除");

            int usedCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM ReferenceSpectrum WHERE CategoryId = @id", new { id });
            if (usedCount > 0)
                throw new InvalidOperationException("该分类下仍有光谱，无法删除");

            conn.Execute("DELETE FROM Category WHERE Id = @id", new { id });
            NotifyDataChanged();
        }
    }

    // 沿新父级链向上检查：若最终回到 nodeId 自身，则表示会产生环
    private static bool WouldCreateCycle(IDbConnection conn, int nodeId, int parentCandidateId)
    {
        int current = parentCandidateId;
        while (true)
        {
            if (current == nodeId) return true;
            var row = conn.QueryFirstOrDefault<CategoryModel>(
                "SELECT Id, ParentId FROM Category WHERE Id = @id", new { id = current });
            if (row == null || !row.ParentId.HasValue) return false;
            current = row.ParentId.Value;
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

    // --- 索引化查询：按主键/关键字下推到数据库，避免整表加载后在内存中过滤 ---

    /// <summary>
    /// 按 ID 获取实测光谱（含全谱 BLOB）
    /// </summary>
    public RamanSpectrumModel GetRamanSpectrumById(int id)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            return conn.QueryFirstOrDefault<RamanSpectrumModel>(
                "SELECT * FROM RamanSpectrum WHERE Id = @id", new { id });
        }
    }

    /// <summary>
    /// 按 ID 获取标准参考光谱（含全谱 JSON）
    /// </summary>
    public ReferenceSpectrumModel GetReferenceSpectrumById(int id)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            return conn.QueryFirstOrDefault<ReferenceSpectrumModel>(
                "SELECT * FROM ReferenceSpectrum WHERE Id = @id", new { id });
        }
    }

    /// <summary>
    /// 按名称模糊搜索实测光谱（名称或自动识别名称）
    /// </summary>
    public List<RamanSpectrumModel> SearchRamanByName(string name)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            var p = new { pattern = "%" + name + "%" };
            return conn.Query<RamanSpectrumModel>(
                "SELECT * FROM RamanSpectrum WHERE Name LIKE @pattern OR NameAuto LIKE @pattern ORDER BY Id DESC", p).ToList();
        }
    }

    /// <summary>
    /// 按物质名称模糊搜索标准参考光谱
    /// </summary>
    public List<ReferenceSpectrumModel> SearchReferenceBySubstanceName(string substanceName)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            var p = new { pattern = "%" + substanceName + "%" };
            return conn.Query<ReferenceSpectrumModel>(
                "SELECT * FROM ReferenceSpectrum WHERE SubstanceName LIKE @pattern ORDER BY Id DESC", p).ToList();
        }
    }

    /// <summary>
    /// 按 CAS 号模糊搜索标准参考光谱
    /// </summary>
    public List<ReferenceSpectrumModel> SearchReferenceByCasNumber(string casNumber)
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            var p = new { pattern = "%" + casNumber + "%" };
            return conn.Query<ReferenceSpectrumModel>(
                "SELECT * FROM ReferenceSpectrum WHERE CasNumber LIKE @pattern ORDER BY Id DESC", p).ToList();
        }
    }

    // --- 轻量列表查询：不带全谱大字段，仅用于列表展示，显著降低内存占用 ---

    /// <summary>
    /// 获取实测光谱轻量列表（不含 X_nm/Y_nm/X_cm/Y_cm 等大字段）
    /// </summary>
    public List<RamanSpectrumModel> GetRamanLibrarySummary()
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            return conn.Query<RamanSpectrumModel>(
                @"SELECT Id, Name, NameAuto, AcquiredAt, Points, Note,
                          IntegrationTime, Grating, LaserPower, ExcitationWavelength, ProcessMethod
                   FROM RamanSpectrum ORDER BY Id DESC").ToList();
        }
    }

    /// <summary>
    /// 获取标准参考光谱轻量列表（不含 X_cm/Y_cm 全谱 JSON）
    /// </summary>
    public List<ReferenceSpectrumModel> GetReferenceLibrarySummary()
    {
        using (IDbConnection conn = new SQLiteConnection(dbPath))
        {
            return conn.Query<ReferenceSpectrumModel>(
                @"SELECT Id, SubstanceName, ChemicalFormula, CasNumber, ExcitationWavelength,
                          Grating, PeaksJson, Note, CategoryId, CreatedAt
                   FROM ReferenceSpectrum ORDER BY Id DESC").ToList();
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

        // 删除后触发通知
        NotifyDataChanged();
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
                    // 导入完成后触发通知
                    NotifyDataChanged();
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