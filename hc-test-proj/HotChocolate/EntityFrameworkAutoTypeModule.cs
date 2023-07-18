using System.Reflection;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace hc_test_proj.HotChocolate;

public class EntityFrameworkAutoTypeModule : ITypeModule
{
    // The Database Context to automatically create schema from.
    private readonly DbContext _dbContext;

    public EntityFrameworkAutoTypeModule(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        // This container will be passed around recursively while creating the object-graph/definitions.
        var typeContainer = new Dictionary<string, ObjectTypeDefinition>();

        // Create the definition of the root query type extension.
        var queryDefinition = CreateQueryTypeDefinition(typeContainer);
        
        // Create it as an object type extension, so it will actually extend the Query type.
        var queryExtension = ObjectTypeExtension.CreateUnsafe(queryDefinition);

        // Create all the object types from our definitions in the container, append the Query extension.
        return typeContainer.Values
            .Select(ObjectType.CreateUnsafe)
            .Append<ITypeSystemMember>(queryExtension)
            .ToList()
            .AsReadOnly();
    }

    public event EventHandler<EventArgs>? TypesChanged;

    /// <summary>
    /// Automatically creates an extension for the Root Query Type based on Entity Framework metadata.
    /// </summary>
    /// <returns></returns>
    private ObjectTypeDefinition CreateQueryTypeDefinition(Dictionary<string, ObjectTypeDefinition> typesContainer)
    {
        var rqt = new ObjectTypeDefinition(OperationTypeNames.Query, "The root query type.");

        // Add all entity types to our query type.
        foreach (var entityType in this._dbContext.Model.GetEntityTypes())
        {
            /*/*entityType.DumpToConsole();#1#
            Console.WriteLine($"Name: {entityType.Name}");
            // Base types
            Console.WriteLine(
                $"Base types: {string.Join(',', entityType.GetAllBaseTypes().Select(t => t.Name))}");
            // Scalar properties
            Console.WriteLine(
                $"Properties: {string.Join(',', entityType.GetProperties().Select(p => p.Name))}");
            // Relations/Navigations
            Console.WriteLine(
                $"Relations/Navigations: {string.Join(',', entityType.GetNavigations().Select(n => n.Name))}");*/

            Console.WriteLine($"Current iteration: {entityType.Name}");
            
            // Create an item GraphQL Type Definition.
            var itemTypeDefinition = GetOrCreateItemObjectTypeDefinition(entityType, typesContainer, relationsShouldBeCreated: true );

            // Create a field definition for the Root Query based on the type definition.
            var itemFieldDefinition = new ObjectFieldDefinition(
                name: entityType.ItemName(),
                type: TypeReference.Parse(itemTypeDefinition.Name),
                resolver: async fr =>
                {
                    var idArg = fr.ArgumentValue<int>("id");
                    var entity = await this._dbContext.FindAsync(entityType.ClrType, idArg);

                    if (entity is null) return null;

                    var entry = this._dbContext.Entry(entity);

                    return entry.Entity;
                }
            );

            // Add an id argument to resolve the item.
            itemFieldDefinition.Arguments.Add(new ArgumentDefinition("id", $"Auto generated for: {entityType.Name}",
                TypeReference.Parse("Int!")));

            // Add the field to the root query.
            rqt.Fields.Add(itemFieldDefinition);

            // Create a collection field definition for the Root Query based on the type definition.
            var collectionFieldDefinition = new ObjectFieldDefinition(
                name: entityType.CollectionName(),
                type: TypeReference.Parse($"[{itemTypeDefinition.Name}]"),
                resolver: async fr =>
                {
                    // ! Super ugly EF hack because Microsoft doesn't expose a non-generic Set method.
                    var method = this._dbContext.GetType().GetMethod("Set", new Type[0])
                        .MakeGenericMethod(entityType.ClrType);

                    var dbSet = method.Invoke(this._dbContext, new object[0]);

                    MethodInfo castMethod =
                        typeof(Program).GetMethod("Cast").MakeGenericMethod(entityType.ClrType);

                    var castedObject = castMethod.Invoke(null, new object[] { dbSet });

                    MethodInfo toListMethod = typeof(Enumerable).GetMethod("ToList")
                        .MakeGenericMethod(entityType.ClrType);

                    return toListMethod.Invoke(null, new object[] { castedObject });
                });

            // Add the field to the root query.
            rqt.Fields.Add(collectionFieldDefinition);

            // ? Register the item type as a type in the container.
            // ^ Maybe not necessary if we can just create a list type from any object type.
        }

        return rqt;
    }

    
    
    private ObjectTypeDefinition GetOrCreateItemObjectTypeDefinition(IEntityType entityType,
        Dictionary<string, ObjectTypeDefinition> typesContainer, bool relationsShouldBeCreated = true)
    {
        Console.WriteLine($"Get Or Create for: {entityType.Name}");

        // If this type already exists, no need to re-create it or register it again, just returns it definition.
        // NB: This short-circuit should not happen if relations should be created.
        var typeAlreadyExists = typesContainer.TryGetValue(entityType.ItemName(), out var existingItemDefinition);
        
        
        if (!relationsShouldBeCreated && typeAlreadyExists)
        {
            Console.WriteLine($"Type for {entityType.ItemName()} already exists and relations should not be created, returning the existing.");
            return existingItemDefinition!;
        }

        // E.g. Api.Entity.User => User.
        var typeName = entityType.ItemName();

        // Create the object type definition for the item. (or re-use existing...)
        var entityItemTypeDefinition = existingItemDefinition ?? new ObjectTypeDefinition(typeName, $"Auto generated item type for: {entityType.Name}");

        // Loop over the scalar properties defined, and add them.
        AddScalarProperties(entityType, entityItemTypeDefinition);

        // Lets add relations to the item type..
        if (relationsShouldBeCreated)
        {
            AddManyToOneProperties(entityType, typesContainer, entityItemTypeDefinition);
            AddOneToManyProperties(entityType, typesContainer, entityItemTypeDefinition);
        }

        // Finally register the type.
        Console.WriteLine($"Registering Outer: {entityType.ItemName()}");
        typesContainer.TryAdd(entityType.ItemName(), entityItemTypeDefinition);

        return entityItemTypeDefinition;
    }
    
    private void AddOneToManyProperties(IEntityType entityType, Dictionary<string, ObjectTypeDefinition> typesContainer,
        ObjectTypeDefinition entityItemTypeDefinition)
    {
        Console.WriteLine($"Adding OneToMany property(ies) \"{string.Join(',', entityType.GetNavigations().Where(n => n.IsCollection).Select(n => n.Name))}\" for {entityType.Name}");
        
        foreach (var relation in entityType.GetNavigations().Where(n => n.IsCollection))
        {
            // Relation is a one-to-many
            // E.g. Api.Entity.User.Reviews => [Review]
            var relationTypeName = relation.TargetEntityType.ItemName();
            Console.WriteLine("Now invoking GetOrCreateItemObjectTypeDefinition recursively (relation is collection)");
            var relationItemTypeDefinition =
                GetOrCreateItemObjectTypeDefinition(relation.TargetEntityType, typesContainer, relationsShouldBeCreated: false);

            var relationFieldDefinition = new ObjectFieldDefinition(
                name: relation.Name,
                description: $"Auto generated field: {relation.Name} for type: {entityType.Name}",
                type: TypeReference.Parse($"[{relationItemTypeDefinition.Name}]"),
                pureResolver: fr =>
                {
                    var parent = fr.Parent<object>();

                    var property = parent.GetType().GetProperty(relation.Name);

                    if (property is null)
                    {
                        throw new InvalidOperationException(
                            $"The property: {relation.Name} does not exist on parent type: {parent.GetType().Name}");
                    }

                    return parent.GetType()?.GetProperty(relation.Name)
                        ?.GetValue(parent,
                            null); // TODO: 1. Ensure that GetProperty will also return a navigation property...
                }
            );

            entityItemTypeDefinition.Fields.Add(relationFieldDefinition);

            // "Register" this created item type in the container.
            // It is possible that this definition+type combo was already registered earlier in the flow, if that is the case it should not be registered.
            typesContainer.TryAdd(relationTypeName, relationItemTypeDefinition);
        }
    }

    /// <summary>
    /// Adds ManyToOne relations to an Object Type definition based on the entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="typesContainer">The container that is passed around that contains the object type definitions.</param>
    /// <param name="entityItemTypeDefinition">The current object type definition for the entity type.</param>
    /// <exception cref="InvalidOperationException"></exception>
    private void AddManyToOneProperties(IEntityType entityType, Dictionary<string, ObjectTypeDefinition> typesContainer,
        ObjectTypeDefinition entityItemTypeDefinition)
    {

        Console.WriteLine($"Adding ManyToOne property(ies) \"{string.Join(',', entityType.GetNavigations().Where(n => !n.IsCollection).Select(n => n.Name))}\" for {entityType.Name}");

        foreach (var relation in entityType.GetNavigations().Where(n => !n.IsCollection))
        {
            // Relation is a many-to-one
            // E.g. Api.Entity.Review => Review.
            var relationTypeName = relation.ClrType.Name.Split('.').Last();
            
            // Example: creating entity type Review then create item type for Author(User) if it does not already exist.
            Console.WriteLine(
                "Now invoking GetOrCreateItemObjectTypeDefinition recursively (relation is not collection)");
            var relationItemTypeDefinition =
                GetOrCreateItemObjectTypeDefinition(relation.TargetEntityType, typesContainer, relationsShouldBeCreated: false);

            var relationFieldDefinition = new ObjectFieldDefinition(
                name: relation.Name,
                description: $"Auto generated field: {relation.Name} for type: {entityType.Name}",
                type: TypeReference.Parse(relationItemTypeDefinition.Name),
                pureResolver: fr =>
                {
                    var parent = fr.Parent<object>();

                    var property = parent.GetType().GetProperty(relation.Name);

                    if (property is null)
                    {
                        throw new InvalidOperationException(
                            $"The property: {relation.Name} does not exist on parent type: {parent.GetType().Name}");
                    }

                    return parent.GetType()?.GetProperty(relation.Name)
                        ?.GetValue(parent,
                            null); // TODO: 1. Ensure that GetProperty will also return a navigation property...
                }
            );

            entityItemTypeDefinition.Fields.Add(relationFieldDefinition);

            // "Register" this created item type in the types container.
            // It is possible that this definition+type combo was already registered earlier in the flow, if that is the case it should not be registered.
            Console.WriteLine($"Registering Inner: {relationItemTypeDefinition.Name}");
            typesContainer.TryAdd(relationItemTypeDefinition.Name, relationItemTypeDefinition);

            Console.WriteLine(
                $"Adding type: {relation.TargetEntityType.ItemName()} to created types.");
        }
    }

    /// <summary>
    /// Adds scalar properties to an Object Type Definition based on the entity type.
    /// </summary>
    /// <param name="entityType"></param>
    /// <param name="entityItemTypeDefinition"></param>
    private static void AddScalarProperties(IEntityType entityType, ObjectTypeDefinition entityItemTypeDefinition)
    {
        foreach (var scalarProperty in entityType.GetProperties())
        {
            // If the scalar property has already been created skip it.
            if (entityItemTypeDefinition.Fields.Any(f => f.Name == scalarProperty.Name))
            {
                continue;
            }
            
            var fieldDefinition = new ObjectFieldDefinition(
                name: scalarProperty.Name,
                description: null,
                type: MapToHotChocolateType(scalarProperty.ClrType),
                pureResolver: fr =>
                {
                    // The parent is the entity itself like a "User" entity or similar.
                    var parent = fr.Parent<object>();

                    // We can resolve the value of a member on this entity using reflection.
                    return parent.GetType()?.GetProperty(scalarProperty.Name)?.GetValue(parent, null);
                }
            );

            entityItemTypeDefinition.Fields.Add(fieldDefinition);
        }
    }


    /// <summary>
    /// Given a C# type, returns the appropriate HotChocolate type.
    /// </summary>
    /// <param name="csharpType">The C# type, string, int, etc.</param>
    /// <returns>An appropriate HotChocolate type.</returns>
    /// <exception cref="InvalidOperationException">If the method cannot map the C# type to a HotChocolate type.</exception>
    private static TypeReference MapToHotChocolateType(Type csharpType)
    {
        // TODO: Handle nullable strings?
        // Nullable
        // Nullable<string>

        if (csharpType == typeof(string))
        {
            return TypeReference.Parse("String!");
        }

        if (csharpType == typeof(int))
        {
            return TypeReference.Parse("Int!");
        }

        if (csharpType == typeof(int?))
        {
            return TypeReference.Parse("Int");
        }

        throw new InvalidOperationException($"Unable to map type: {csharpType.Name} to a HotChocolate type.");
    }

    private class TypeAndDefinition
    {
        public ITypeSystemMember TypeMember { get; }

        public ObjectTypeDefinition TypeDefinition { get; }

        public TypeAndDefinition(ITypeSystemMember typeMember, ObjectTypeDefinition typeDefinition)
        {
            TypeMember = typeMember;
            TypeDefinition = typeDefinition;
        }
    }


}