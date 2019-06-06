using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Alumis.Typescript.Attributes;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Runtime.CompilerServices;
using System.Collections;

namespace Alumis.TypeScript.Generator
{
    class Program
    {        
        static ConfigJson _configJson;
        static HashSet<Type> _types = new HashSet<Type>();
        static StreamWriter _typingsOutput;
        static List<StreamWriter> _typeScriptOutputList = new List<StreamWriter>();
        static OrderedDictionary _compiledTypes = new OrderedDictionary();

        static string AbsoluteTypingsOutputPath
        {
            get
            {
                return Path.Combine(Directory.GetCurrentDirectory(), _configJson.TypingsOutputPath);
            }
        }

        static string AbsoluteTypeScriptOutputPath
        {
            get
            {
                return Path.Combine(Directory.GetCurrentDirectory(), _configJson.TypeScriptOuputPath);
            }
        }

        const string INDENTATION = "    ";
        const string DEFAULT_CONFIG_FILE_NAME = "tsgenerator.json";
        const string DEFAULT_TYPINGS_FILE_NAME = "server-types.d.ts";

        static void Main(string[] args)
        {
            #if DEBUG
                Directory.SetCurrentDirectory(Path.Combine(Directory.GetCurrentDirectory(),"../whistleportal/WhistlePortal.Web"));
            #endif

            ProcessArgs(args);

            var currentDirectory = Directory.GetCurrentDirectory();

            if (_configJson == null)
                _configJson = ReadConfigFile(Path.Combine(currentDirectory, DEFAULT_CONFIG_FILE_NAME));

            _typingsOutput = File.CreateText(AbsoluteTypingsOutputPath);

            var assembly = Assembly.LoadFrom(Path.Combine(currentDirectory, _configJson.AssemblyPath));

            CompileTypesOfAssembly(assembly);
            FlushOutput();
        }

        static void ProcessArgs(string[] args)
        { 
            if (args.Length == 0)            
                return;

            for(int i = 0; i < args.Length; ++i)
            {
                var a = args[i];

                if (a == "--config")
                {
                    if (i == args.Length - 1)
                        throw new ArgumentException("Missing required path value for the --config argument.");
                    
                    var path = args[i++];
                    var absolutePath = Path.Combine(Directory.GetCurrentDirectory(), path);

                    _configJson = ReadConfigFile(absolutePath);

                    Directory.SetCurrentDirectory(Path.GetDirectoryName(absolutePath));
                }
            }
        }

        static ConfigJson ReadConfigFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new ArgumentException($"Could not find config file in {path}.");
            }

            var configText = File.ReadAllText(path);
            var configJson = JsonConvert.DeserializeObject<ConfigJson>(configText);

            var exceptions = new List<Exception>();

            if (string.IsNullOrEmpty(configJson.TypeScriptOuputPath))
            {
                exceptions.Add(new ArgumentNullException($"Config file is missing required property {nameof(configJson.TypeScriptOuputPath)}."));
            }

            if (!configJson.TypeScriptOuputPath.EndsWith('/'))
                configJson.TypeScriptOuputPath += '/';

            if (string.IsNullOrEmpty(configJson.TypingsOutputPath))
            {
                exceptions.Add(new ArgumentException($"Config file is missing required property {nameof(configJson.TypingsOutputPath)}"));
            }

            if (!configJson.TypingsOutputPath.EndsWith(".d.ts"))
            {
                configJson.TypingsOutputPath += DEFAULT_TYPINGS_FILE_NAME;
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }

            return configJson;
        }

        static void FlushOutput()
        {
            foreach (DictionaryEntry e in _compiledTypes)
            {
                _typingsOutput.Write(((CompiledType)e.Value).Value);
                _typingsOutput.WriteLine();
            }

            _typingsOutput.Flush();

            foreach (var s in _typeScriptOutputList)
                s.Flush();
        }

        static string CalculateRelativeFilePath(string absoluteStartPath, string absoluteEndPath)
        {
            return new Uri(absoluteStartPath).MakeRelativeUri(new Uri(absoluteEndPath)).ToString();
        }

        static void CompileTypesOfAssembly(Assembly assembly)
        {
            var assemblyTypes = assembly.GetTypes();
            var controllerTypes = assemblyTypes.Where(t => typeof(Controller).IsAssignableFrom(t)).ToList();

            foreach (var c in controllerTypes)
                CompileControllerType(c);     

            // TODO: compile 'Hub'                
        }

        static void CompileControllerType(Type controllerType)
        {
            var typeScriptAttribute = controllerType.GetCustomAttribute<TypeScriptAttribute>();

            if (typeScriptAttribute != null && !typeScriptAttribute.Include)
                return;
            
            var typeName = controllerType.Name;
            string controllerName;
            
            if (typeName.EndsWith("Controller"))
                controllerName = typeName.Substring(0, typeName.IndexOf("Controller"));
            
            else controllerName = typeName;

            var className = $"{controllerName}Api";
            var absoluteTypeScriptFilePath = $"{AbsoluteTypeScriptOutputPath}{className}.ts";

            if (File.Exists(absoluteTypeScriptFilePath))
                File.Delete(absoluteTypeScriptFilePath);

            var streamOutput = File.CreateText(absoluteTypeScriptFilePath);
            streamOutput.WriteLine("import { CancellationToken } from '@alumis/cancellationtoken';");
            streamOutput.WriteLine("import { getJsonAsync, postAsync, postParseJsonAsync } from '@alumis/http';"); 

            streamOutput.WriteLine("");
            streamOutput.WriteLine($"export class {className} {{");

            var methodInfoList = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.DeclaringType != typeof(Controller) && typeof(Controller).IsAssignableFrom(m.DeclaringType))
                .Where(m => m.GetBaseDefinition() == m).ToList();

            foreach(var m in methodInfoList)
            {
                typeScriptAttribute = m.GetCustomAttribute<TypeScriptAttribute>();

                if (typeScriptAttribute != null && !typeScriptAttribute.Include) 
                    continue;

                var parameters = m.GetParameters().ToList();
                streamOutput.WriteLine($"{INDENTATION}");
                streamOutput.WriteLine($"{INDENTATION}static async {CamelCase(m.Name)}Async(data: {{{string.Join(", ", parameters.Select(p => CompileParameter(p)))}}}, cancellationToken?: CancellationToken) {{");                
                streamOutput.WriteLine($"{INDENTATION.Repeat(2)}");
                streamOutput.WriteLine($"{INDENTATION.Repeat(2)}const args = <IHttpOptions>{{ url: '{controllerName}/{m.Name}', data: data }};");
                streamOutput.WriteLine($"{INDENTATION.Repeat(2)}");

                if (m.GetCustomAttribute<HttpPostAttribute>() != null)
                {
                    var returnType = GetMethodReturnType(m);

                    if (returnType == typeof(void))                    
                        streamOutput.WriteLine($"{INDENTATION.Repeat(2)}return await postAsync(args, cancellationToken)");

                    else
                        streamOutput.WriteLine($"{INDENTATION.Repeat(2)}return await postParseJsonAsync<{GetMemberTypeName(returnType)}>(args, cancellationToken);");
                    
                }
                else
                {                                        
                    var returnType = GetMethodReturnType(m);

                    if (returnType == typeof(void))                    
                        streamOutput.WriteLine($"{INDENTATION.Repeat(2)}return await getAsync(args, cancellationToken);");
                    
                    else                    
                        streamOutput.WriteLine($"{INDENTATION.Repeat(2)}return await getJsonAsync<{GetMemberTypeName(returnType)}>(args, cancellationToken);");                
                }

                // method end
                streamOutput.WriteLine($"{INDENTATION}}}");           
            }

            streamOutput.WriteLine("}");
            streamOutput.WriteLine("");

            _typeScriptOutputList.Add(streamOutput);
        }

        static Type GetMethodReturnType(MethodInfo methodInfo)
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

        static string GetMemberTypeName(Type type)
        {
            if (type == typeof(object))
                return "any";

            type = GetUnderlyingNullableType(type);

            if (type.IsEnum)
            {
                if (!_types.Contains(type))
                    Debugger.Break();

                CompileType(type);
                return ((CompiledType)_compiledTypes[type]).Name;
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

            if (type == typeof(DateTimeOffset))
                return "Date | string";

            var dictionaryArgumentTypes = GetDictionaryArguments(type);

            if (dictionaryArgumentTypes != null)
                return $"{{ [key: {GetMemberTypeName(dictionaryArgumentTypes.Item1)}]: {GetMemberTypeName(dictionaryArgumentTypes.Item2)} }}";

            var enumerableArgumentType = GetEnumerableArgument(type);

            if (enumerableArgumentType != null)
                return GetMemberTypeName(enumerableArgumentType) + "[]";

            CompileType(type);
            return ((CompiledType)_compiledTypes[type]).Name;
        }

        static void CompileType(Type type)
        {
            if (_compiledTypes.Contains(type)) // May have been compiled earlier in the process due to inheritance
                return;

            var compiledType = new CompiledType() { Name = GetTypeName(type) };

            _compiledTypes[type] = compiledType; // Add dummy value

            var sb = new StringBuilder();

            if (type.IsEnum)
            {
                sb.AppendLine($"const enum {compiledType.Name} {{");

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

                    firstLine += $"extends {string.Join(", ", inherits.Select(t2 => ((CompiledType)_compiledTypes[t2]).Name))} ";
                }

                firstLine += "{";

                sb.AppendLine(firstLine);

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
            sb.AppendLine("");
            compiledType.Value = sb.ToString();
            _compiledTypes[type] = compiledType;
        }

        static bool ShouldIncludeProperty(PropertyInfo property)
        {
            var attr = property.GetCustomAttribute<TypeScriptAttribute>();

            if (attr != null && !attr.Include)
                return false;

            if (property.PropertyType == typeof(object))
                return true;

            var type = GetUnderlyingNullableType(property.PropertyType);

            if (type.IsEnum)
                return _types.Contains(type);

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
                    if (_types.Contains(type))
                        goto valueType;

                    else return false;
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
                    return _types.Contains(type);

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
                    return _types.Contains(type);

                if (type == typeof(DateTimeOffset))
                    return true;

                return false;
            }

            if (type.IsClass || type.IsInterface)
                return _types.Contains(type);

            return false;
        }

        static string CompileParameter(ParameterInfo parameter)
        {
            var line = CamelCase(parameter.Name);

            //if (parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>))
            //    line += "?";

            line += ": " + GetMemberTypeName(parameter.ParameterType);
            return line;
        }

        static Type GetUnderlyingNullableType(Type type)
        {
            for (; ; )
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                    type = Nullable.GetUnderlyingType(type);

                else return type;
            }
        }

        static Tuple<Type, Type> GetDictionaryArguments(Type type)
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

        static Type GetEnumerableArgument(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            return type.GetInterfaces()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(t => t.GenericTypeArguments[0]).FirstOrDefault();
        }

        static bool IsTypeEnumerable(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return true;

            return type.GetInterfaces().Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)).Any();
        }

        static Type GetTaskArgument(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                return type.GetGenericArguments()[0];

            return type.GetInterfaces()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
                .Select(t => t.GenericTypeArguments[0]).FirstOrDefault()
                
                ?? typeof(void);
        }

        static string GetTypeName(Type type)
        {
            return type.Name;
        }
        
        static string CamelCase(string s)
        {
            if (s == null)
                throw new ArgumentException();

            if (2 <= s.Length && char.IsUpper(s[0]) && char.IsUpper(s[1]))
                return s;

            if (1 <= s.Length)
                return char.ToLowerInvariant(s[0]) + s.Substring(1);

            return s;
        }
    }
}
