using System.Net;
using Xunit;

public sealed class SqlServerProviderTests
{
    [Fact]
    public void Maps_tables_columns_defaults_checks_indexes_and_no_action_fks()
    {
        var rows = new SqlServerCatalogRows(
            Tables:
            [
                new("dbo", "Accounts", "Account master"),
                new("sales", "Orders", null),
            ],
            Columns:
            [
                new("dbo", "Accounts", "Code", "nvarchar", 100, 0, 0, false, false, null, 2, "Unique code"),
                new("dbo", "Accounts", "Id", "int", 4, 10, 0, false, true, null, 1, null),
                new("dbo", "Accounts", "Balance", "decimal", 9, 18, 2, false, false, "((0))", 3, null),
                new("sales", "Orders", "Id", "uniqueidentifier", 16, 0, 0, false, false, "(newid())", 1, null),
                new("sales", "Orders", "AccountId", "int", 4, 10, 0, false, false, null, 2, null),
                new("sales", "Orders", "Notes", "nvarchar", -1, 0, 0, true, false, null, 3, null),
            ],
            Keys:
            [
                new("dbo", "Accounts", "PK_Accounts", true, false, "Id", 1),
                new("dbo", "Accounts", "UQ_Accounts_Code", false, true, "Code", 1),
                new("sales", "Orders", "PK_Orders", true, false, "Id", 1),
            ],
            Checks:
            [
                new("dbo", "Accounts", "CK_Accounts_Balance", "([Balance]>=(0))", "Balance"),
            ],
            IndexColumns:
            [
                new("sales", "Orders", "IX_Orders_Account", false, "NONCLUSTERED", false, false, 1, "AccountId"),
            ],
            ForeignKeyColumns:
            [
                new("FK_Orders_Accounts", "sales", "Orders", "AccountId", "dbo", "Accounts", "Id", 1, "NO_ACTION", "CASCADE"),
            ]);

        var result = SqlServerIntrospectionProvider.MapCatalog("erp", rows);

        Assert.Equal("erp", result.Database);
        Assert.Equal("mssql", result.Engine);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.Tables.Count);

        var accounts = result.Tables.Single(t => t.Name == "Accounts");
        Assert.Equal("dbo", accounts.Schema);
        Assert.Equal("Account master", accounts.Comment);
        Assert.Equal(["Id", "Code", "Balance"], accounts.Columns.Select(c => c.Name).ToArray());

        Assert.True(accounts.Columns[0].IsPrimaryKey);
        Assert.True(accounts.Columns[0].IsAutoIncrement);
        Assert.Equal("NVARCHAR(50)", accounts.Columns[1].Type);
        Assert.True(accounts.Columns[1].IsUnique);
        Assert.Equal("Unique code", accounts.Columns[1].Comment);
        Assert.Equal("DECIMAL(18,2)", accounts.Columns[2].Type);
        Assert.Equal("0", accounts.Columns[2].DefaultValue);
        Assert.Equal("[Balance]>=(0)", accounts.Columns[2].CheckConstraint);

        var orders = result.Tables.Single(t => t.Name == "Orders");
        Assert.Equal("sales", orders.Schema);
        Assert.Equal("NVARCHAR(MAX)", orders.Columns[2].Type);
        Assert.Single(orders.Indexes);
        Assert.Equal([orders.Columns[1].Id], orders.Indexes[0].Columns);

        var rel = Assert.Single(result.Relations);
        Assert.Equal(accounts.Id, rel.SourceTableId);
        Assert.Equal(orders.Id, rel.TargetTableId);
        Assert.Equal("1:N", rel.Cardinality);
        Assert.Equal("NO ACTION", rel.OnDelete);
        Assert.Equal("CASCADE", rel.OnUpdate);
    }

    [Fact]
    public void Maps_p1_table_check_composite_unique_composite_fk_and_filtered_include_index_losslessly()
    {
        var rows = new SqlServerCatalogRows(
            Tables:
            [
                new("dbo", "Parent", null),
                new("dbo", "Child", null),
            ],
            Columns:
            [
                new("dbo", "Parent", "TenantId", "int", 4, 10, 0, false, false, null, 1, null),
                new("dbo", "Parent", "KeyCode", "varchar", 20, 0, 0, false, false, null, 2, null),
                new("dbo", "Child", "TenantId", "int", 4, 10, 0, false, false, null, 1, null),
                new("dbo", "Child", "KeyCode", "varchar", 20, 0, 0, false, false, null, 2, null),
                new("dbo", "Child", "Payload", "varchar", -1, 0, 0, true, false, null, 3, null),
            ],
            Keys:
            [
                new("dbo", "Parent", "UQ_Parent_Tenant_Key", false, true, "TenantId", 1),
                new("dbo", "Parent", "UQ_Parent_Tenant_Key", false, true, "KeyCode", 2),
            ],
            Checks:
            [
                new("dbo", "Child", "CK_Child_TableLevel", "([TenantId]>(0) AND [KeyCode]<>'')", null),
            ],
            IndexColumns:
            [
                new("dbo", "Child", "IX_Child_Filtered", false, "NONCLUSTERED", true, false, 1, "TenantId", "([TenantId]>(0))"),
                new("dbo", "Child", "IX_Child_Filtered", false, "NONCLUSTERED", true, true, 0, "Payload", "([TenantId]>(0))"),
            ],
            ForeignKeyColumns:
            [
                new("FK_Child_Parent", "dbo", "Child", "TenantId", "dbo", "Parent", "TenantId", 1, "NO_ACTION", "NO_ACTION"),
                new("FK_Child_Parent", "dbo", "Child", "KeyCode", "dbo", "Parent", "KeyCode", 2, "NO_ACTION", "NO_ACTION"),
            ]);

        var result = SqlServerIntrospectionProvider.MapCatalog("erp", rows);

        Assert.Empty(result.Warnings);

        var parent = result.Tables.Single(t => t.Name == "Parent");
        var uq = Assert.Single(parent.UniqueConstraints!);
        Assert.Equal("UQ_Parent_Tenant_Key", uq.Name);
        Assert.Equal([parent.Columns[0].Id, parent.Columns[1].Id], uq.Columns);

        var child = result.Tables.Single(t => t.Name == "Child");
        var chk = Assert.Single(child.CheckConstraints!);
        Assert.Equal("CK_Child_TableLevel", chk.Name);
        Assert.Equal("[TenantId]>(0) AND [KeyCode]<>''", chk.Expression);

        var idx = Assert.Single(child.Indexes);
        Assert.Equal([child.Columns[0].Id], idx.Columns);
        Assert.Equal("[TenantId]>(0)", idx.WhereClause);
        Assert.Equal([child.Columns[2].Id], idx.IncludeColumns);

        var rel = Assert.Single(result.Relations);
        Assert.Equal("FK_Child_Parent", rel.Name);
        Assert.Equal([parent.Columns[0].Id, parent.Columns[1].Id], rel.SourceColumnIds);
        Assert.Equal([child.Columns[0].Id, child.Columns[1].Id], rel.TargetColumnIds);
    }

    [Fact]
    public async Task Opt_in_live_mssql_smoke_test()
    {
        var url = Environment.GetEnvironmentVariable("SCHEMAFORGE_TEST_MSSQL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var provider = new SqlServerIntrospectionProvider();
        var addresses = await Dns.GetHostAddressesAsync(uri.Host);
        var target = new ValidatedDatabaseTarget(uri.Host, addresses[0], uri.Port > 0 ? uri.Port : 1433);
        var request = new IntrospectionRequest
        {
            Engine = "mssql",
            Host = uri.Host,
            Port = target.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Ssl = false,
        };

        var result = await provider.IntrospectAsync(request, target, CancellationToken.None);
        Assert.Equal("mssql", result.Engine);
        Assert.NotNull(result.Warnings);
    }
}
