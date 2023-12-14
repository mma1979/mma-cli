using Microsoft.Data.SqlClient;

using Mma.Cli.Shared.Consts;
using Mma.Cli.UI.Models;

using System.Data;

namespace Mma.Cli.UI.Data
{
    public class DatabaseEngine
    {
        private string ConnectionString { get; set; }
        public DatabaseEngine(string connectionString)
        {
            ConnectionString = connectionString;
        }
        public List<DatabaseScheme> ReadTablesScheme()
        {
            using var ad = new SqlDataAdapter(@"SELECT 
    SCHEMA_NAME(tab.schema_id) AS schema_name,
    tab.name AS table_name,
    col.column_id,
    col.name AS column_name,
    t.name AS data_type,
    col.max_length,
    col.precision,
    col.is_nullable,
    CASE WHEN fk.name IS NOT NULL THEN convert(bit, 1) ELSE CONVERT(bit, 0) END AS is_foreign_key
FROM 
    sys.tables AS tab
    INNER JOIN sys.columns AS col ON tab.object_id = col.object_id
    LEFT JOIN sys.types AS t ON col.user_type_id = t.user_type_id
    LEFT JOIN (
        SELECT 
            fk.name,
            tab.name AS table_name,
            col.name AS column_name
        FROM 
            sys.foreign_keys AS fk
            INNER JOIN sys.tables AS tab ON fk.parent_object_id = tab.object_id
            INNER JOIN sys.columns AS col ON fk.parent_object_id = col.object_id
    ) AS fk ON tab.name = fk.table_name AND col.name = fk.column_name
WHERE 
    tab.name NOT IN ('__EFMigrationsHistory', 'sysdiagrams', 'AppUsers', 'Attachments', 'ELMAH_Error', 'NotificationStatuses', 'NotificationTypes', 'Roles', 'SysSettings', 'Notifications', 'RefreshTokens', 'UserClaims', 'UserLogins', 'UserTokens', 'RoleClaims', 'UserRoles') AND
    col.name NOT IN ('CreatedBy', 'CreatedDate', 'ModifiedBy', 'ModifiedDate', 'IsDeleted', 'DeletedBy', 'DeletedDate')
ORDER BY 
    schema_name,
    table_name,
    column_id;

", ConnectionString);

            using var dt = new DataTable();
            ad.Fill(dt);

            return dt.Rows.Cast<DataRow>()
                .Select(r => new DatabaseScheme
                {
                    schema_name = r["schema_name"].ToString(),
                    table_name = r["table_name"].ToString(),
                    column_id = r["column_id"].ToString(),
                    column_name = r["column_name"].ToString(),
                    data_type = r["data_type"].ToString(),
                    max_length = r["max_length"].ToString(),
                    precision = r["precision"].ToString(),
                    is_nullable = bool.Parse(r["is_nullable"].ToString()),
                    is_foreign_key = bool.Parse(r["is_foreign_key"].ToString())
                     
                }).ToList();

        }

        public List<RelationsScheme> ReadRelationsScheme()
        {
            using var ad = new SqlDataAdapter(@"SELECT
					--fk.name 'FK Name',
					tp.name 'RelatedTableName',
					cp.name 'ForeignKey', --cp.column_id,
					tr.name 'CurrentTable',
					t.name 'DataType'
					--cr.name, cr.column_id
				FROM 
					sys.foreign_keys fk
				INNER JOIN 
					sys.tables tp ON fk.parent_object_id = tp.object_id
				INNER JOIN 
					sys.tables tr ON fk.referenced_object_id = tr.object_id
				INNER JOIN 
					sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
				INNER JOIN 
					sys.columns cp ON fkc.parent_column_id = cp.column_id AND fkc.parent_object_id = cp.object_id
				INNER JOIN 
					sys.columns cr ON fkc.referenced_column_id = cr.column_id AND fkc.referenced_object_id = cr.object_id
				INNER JOIN
					sys.types t on t.system_type_id = cp.system_type_id and t.name <> 'sysname'

				where tp.name NOT IN ('__EFMigrationsHistory', 'sysdiagrams', 'AppUsers', 'Attachments', 'ELMAH_Error', 'NotificationStatuses', 'NotificationTypes', 'Roles', 'SysSettings', 'Notifications', 'RefreshTokens', 'UserClaims', 'UserLogins', 'UserTokens', 'RoleClaims', 'UserRoles')
				ORDER BY
					tp.name, cp.column_id", ConnectionString);

            using var dt = new DataTable();
            ad.Fill(dt);

            return dt.Rows.Cast<DataRow>()
                .Select(r => new RelationsScheme
                {
                    CurrentTable = r["CurrentTable"].ToString(),
                    ForeignKey = r["ForeignKey"].ToString(),
                    RelatedTableName = r["RelatedTableName"].ToString(),
                    DataType = r["DataType"].ToString()
                }).ToList();

        }

        public Table GetTable(List<DatabaseScheme> scheme, List<RelationsScheme> relations)
        {
            var tableName = scheme.First().table_name;
            var idType = MapDataType(scheme.FirstOrDefault(s => s.column_name.ToLower() == "id")?.data_type);
            var columns = scheme.Where(s => s.column_name.ToLower() != "id")
                .Select(c => new Column
                {
                    Name = c.column_name,
                    DataType = MapDataType(c.data_type),
                    IsNullable = c.is_nullable,
                    IsForeignKey = c.is_foreign_key
                }).ToList();
            var tableRelations = relations
                .Where(r => r.CurrentTable == tableName || r.RelatedTableName == tableName)
                .Select(r => new TableRelation
                {
                    IsCollection = (r.CurrentTable == tableName),
                    RelatedTableName = MapTableName(r.CurrentTable == tableName ? r.RelatedTableName : r.CurrentTable)
                }).ToList();

            return new Table
            {
                Name = MapTableName(tableName),
                IdType = idType,
                Columns = columns,
                TableRelations = tableRelations
            };
        }

        public List<Relation> GetRelations(List<RelationsScheme> schemes)
        {
            return schemes.Select(r => new Relation
            {
                ParentName = MapTableName(r.CurrentTable),
                ChiledName = MapTableName(r.RelatedTableName),
                ForeignKey = r.ForeignKey,
                RelationType = "1:M"
            }).ToList();
        }

        public string MapDataType(string dbType)
        {
            return dbType switch
            {
                "int" => PkTypes.INT,
                "bigint" => PkTypes.LONG,
                "nvarchar" => PkTypes.STRING,
                "ntext" => PkTypes.STRING,
                "bit" => PkTypes.BOOL,
                "datetimeoffset" => PkTypes.DATE_TIME,
                "datetime" => PkTypes.DATE_TIME,
                "datetime2" => PkTypes.DATE_TIME,
                "decimal" => PkTypes.DECIMAL,
                "uniqueidentifier" => PkTypes.GUID,
                _ => PkTypes.STRING

            };
        }

        public string MapTableName(string dbTableName)
        {
            return dbTableName.EndsWith("ies") ?
                dbTableName.Replace("ies", "y") : dbTableName.EndsWith("ses") ?
                dbTableName.Replace("ses", "s") : dbTableName.TrimEnd('s');
        }
    }
}
