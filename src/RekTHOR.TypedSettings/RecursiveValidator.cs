using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TypedSettings;

internal static class RecursiveValidator
{
    public static List<string> Validate(object root, string sectionName)
    {
        List<string> errors = [];
        Visit(root, sectionName, errors, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return errors;
    }

    private static void Visit(object node, string path, List<string> errors, HashSet<object> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        List<ValidationResult> results = [];
        Validator.TryValidateObject(node, new ValidationContext(node), results, validateAllProperties: true);
        errors.AddRange(results.Select(r => $"{path}: {r.ErrorMessage ?? "is invalid."}"));

        foreach (PropertyInfo property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value = property.GetValue(node);

            if (value is IEnumerable items and not string)
            {
                int index = 0;

                foreach (object? item in items)
                {
                    if (IsNested(item))
                    {
                        Visit(item!, $"{path}:{property.Name}:{index}", errors, visited);
                    }

                    index++;
                }
            }
            else if (IsNested(value))
            {
                Visit(value!, $"{path}:{property.Name}", errors, visited);
            }
        }
    }

    private static bool IsNested(object? value) =>
        value?.GetType() is { IsClass: true } type
        && type != typeof(string)
        && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true;
}
