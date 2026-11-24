using System.Net;
using Xunit;

public sealed class MySqlProviderTests
{
    [Fact]
    public void Maps_tables_storage_engine_charset_columns_indexes_and_single_column_fks()
    {
        var rows = new MySqlCatalogRows(
            Tables:
            [
                new("customers", "InnoDB", "utf8mb4_unicode_ci", "Customer directory"),
                new("invoices", "MyISAM", "latin1_swedish_ci", "Billing invoices"),
            ],
            Columns:
            [
                new("customers", "email", "varchar(255)", false, "UNI", null, "", "Email address", 2),
                new("customers", "id", "bigint unsigned", false, "PRI", null, "auto_increment", "Primary key", 1),
                new("customers", "credit_limit", "decimal(10,2)", false, "", "0.00", "", null, 3),
                new("invoices", "id", "bigint unsigned", false, "PRI", null, "auto_increment", null, 1),
                new("invoices", "customer_id", "bigint unsigned", false, "MUL", null, "", null, 2),
                new("invoices", "status", "varchar(32)", false, "", "open", "", null, 3),
            ],
            Keys:
            [
                new("customers", "PRIMARY", "PRIMARY KEY", "id", 1),
                new("customers", "uq_customers_email", "UNIQUE", "email", 1),
                new("invoices", "PRIMARY", "PRIMARY KEY", "id", 1),
            ],
            Checks:
            [
                new("customers", "chk_credit_limit", "(`credit_limit` >= 0)"),
            ],
            IndexColumns:
            [
                new("invoices", "idx_invoices_customer_status", false, 2, "status", null, "BTREE", null),
                new("invoices", "idx_invoices_customer_status", false, 1, "customer_id", null, "BTREE", null),
            ],
            ForeignKeyColumns:
            [
                new("fk_invoices_customer", "invoices", "customer_id", "customers", "id", 1, "CASCADE", "RESTRICT"),
            ]);

        var result = MySqlIntrospectionProvider.MapCatalog("billing", rows);

        Assert.Equal("billing", result.Database);
        Assert.Equal("mysql", result.Engine);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.Tables.Count);

        var customers = result.Tables.Single(t => t.Name == "customers");
        Assert.Equal("InnoDB", customers.StorageEngine);
        Assert.Equal("utf8mb4", customers.Charset);
        Assert.Equal("Customer directory", customers.Comment);
        Assert.Equal(["id", "email", "credit_limit"], customers.Columns.Select(c => c.Name).ToArray());

        Assert.True(customers.Columns[0].IsPrimaryKey);
        Assert.True(customers.Columns[0].IsAutoIncrement);
        Assert.True(customers.Columns[1].IsUnique);
        Assert.Equal("`credit_limit` >= 0", customers.Columns[2].CheckConstraint);

        var invoices = result.Tables.Single(t => t.Name == "invoices");
        Assert.Equal("MyISAM", invoices.StorageEngine);
        Assert.Equal("latin1", invoices.Charset);
        Assert.Single(invoices.Indexes);
        Assert.Equal(
            [invoices.Columns[1].Id, invoices.Columns[2].Id],
            invoices.Indexes[0].Columns);

        var rel = Assert.Single(result.Relations);
        Assert.Equal(customers.Id, rel.SourceTableId);
        Assert.Equal(invoices.Id, rel.TargetTableId);
        Assert.Equal("1:N", rel.Cardinality);
        Assert.Equal("CASCADE", rel.OnDelete);
        Assert.Equal("RESTRICT", rel.OnUpdate);
    }

    [Fact]
    public void Maps_p1_composite_unique_composite_fk_table_check_and_index_method_losslessly()
    {
        var rows = new MySqlCatalogRows(
            Tables:
            [
                new("parent", "InnoDB", "utf8mb4_0900_ai_ci", null),
                new("child", "InnoDB", "utf8mb4_0900_ai_ci", null),
            ],
            Columns:
            [
                new("parent", "a", "int", false, "PRI", null, "", null, 1),
                new("parent", "b", "int", false, "PRI", null, "", null, 2),
                new("child", "a", "int", false, "", null, "", null, 1),
                new("child", "b", "int", false, "", null, "", null, 2),
                new("child", "notes", "text", true, "", null, "", null, 3),
            ],
            Keys:
            [
                new("parent", "uq_parent_ab", "UNIQUE", "a", 1),
                new("parent", "uq_parent_ab", "UNIQUE", "b", 2),
            ],
            Checks:
            [
                new("child", "chk_ab", "(`a` < `b`)"),
            ],
            IndexColumns:
            [
                new("child", "ft_notes", false, 1, "notes", null, "FULLTEXT", null),
                new("child", "idx_notes_prefix", false, 1, "notes", 20, "BTREE", null),
            ],
            ForeignKeyColumns:
            [
                new("fk_child_parent", "child", "a", "parent", "a", 1, "CASCADE", "NO ACTION"),
                new("fk_child_parent", "child", "b", "parent", "b", 2, "CASCADE", "NO ACTION"),
            ]);

        var result = MySqlIntrospectionProvider.MapCatalog("appdb", rows);

        var parent = result.Tables.Single(t => t.Name == "parent");
        var uq = Assert.Single(parent.UniqueConstraints!);
        Assert.Equal("uq_parent_ab", uq.Name);
        Assert.Equal([parent.Columns[0].Id, parent.Columns[1].Id], uq.Columns);

        var child = result.Tables.Single(t => t.Name == "child");
        var chk = Assert.Single(child.CheckConstraints!);
        Assert.Equal("chk_ab", chk.Name);
        Assert.Equal("`a` < `b`", chk.Expression);

        var ftIdx = child.Indexes.Single(i => i.Name == "ft_notes");
        Assert.Equal("FULLTEXT", ftIdx.Method);

        var rel = Assert.Single(result.Relations);
        Assert.Equal("fk_child_parent", rel.Name);
        Assert.Equal([parent.Columns[0].Id, parent.Columns[1].Id], rel.SourceColumnIds);
        Assert.Equal([child.Columns[0].Id, child.Columns[1].Id], rel.TargetColumnIds);

        // Prefix sub-part still emits unsupported_index_semantics warning
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("unsupported_index_semantics", warning.Code);
    }

    [Fact]
    public async Task Opt_in_live_mysql_smoke_test()
    {
        var url = Environment.GetEnvironmentVariable("SCHEMAFORGE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var provider = new MySqlIntrospectionProvider();
        var addresses = await Dns.GetHostAddressesAsync(uri.Host);
        var target = new ValidatedDatabaseTarget(uri.Host, addresses[0], uri.Port > 0 ? uri.Port : 3306);
        var request = new IntrospectionRequest
        {
            Engine = "mysql",
            Host = uri.Host,
            Port = target.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Ssl = false,
        };

        var result = await provider.IntrospectAsync(request, target, CancellationToken.None);
        Assert.Equal("mysql", result.Engine);
        Assert.NotNull(result.Warnings);
    }
}
