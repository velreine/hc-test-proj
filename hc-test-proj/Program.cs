using System.Reflection;
using hc_test_proj.Database;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace hc_test_proj;

public class Program
    {
        public static readonly Dictionary<string, IOutputType> CreatedTypes = new();

        // Test if this can prevent the schema from being created over and over.
        private static ObjectType? CreatedQueryType = null;


        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            
            // Add services to the container.
            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Add CodaContext as an available service.
            builder.Services.AddScoped<MyDbContext>();

            var dbContext = new MyDbContext();

            
            // Create the GraphQL Schema.
            var schema = await builder.Services
                    .AddGraphQLServer()
                    .ModifyOptions(o => { })
                    .ConfigureSchema(s => { s.AddQueryType(CreateQueryType(dbContext)); })
                    .BuildSchemaAsync()
                ;

            var executor = schema.MakeExecutable();
            var result =
                await executor.ExecuteAsync("{ Review(id: 1) { Id Content Rating AuthorId Author { Id Username } } }");
            var x = schema.Print();
            Console.WriteLine(x);

            Console.WriteLine(result.ToJson());


            foreach (var field in schema.QueryType.Fields)
            {
                Console.WriteLine(
                    $"Field: Name: {field.Name}, Type: {field.Type}, Declaring Type: {field.DeclaringType.Name}");
            }

            Console.WriteLine(schema.QueryType.Print());

            var app = builder.Build();
            
            // Add GraphQL endpoint.
            app.MapGraphQL();


            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
                app.UseCors(pol =>
                    pol
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                );
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();
            
            Console.WriteLine($"Current app environment is: {app.Environment.EnvironmentName}");
            /*Console.WriteLine($"Allowed hosts: {}");*/

            using (var db = new MyDbContext())
            {
                Console.WriteLine("Printing all users:");
                foreach (var user in db.Users)
                {
                    Console.WriteLine($"Id: {user.Id}, Username: {user.Username}, Password: {user.Password}");
                }
            }

            await app.RunAsync();
        }

        private static ObjectType CreateQueryType(MyDbContext _codaContext)
        {
            if (CreatedQueryType is not null) return CreatedQueryType;

            var queryType = new ObjectType(rqt =>
            {
                rqt.Name(OperationTypeNames.Query).Description("The root query type.");

                foreach (var entityType in _codaContext.Model.GetEntityTypes())
                {
                    /*entityType.DumpToConsole();*/
                    Console.WriteLine($"Name: {entityType.Name}");


                    // Base types
                    Console.WriteLine(
                        $"Base types: {string.Join(',', entityType.GetAllBaseTypes().Select(t => t.Name))}");

                    // Scalar properties
                    Console.WriteLine(
                        $"Properties: {string.Join(',', entityType.GetProperties().Select(p => p.Name))}");

                    // Relations/Navigations
                    Console.WriteLine(
                        $"Relations/Navigations: {string.Join(',', entityType.GetNavigations().Select(n => n.Name))}");


                    // The GraphQL Item Type
                    var hotChocolateType = CreateGraphQlItemType(entityType);

                    // Register the item type.
                    // TODO: Figure out if possible not to create the schema twice.
                    CreatedTypes.TryAdd(entityType.Name.Split('.').Last(), hotChocolateType);

                    // The GraphQL Collection Type
                    var listType = new ListType(hotChocolateType);

                    // Add the item type to the Root Query Type.
                    rqt
                        .Field(entityType.Name.Split('.').Last())
                        .Argument("id", arg => arg.Type<NonNullType<IntType>>())
                        .Type(hotChocolateType)
                        .Resolve(fr =>
                        {
                            var idArg = fr.ArgumentValue<int>("id");
                            var entry = _codaContext.Entry(_codaContext.Find(entityType.ClrType, idArg));

                            return entry.Entity;
                        });

                    // Add the collection type to the Root Query Type.
                    rqt
                        .Field(entityType.Name.Split('.').Last() + 's') // TODO: Proper pluralization
                        .Type(listType)
                        .Resolve(fr =>
                        {
                            // ! Super ugly EF hack because Microsoft doesn't expose a non-generic Set method.
                            var method = _codaContext.GetType().GetMethod("Set", new Type[0])
                                .MakeGenericMethod(entityType.ClrType);

                            var dbSet = method.Invoke(_codaContext, new object[0]);

                            MethodInfo castMethod =
                                typeof(Program).GetMethod("Cast").MakeGenericMethod(entityType.ClrType);

                            var castedObject = castMethod.Invoke(null, new object[] { dbSet });

                            MethodInfo toListMethod = typeof(Enumerable).GetMethod("ToList")
                                .MakeGenericMethod(entityType.ClrType);

                            return toListMethod.Invoke(null, new object[] { castedObject });
                        });
                }

                /*// testing fields.
                for (int i = 0; i < 10; i++)
                {
                    rqt.Field($"field_{i}").Type<IntType>().Resolve(i);
                }*/
            });

            CreatedQueryType = queryType;

            return queryType;
        }

        public static DbSet<T> Cast<T>(object o) where T : class, new()
        {
            return (DbSet<T>)o;
        }

        private static ObjectType CreateGraphQlItemType(IEntityType entityType)
        {
            // E.g. Api.Entity.User => User.
            var typeName = entityType.Name.Split('.').Last();


            // Create an object type for each Entity Type in our domain.
            var hotChocolateType = new ObjectType(o =>
            {
                o.Name(typeName);


                /*o.Field("test").Type<StringType>().Resolve(_ => "test");
                        o.Field("Username").Type<StringType>().ResolveWith();*/

                // EF automatically includes foreign key properties...
                // ^Eg. AuthorId on Review entity, find a way to resolve/skip.
                foreach (var scalarProperty in entityType.GetProperties())
                {
                    o
                        .Field(scalarProperty.Name)
                        .Type(MapToHotChocolateType(scalarProperty.ClrType))
                        .Resolve(fr =>
                        {
                            // The parent is the entity itself like a "User" entity or similar.
                            var parent = fr.Parent<object>();

                            // We can resolve the value of a member on this entity using reflection.
                            return parent.GetType()?.GetProperty(scalarProperty.Name)?.GetValue(parent, null);
                        })
                        ;
                }


                // Lets add relations to the item type..
                foreach (var relation in entityType.GetNavigations())
                {
                    Console.WriteLine($"Current relation is: {relation.Name}");
                    Console.WriteLine($"Target entity: {relation.TargetEntityType.Name}");

                    if (relation.IsCollection)
                    {
                        Console.WriteLine("The relation is a collection, skipping for now.");
                        continue; // TODO: Handle one-to-many (collections).
                    }
                    else
                    {
                        Console.WriteLine("The relation is not a collection.");

                        // Relation is a many-to-one
                        // E.g. Api.Entity.Review => Review.
                        var relationTypeName = relation.ClrType.Name.Split('.').Last();

                        Console.WriteLine($"Checking if we already have an output type for {relationTypeName}");

                        // If we already created the HotChocolate object type for this entity re-use it, otherwise create a new one.
                        if (CreatedTypes.TryGetValue(relationTypeName, out var existingType))
                        {
                            Console.WriteLine($"The type already exists, so re-using it.");

                            // TODO: Testing....
                            //o.Field(relation.Name).Type<StringType>().Resolve(_ => "hello world");

                            o
                                .Field(relation.Name)
                                .Type(existingType)
                                .Resolve(fr =>
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
                                });
                        }
                        else
                        {
                            Console.WriteLine("The type does not already exist, creating it...");

                            // TODO: Testing....
                            //o.Field(relation.Name).Type<StringType>().Resolve(_ => "hello world");

                            var itemType = CreateGraphQlItemType(relation.TargetEntityType);
                            var xx = 3;
                            var yy = 4;


                            o
                                .Field(relation.Name)
                                .Type(itemType)
                                .Resolve(fr =>
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
                                });

                            // And also "register" this type.
                            // TODO: See if it possible not to create the schema twice.
                            Console.WriteLine(
                                $"Adding type: {relation.TargetEntityType.Name.Split('.').Last()} to created types.");
                            CreatedTypes.TryAdd(relation.TargetEntityType.Name.Split('.').Last(), itemType);
                        }
                    }

                    var x = 2;
                    var y = x + 3;
                }
            });
            return hotChocolateType;
        }

        /// <summary>
        /// Given a C# type, returns the appropriate HotChocolate type.
        /// </summary>
        /// <param name="csharpType">The C# type, string, int, etc.</param>
        /// <returns>An appropriate HotChocolate type.</returns>
        /// <exception cref="InvalidOperationException">If the method cannot map the C# type to a HotChocolate type.</exception>
        private static Type MapToHotChocolateType(Type csharpType)
        {
            if (csharpType == typeof(string))
            {
                return typeof(StringType);
            }

            if (csharpType == typeof(int) || csharpType == typeof(int?))
            {
                return typeof(IntType);
            }

            throw new InvalidOperationException($"Unable to map type: {csharpType.Name} to a HotChocolate type.");
        }
    }