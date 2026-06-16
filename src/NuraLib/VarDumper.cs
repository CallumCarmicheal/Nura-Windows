using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NuraLib;

public static class TypeExtensions {
    public static string GetFriendlyName(this Type type, bool simplifyNames = false) {
        if (type.IsGenericType) {
            // e.g. "List`1" or "System.Collections.Generic.List`1"
            string defName = simplifyNames
                ? type.GetGenericTypeDefinition().Name
                : type.GetGenericTypeDefinition().FullName!;
            int bt = defName.IndexOf('`');
            string baseName = bt < 0 ? defName : defName.Substring(0, bt);

            // recurse generic arguments
            var args = type.GetGenericArguments()
                           .Select(t => t.GetFriendlyName(simplifyNames));
            return $"{baseName}<{string.Join(", ", args)}>";
        }

        return simplifyNames
            ? type.Name
            : type.FullName ?? type.Name;
    }
}

public static class VarDumper {
    /// <summary>
    /// Dumps any object graph, with options to simplify type names and inline scalar values.
    /// Tuples (ValueTuple) get special header formatting and then expand their elements inside braces.
    /// </summary>
    public static string Dump(
        object? obj,
        bool simplifyNames = false,
        bool optSameLineValue = false,
        int depth = 0,
        int maxDepth = 5
    ) {
        var indent = new string(' ', depth * 2);

        // Null or too deep
        if (obj == null)
            return $"{indent}null";
        if (depth > maxDepth)
            return $"{indent}…";

        var type = obj.GetType();

        // 1) Scalar: primitive, string or enum
        if (type.IsPrimitive || obj is string || type.IsEnum)
            return $"{indent}{type.GetFriendlyName(simplifyNames)}({obj})";

        // 2) ValueTuple special-case
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition().FullName?.StartsWith("System.ValueTuple`") == true) {
            // Grab Item1, Item2, ... fields
            var fields = type
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.Name.StartsWith("Item"))
                .OrderBy(f => f.Name)
                .ToArray();

            // Build header entries "ItemX: TypeName"
            var headers = fields
                .Select(f => {
                    var fldType = f.FieldType;
                    return $"{f.Name}: {fldType.GetFriendlyName(simplifyNames)}";
                })
                .ToArray();

            // Decide inline vs multi-line header
            string header;
            if (headers.Length <= 3) {
                header = $"({string.Join(", ", headers)})";
            } else {
                var sbh = new StringBuilder();
                sbh.AppendLine("#Tuple(");
                foreach (var h in headers)
                    sbh.AppendLine($"  {h},");
                sbh.Append(")");
                header = sbh.ToString();
            }

            // Now build body: each element dumped inside braces
            var sbb = new StringBuilder();
            sbb.AppendLine($"{indent}{header} => {{");
            foreach (var f in fields) {
                var val = f.GetValue(obj);
                bool isScalar = val == null
                    || val.GetType().IsPrimitive
                    || val is string
                    || val.GetType().IsEnum;

                var childIndent = new string(' ', (depth + 1) * 2);
                if (optSameLineValue && isScalar) {
                    // inline scalar
                    var scalar = Dump(val, simplifyNames, optSameLineValue, 0, maxDepth).Trim();
                    sbb.AppendLine($"{childIndent}{f.Name} =>  {scalar}");
                } else {
                    sbb.AppendLine($"{childIndent}{f.Name} =>");
                    sbb.AppendLine(Dump(val, simplifyNames, optSameLineValue, depth + 2, maxDepth));
                }
            }
            sbb.Append($"{indent}}}");
            return sbb.ToString();
        }

        // 3) Collections
        if (obj is IEnumerable enumerable) {
            var sbc = new StringBuilder();
            sbc.AppendLine($"{indent}{type.GetFriendlyName(simplifyNames)}[");
            foreach (var item in enumerable)
                sbc.AppendLine(Dump(item, simplifyNames, optSameLineValue, depth + 1, maxDepth));
            sbc.Append($"{indent}]");
            return sbc.ToString();
        }

        // 4) Complex object
        var sb = new StringBuilder();
        sb.AppendLine($"{indent}{type.GetFriendlyName(simplifyNames)} {{");
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            object? val = null;
            try { val = prop.GetValue(obj); } catch { }

            bool isScalar = val == null
                || val.GetType().IsPrimitive
                || val is string
                || val.GetType().IsEnum;

            var childIndent = new string(' ', (depth + 1) * 2);
            if (optSameLineValue && isScalar) {
                var scalar = Dump(val, simplifyNames, optSameLineValue, 0, maxDepth).Trim();
                sb.AppendLine($"{childIndent}{prop.Name} =>  {scalar}");
            } else {
                sb.AppendLine($"{childIndent}{prop.Name} =>");
                sb.AppendLine(Dump(val, simplifyNames, optSameLineValue, depth + 2, maxDepth));
            }
        }
        sb.Append($"{indent}}}");
        return sb.ToString();
    }
}