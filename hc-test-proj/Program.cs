using hc_test_proj.Database;
using hc_test_proj.HotChocolate;
using hc_test_proj.Resolver;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;

namespace hc_test_proj;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Add MyDbContext as an available service.
        builder.Services.AddScoped<MyDbContext>();

        // Create the GraphQL Schema.
        var schema = await builder.Services
                .AddGraphQLServer()
                .ModifyOptions(o => { })
                .AddQueryType<Query>()
                .AddTypeModule(_ => new EntityFrameworkAutoTypeModule(new MyDbContext()))
                //.ConfigureSchema(s => { s.AddQueryType(CreateQueryType(dbContext)); })
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

    public static DbSet<T> Cast<T>(object o) where T : class, new()
    {
        return (DbSet<T>)o;
    }
}