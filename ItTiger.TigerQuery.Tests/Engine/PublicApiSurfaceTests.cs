using System.Collections;
using System.Reflection;

namespace ItTiger.TigerQuery.Tests.Engine;

/// <summary>
/// Guards the first-release boundary: the ordered execution-step model is an
/// internal implementation detail and must not become public API.
/// </summary>
public sealed class PublicApiSurfaceTests
{
    private static readonly Assembly TigerQueryAssembly = typeof(SqlCmdParser).Assembly;

    [Theory]
    [InlineData("ItTiger.TigerQuery.Engine.ExecutionStep")]
    [InlineData("ItTiger.TigerQuery.Engine.ExecuteBatchStep")]
    [InlineData("ItTiger.TigerQuery.Engine.SetOutRouteStep")]
    [InlineData("ItTiger.TigerQuery.Engine.SetErrorRouteStep")]
    [InlineData("ItTiger.TigerQuery.Engine.OutputDirective")]
    [InlineData("ItTiger.TigerQuery.Engine.ExecutionBatch")]
    [InlineData("ItTiger.TigerQuery.Engine.PreparedExecutionPlan")]
    public void ExecutionStepTypesAreNotVisibleOutsideTheAssembly(string typeName)
    {
        var type = TigerQueryAssembly.GetType(typeName, throwOnError: true)!;

        Assert.False(type.IsVisible, $"{typeName} must not be part of the public API surface.");
    }

    [Fact]
    public void NoPublicTypeNamesAScriptStepConcept()
    {
        var offenders = TigerQueryAssembly
            .GetExportedTypes()
            .Where(type =>
                type.Name.Contains("ExecutionStep", StringComparison.Ordinal)
                || type.Name.Contains("RouteStep", StringComparison.Ordinal)
                || type.Name.Contains("OutputDirective", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void PublicParserEnumerationReturnsBatchesOnly()
    {
        var enumerations = typeof(SqlCmdParser)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => IsEnumeration(method.ReturnType))
            .ToList();

        var method = Assert.Single(enumerations);
        Assert.Equal(nameof(SqlCmdParser.ReadBatchesAsync), method.Name);
        Assert.Equal(typeof(SqlBatch), method.ReturnType.GetGenericArguments()[0]);
    }

    [Fact]
    public void NoPublicMemberExposesAnInternalStepType()
    {
        var internalStepTypes = new[]
        {
            "ItTiger.TigerQuery.Engine.ExecutionStep",
            "ItTiger.TigerQuery.Engine.ExecutionBatch",
            "ItTiger.TigerQuery.Engine.PreparedExecutionPlan"
        }
        .Select(name => TigerQueryAssembly.GetType(name, throwOnError: true)!)
        .ToHashSet();

        var offenders = new List<string>();

        foreach (var type in TigerQueryAssembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var referenced = method
                    .GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType)
                    .SelectMany(Unwrap);

                if (referenced.Any(internalStepTypes.Contains))
                {
                    offenders.Add($"{type.FullName}.{method.Name}");
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (Unwrap(property.PropertyType).Any(internalStepTypes.Contains))
                {
                    offenders.Add($"{type.FullName}.{property.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsEnumeration(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IAsyncEnumerable<>)
            || definition == typeof(IEnumerable<>)
            || definition == typeof(IReadOnlyList<>);
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Unwrap))
            {
                yield return argument;
            }
        }
    }
}
