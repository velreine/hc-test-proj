using Microsoft.EntityFrameworkCore.Metadata;

namespace hc_test_proj.HotChocolate;

public static class ExtensionMethods
{
    public static string ItemName(this IEntityType entityType) => entityType.Name.Split('.').Last();

    public static string CollectionName(this IEntityType entityType) => entityType.Name.Split('.').Last() + 's'; // TODO: Proper pluralization.
}