using System.Net;
using Xunit;

public sealed class PostgreSqlProviderTests
{
    [Fact]
    public void Maps_tables_ordered_columns_composite_pk_checks_indexes_and_single_column_fks()
    {
        var rows = new PostgreSqlCatalogRows(
            Tables:
            [
                new("public", "users", "User accounts"),
                new("public", "orders", "Customer orders"),
                new("public", "order_items", null),
            ],
            Columns:
            [
                new("public", "users", "email", "character varying", "varchar", 255, null, null, false, null, false, 2, "Primary email"),
                new("public", "users", "id", "bigint", "int8", null, null, null, false, "nextval('users_id_seq'::regclass)", false, 1, "Surrogate PK"),
                new("public", "users", "age", "integer", "int4", null, null, null, true, "0", false, 3, null),
                new("public", "orders", "id", "uuid", "uuid", null, null, null, false, "gen_random_uuid()", false, 1, null),
                new("public", "orders", "user_id", "bigint", "int8", null, null, null, false, null, false, 2, null),
                new("public", "orders", "total", "numeric", "numeric", null, 10, 2, false, "0", false, 3, null),
                new("public", "order_items", "order_id", "uuid", "uuid", null, null, null, false, null, false, 1, null),
                new("public", "order_items", "line_no", "integer", "int4", null, null, null, false, null, false, 2, null),
            ],
            Keys:
            [
                new("public", "users", "users_pkey", "PRIMARY KEY", "id", 1),
                new("public", "users", "users_email_key", "UNIQUE", "email", 1),
                new("public", "orders", "orders_pkey", "PRIMARY KEY", "id", 1),
                new("public", "orders", "orders_user_id_key", "UNIQUE", "user_id", 1),
                new("public", "order_items", "order_items_pkey", "PRIMARY KEY", "order_id", 1),
                new("public", "order_items", "order_items_pkey", "PRIMARY KEY", "line_no", 2),
            ],
            Checks:
            [
                new("public", "users", "users_age_check", "CHECK ((age >= 0))", ["age"]),
            ],
            Indexes:
            [
                new("public", "orders", "idx_orders_user_total", false, "btree", false, false, false, ["user_id", "total"]),
            ],
            ForeignKeys:
            [
                new("fk_orders_user", "public", "orders", ["user_id"], "public", "users", ["id"], "CASCADE", "NO ACTION"),
                new("fk_items_order", "public", "order_items", ["order_id"], "public", "orders", ["id"], "RESTRICT", "SET NULL"),
            ]);

        var result = PostgreSqlIntrospectionProvider.MapCatalog("shopdb", rows);

        Assert.Equal("shopdb", result.Database);
        Assert.Equal("postgresql", result.Engine);
        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.Tables.Count);

        var users = result.Tables.Single(t => t.Name == "users");
        Assert.Equal("public", users.Schema);
        Assert.Equal("User accounts", users.Comment);
        Assert.Equal(["id", "email", "age"], users.Columns.Select(c => c.Name).ToArray());

        var idCol = users.Columns[0];
        Assert.Equal("bigserial", idCol.Type);
        Assert.True(idCol.IsPrimaryKey);
        Assert.False(idCol.IsNullable);
        Assert.True(idCol.IsAutoIncrement);
        Assert.Equal(string.Empty, idCol.DefaultValue);
        Assert.Equal("Surrogate PK", idCol.Comment);

        var emailCol = users.Columns[1];
        Assert.Equal("varchar(255)", emailCol.Type);
        Assert.True(emailCol.IsUnique);
        Assert.Equal("Primary email", emailCol.Comment);

        var ageCol = users.Columns[2];
        Assert.Equal("age >= 0", ageCol.CheckConstraint);

        var orders = result.Tables.Single(t => t.Name == "orders");
        Assert.Single(orders.Indexes);
        var idx = orders.Indexes[0];
        Assert.Equal("idx_orders_user_total", idx.Name);
        Assert.False(idx.IsUnique);
        Assert.Equal(
            [orders.Columns[1].Id, orders.Columns[2].Id],
            idx.Columns);

        var orderItems = result.Tables.Single(t => t.Name == "order_items");
        Assert.All(orderItems.Columns, c => Assert.True(c.IsPrimaryKey));

        Assert.Equal(2, result.Relations.Count);
        var userOrderRel = result.Relations.Single(r => r.Name == "fk_orders_user");
        Assert.Equal(users.Id, userOrderRel.SourceTableId);
        Assert.Equal(orders.Id, userOrderRel.TargetTableId);
        Assert.Equal("1:1", userOrderRel.Cardinality);
        Assert.Equal("CASCADE", userOrderRel.OnDelete);
        Assert.Equal("NO ACTION", userOrderRel.OnUpdate);

        var orderItemsRel = result.Relations.Single(r => r.Name == "fk_items_order");
        Assert.Equal("1:N", orderItemsRel.Cardinality);
        Assert.Equal("RESTRICT", orderItemsRel.OnDelete);
        Assert.Equal("SET NULL", orderItemsRel.OnUpdate);
    }

    [Fact]
    public void Maps_p1_composite_fks_composite_uniques_table_checks_and_index_semantics_losslessly()
    {
        var rows = new PostgreSqlCatalogRows(
            Tables:
            [
                new("app", "parent", null),
                new("app", "child", null),
            ],
            Columns:
            [
                new("app", "parent", "tenant_id", "integer", "int4", null, null, null, false, null, false, 1, null),
                new("app", "parent", "code", "text", "text", null, null, null, false, null, false, 2, null),
                new("app", "child", "tenant_id", "integer", "int4", null, null, null, false, null, false, 1, null),
                new("app", "child", "code", "text", "text", null, null, null, false, null, false, 2, null),
                new("app", "child", "payload", "text", "text", null, null, null, true, null, false, 3, null),
            ],
            Keys:
            [
                new("app", "parent", "uq_parent_tenant_code", "UNIQUE", "tenant_id", 1),
                new("app", "parent", "uq_parent_tenant_code", "UNIQUE", "code", 2),
            ],
            Checks:
            [
                new("app", "child", "chk_tenant_code", "CHECK ((tenant_id > 0 AND length(code) > 0))", ["tenant_id", "code"]),
            ],
            Indexes:
            [
                new("app", "child", "idx_child_partial_gin", false, "gin", true, false, true, ["code"], "tenant_id > 0", ["payload"]),
                new("app", "child", "idx_child_expr", false, "btree", false, true, false, ["code"]),
            ],
            ForeignKeys:
            [
                new("fk_child_parent_composite", "app", "child", ["tenant_id", "code"], "app", "parent", ["tenant_id", "code"], "CASCADE", "NO ACTION"),
            ]);

        var result = PostgreSqlIntrospectionProvider.MapCatalog("appdb", rows);

        var parent = result.Tables.Single(t => t.Name == "parent");
        Assert.All(parent.Columns, c => Assert.False(c.IsUnique));
        var uq = Assert.Single(parent.UniqueConstraints!);
        Assert.Equal("uq_parent_tenant_code", uq.Name);
        Assert.Equal([parent.Columns[0].Id, parent.Columns[1].Id], uq.Columns);

        var child = result.Tables.Single(t => t.Name == "child");
        var chk = Assert.Single(child.CheckConstraints!);
        Assert.Equal("chk_tenant_code", chk.Name);
        Assert.Equal("tenant_id > 0 AND length(code) > 0", chk.Expression);

        var ginIdx = child.Indexes.Single(i => i.Name == "idx_child_partial_gin");
        Assert.Equal("gin", ginIdx.Method);
        Assert.Equal("tenant_id > 0", ginIdx.WhereClause);
        Assert.Equal([child.Columns[2].Id], ginIdx.IncludeColumns);

        var rel = Assert.Single(result.Relations);
        Assert.Equal("fk_child_parent_composite", rel.Name);
        Assert.Equal([parent.Columns[0].Id, parent.Columns[1].Id], rel.SourceColumnIds);
        Assert.Equal([child.Columns[0].Id, child.Columns[1].Id], rel.TargetColumnIds);

        // Only expression index still emits unsupported_index_semantics warning
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("unsupported_index_semantics", warning.Code);
    }

    [Fact]
    public async Task Opt_in_live_postgres_smoke_test()
    {
        var url = Environment.GetEnvironmentVariable("SCHEMAFORGE_TEST_POSTGRES_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var provider = new PostgreSqlIntrospectionProvider();
        var addresses = await Dns.GetHostAddressesAsync(uri.Host);
        var target = new ValidatedDatabaseTarget(uri.Host, addresses[0], uri.Port > 0 ? uri.Port : 5432);
        var request = new IntrospectionRequest
        {
            Engine = "postgresql",
            Host = uri.Host,
            Port = target.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Ssl = false,
        };

        var result = await provider.IntrospectAsync(request, target, CancellationToken.None);
        Assert.Equal("postgresql", result.Engine);
        Assert.NotNull(result.Warnings);
    }
}
