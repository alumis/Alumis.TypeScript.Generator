using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Alumis.Typescript.Attributes;

namespace Alumis.TypeScript.Generator
{
    public abstract class TypeCompilerBase
    {
        public const string INDENTATION = "    ";

        public OrderedDictionary CompiledTypes = new OrderedDictionary();
        HashSet<Type> _types = new HashSet<Type>();

        protected virtual Type GetMethodReturnType(MethodInfo methodInfo)
        {
            Type returnType;
            var definitelyTypedAttribute = methodInfo.GetCustomAttribute<TypeScriptAttribute>();
            

            if (definitelyTypedAttribute != null && definitelyTypedAttribute.MethodReturnType != null)
                returnType = definitelyTypedAttribute.MethodReturnType;

            else
            {
                var asyncStateMachineAttribute = methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>();

                if (asyncStateMachineAttribute != null)
                    returnType = GetTaskArgument(methodInfo.ReturnType);

                else returnType = methodInfo.ReturnType;

                if (returnType != typeof(string))
                    returnType = typeof(void);
            }

            return returnType;
        }

        protected Type GetTaskArgument(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                return type.GetGenericArguments()[0];

            return type.GetInterfaces()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
                .Select(t => t.GenericTypeArguments[0]).FirstOrDefault()
                
                ?? typeof(void);
        }

        protected string GetTypeName(Type type)
        {
            return type.Name;
        }

        protected string CamelCase(string s)
        {
            if (s == null)
                throw new ArgumentException();

            if (2 <= s.Length && char.IsUpper(s[0]) && char.IsUpper(s[1]))
                return s;

            if (1 <= s.Length)
                return char.ToLowerInvariant(s[0]) + s.Substring(1);

            return s;
        }

        protected bool IsTypeEnumerable(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return true;

            return type.GetInterfaces().Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)).Any();
        }

        protected Type GetEnumerableArgument(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            return type.GetInterfaces()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(t => t.GenericTypeArguments[0]).FirstOrDefault();
        }

        protected Tuple<Type, Type> GetDictionaryArguments(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                var args = type.GetGenericArguments();

                return new Tuple<Type, Type>(args[0], args[1]);
            }

            return type.GetInterfaces()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                .Select(t => new Tuple<Type, Type>(t.GenericTypeArguments[0], t.GenericTypeArguments[1])).FirstOrDefault();
        }

        protected Type GetUnderlyingNullableType(Type type)
        {
            for (; ; )
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                    type = Nullable.GetUnderlyingType(type);

                else return type;
            }
        }

        protected string CompileParameter(ParameterInfo parameter)
        {
            var line = CamelCase(parameter.Name);

            //if (parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>))
            //    line += "?";

            line += ": " + GetMemberTypeName(parameter.ParameterType);
            return line;
        }

        protected bool ShouldIncludeProperty(PropertyInfo property)
        {
            if (property.Name == "N")
                Debugger.Break();

            var attr = property.GetCustomAttribute<TypeScriptAttribute>();

            if (attr != null && !attr.Include)
                return false;

            if (property.PropertyType == typeof(object))
                return true;

            var type = GetUnderlyingNullableType(property.PropertyType);

            if (type.IsEnum)
                return true;

            var typeCode = Type.GetTypeCode(type);

            if (TypeCode.Boolean <= typeCode && typeCode <= TypeCode.String) // Let's hope TypeCode never changes
                return true;

            if (type == typeof(DateTimeOffset))
                return true;

            var dictionaryArgumentTypes = GetDictionaryArguments(type);

            if (dictionaryArgumentTypes != null)
            {
                type = GetUnderlyingNullableType(dictionaryArgumentTypes.Item1);
                typeCode = Type.GetTypeCode(type);

                if (TypeCode.Boolean <= typeCode && typeCode <= TypeCode.String)
                    goto valueType;

                if (type.IsClass || type.IsInterface || type.IsEnum)
                {
                    //if (_types.Contains(type))
                    goto valueType;

                    //else return false;
                }

                if (type == typeof(DateTimeOffset))
                    goto valueType;

                return false;

                valueType:

                type = GetUnderlyingNullableType(dictionaryArgumentTypes.Item2);
                typeCode = Type.GetTypeCode(type);

                if (TypeCode.Boolean <= typeCode && typeCode <= TypeCode.String)
                    return true;

                if (type.IsClass || type.IsInterface || type.IsEnum)
                    return true;

                if (type == typeof(DateTimeOffset))
                    return true;

                return false;
            }

            var enumerableArgumentType = GetEnumerableArgument(type);

            if (enumerableArgumentType != null)
            {
                type = GetUnderlyingNullableType(enumerableArgumentType);
                typeCode = Type.GetTypeCode(type);

                if (TypeCode.Boolean <= typeCode && typeCode <= TypeCode.String)
                    return true;

                if (type.IsClass || type.IsInterface || type.IsEnum)
                    return true;

                if (type == typeof(DateTimeOffset))
                    return true;

                return false;
            }

            if (type.IsClass || type.IsInterface)
                return true;

            return false;
        }

        protected string GetMemberTypeName(Type type)
        {
            if (type == typeof(object))
                return "any";

            type = GetUnderlyingNullableType(type);

            if (type.IsEnum)
            {
                CompileType(type);
                return ((CompiledType)CompiledTypes[type]).Name;
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:

                case TypeCode.SByte:

                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:

                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:

                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:

                    return "number";

                case TypeCode.String:

                    return "string";

                case TypeCode.Boolean:

                    return "boolean";

                case TypeCode.DateTime:

                    return "Date | string";
            }

            if (type == typeof(void))
                return "void";

            if (type == typeof(DateTimeOffset))
                return "Date | string";

            var dictionaryArgumentTypes = GetDictionaryArguments(type);

            if (dictionaryArgumentTypes != null)
                return $"{{ [key: {GetMemberTypeName(dictionaryArgumentTypes.Item1)}]: {GetMemberTypeName(dictionaryArgumentTypes.Item2)} }}";

            var enumerableArgumentType = GetEnumerableArgument(type);

            if (enumerableArgumentType != null)
                return GetMemberTypeName(enumerableArgumentType) + "[]";

            CompileType(type);
            return ((CompiledType)CompiledTypes[type]).Name;
        }

        protected void CompileType(Type type)
        {
            if (CompiledTypes.Contains(type)) // May have been compiled earlier in the process due to inheritance
                return;

            var compiledType = new CompiledType() { Name = GetTypeName(type) };

            CompiledTypes[type] = compiledType; // Add dummy value

            var sb = new StringBuilder();

            if (type.IsEnum)
            {
                sb.AppendLine($"declare const enum {compiledType.Name} {{");
                sb.AppendLine("");

                var names = type.GetEnumNames();
                var values = type.GetEnumValues();

                for (var i = 0; i < names.Length; ++i)
                {
                    sb.Append($"{INDENTATION}{names[i]} = {(int)values.GetValue(i)}");

                    if (i != names.Length - 1)
                        sb.Append(",");

                    sb.AppendLine();
                }
            }

            else
            {
                // First line

                if (compiledType.Name == "Object")
                    Debugger.Break();

                var firstLine = $"interface {compiledType.Name} ";
                
                var inherits = type == typeof(object) ? new List<Type>() : type.GetInterfaces().Concat(new[] { type.BaseType }).Where(t => _types.Contains(t)).ToList();

                if (inherits.Any())
                {
                    foreach (var t in inherits)
                        CompileType(t);

                    firstLine += $"extends {string.Join(", ", inherits.Select(t2 => ((CompiledType)CompiledTypes[t2]).Name))} ";
                }

                firstLine += "{";

                sb.AppendLine(firstLine);
                sb.AppendLine("");

                // Body

                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(p2 => ShouldIncludeProperty(p2)))
                {
                    var line = INDENTATION + CamelCase(p.Name);

                    if (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        line += "?";

                    line += ": " + GetMemberTypeName(p.PropertyType) + ";";
                    sb.AppendLine(line);
                }
            }

            sb.AppendLine("}");
            compiledType.Value = sb.ToString();
            CompiledTypes[type] = compiledType;
        }
    }
}