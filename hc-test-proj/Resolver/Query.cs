namespace hc_test_proj.Resolver;

/// <summary>
/// The Root Query type, dynamically extended at run-time.
/// NB: Important that class name is not changed as our Type Module depends on it being "Query".
/// </summary>
public class Query
{
    public string GetHello() => "World";
}