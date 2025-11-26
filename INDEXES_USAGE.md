# Configuração de Índices no CRUD-QL

## Visão Geral

O CRUD-QL permite configurar índices de banco de dados de forma declarativa e documentável através de uma API fluente. Os índices configurados são automaticamente aplicados ao Entity Framework Core.

## Exemplos de Uso

### 1. Índice Simples

```csharp
services.AddCrudQl()
    .AddEntity<Product>(cfg =>
    {
        cfg.ConfigureIndexes(indexes =>
        {
            // Índice ascendente no campo Price
            indexes.HasIndex(p => p.Price);

            // Índice descendente no campo CreatedAt
            indexes.HasIndex(
                p => p.CreatedAt,
                indexName: "IX_Product_CreatedAt_Desc",
                sortOrder: IndexSortOrder.Descending
            );
        });
    });
```

### 2. Índice Composto (Múltiplos Campos)

```csharp
cfg.ConfigureIndexes(indexes =>
{
    // Índice composto para ordenação Category + Price
    indexes.HasIndex(idx =>
    {
        idx.HasField(p => p.Category, IndexSortOrder.Ascending);
        idx.HasField(p => p.Price, IndexSortOrder.Descending);
    }, indexName: "IX_Product_Category_Price");
});
```

### 3. Índice Único

```csharp
cfg.ConfigureIndexes(indexes =>
{
    // Garante que Name seja único
    indexes.HasIndex(idx =>
    {
        idx.HasField(p => p.Name);
        idx.IsUnique();
    }, indexName: "IX_Product_Name_Unique");
});
```

### 4. Índice com Filtro (Partial Index)

```csharp
cfg.ConfigureIndexes(indexes =>
{
    // Índice apenas para produtos não deletados
    indexes.HasIndex(idx =>
    {
        idx.HasField(p => p.CreatedAt, IndexSortOrder.Descending);
        idx.HasFilter("[IsDeleted] = 0");
    }, indexName: "IX_Product_CreatedAt_Active");
});
```

### 5. Configuração Completa de Exemplo

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddCrudQl()
            .AddEntity<Product>(cfg =>
            {
                // Política de autorização
                cfg.UsePolicy(new ProductPolicy());

                // Paginação
                cfg.ConfigurePagination(defaultPageSize: 25, maxPageSize: 100);

                // Índices (NOVO!)
                cfg.ConfigureIndexes(indexes =>
                {
                    // Índice simples para ordenação por preço
                    indexes.HasIndex(p => p.Price);

                    // Índice composto para filtrar por categoria e ordenar por preço
                    indexes.HasIndex(idx =>
                    {
                        idx.HasField(p => p.Category, IndexSortOrder.Ascending);
                        idx.HasField(p => p.Price, IndexSortOrder.Descending);
                    }, indexName: "IX_Product_Category_Price");

                    // Índice único para nome
                    indexes.HasIndex(idx =>
                    {
                        idx.HasField(p => p.Name);
                        idx.IsUnique();
                    });

                    // Índice para soft delete
                    indexes.HasIndex(idx =>
                    {
                        idx.HasField(p => p.IsDeleted);
                        idx.HasField(p => p.DeletedAt);
                        idx.HasFilter("[IsDeleted] = 1");
                    }, indexName: "IX_Product_SoftDelete");
                });
            })
            .AddEntitiesFromDbContext<AppDbContext>();
    }
}
```

### 6. Aplicar Índices ao DbContext

No seu `DbContext`, aplique os índices automaticamente:

```csharp
public class AppDbContext : DbContext
{
    private readonly ICrudEntityRegistry _registry;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICrudEntityRegistry registry)
        : base(options)
    {
        _registry = registry;
    }

    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aplica todos os índices configurados via CRUD-QL
        modelBuilder.ApplyCrudQlIndexes(_registry);
    }
}
```

## Vantagens desta Abordagem

### ✅ Documentação Centralizada
Todos os índices estão documentados no mesmo lugar onde você configura a entidade no CRUD-QL.

### ✅ Type-Safe
Usa expressions para referenciar propriedades, detectando erros em compile-time.

### ✅ Aplicação Automática
Os índices são automaticamente aplicados ao EF Core via `ApplyCrudQlIndexes()`.

### ✅ Validação de Ordenação (Futuro)
Quando implementar ordenação, pode validar que apenas campos indexados são usados:

```csharp
// Futura validação de orderBy
var indexedFields = registration.IndexConfig?.Indexes
    .SelectMany(i => i.Fields)
    .Select(f => f.FieldName)
    .ToHashSet();

if (!indexedFields.Contains(orderByField))
{
    return BadRequest("Campo não indexado para ordenação");
}
```

## Estrutura SQL Gerada

Para o exemplo completo acima, o EF Core gerará migrations com:

```sql
-- Índice simples
CREATE INDEX [IX_Product_Price] ON [Products] ([Price]);

-- Índice composto
CREATE INDEX [IX_Product_Category_Price]
ON [Products] ([Category] ASC, [Price] DESC);

-- Índice único
CREATE UNIQUE INDEX [IX_Product_Name] ON [Products] ([Name]);

-- Índice filtrado
CREATE INDEX [IX_Product_SoftDelete]
ON [Products] ([IsDeleted], [DeletedAt])
WHERE [IsDeleted] = 1;
```

## Performance com Paginação + Ordenação

Índices compostos otimizam queries com paginação:

```http
GET /crud?entity=Product&filter={"field":"category","op":"eq","value":"Electronics"}
         &orderBy=price:desc&page=1&pageSize=20
```

**SQL Gerado (eficiente com índice `IX_Product_Category_Price`):**
```sql
SELECT * FROM Products
WHERE Category = 'Electronics'
ORDER BY Price DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY
```

O índice `IX_Product_Category_Price` cobre completamente esta query!

## Resumo

```csharp
// ✅ Índices documentados e centralizados
// ✅ Type-safe com expressions
// ✅ Aplicação automática ao EF Core
// ✅ Suporte a índices compostos, únicos e filtrados
// ✅ Preparado para validação de ordenação

cfg.ConfigureIndexes(indexes =>
{
    indexes.HasIndex(p => p.Price);
    indexes.HasIndex(idx => {
        idx.HasField(p => p.Category);
        idx.HasField(p => p.Price, IndexSortOrder.Descending);
    });
});
```
