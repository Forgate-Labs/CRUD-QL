using CrudQL.Service.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CrudQL.Service.Indexes;

internal sealed class CrudQlModelCustomizer : ModelCustomizer
{
    private readonly ICrudEntityRegistry registry;

    public CrudQlModelCustomizer(
        ModelCustomizerDependencies dependencies,
        ICrudEntityRegistry registry)
        : base(dependencies)
    {
        this.registry = registry;
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.ApplyCrudQlIndexes(registry);
    }
}
