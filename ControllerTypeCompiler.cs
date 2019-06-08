using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Alumis.Typescript.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Alumis.TypeScript.Generator
{
    public class ControllerTypeCompiler : TypeCompilerBase
    {
        Type _controllerType;
        string _absoluteTypeScriptOutputPath;

        public ControllerTypeCompiler(Type controllerType, string absoluteTypeScriptOutputPath)
        {
            _controllerType = controllerType;
            _absoluteTypeScriptOutputPath = absoluteTypeScriptOutputPath;
        }

        public StreamWriter Compile()
        {            
            var typeName = GetTypeName(_controllerType);
            string controllerName;
            
            if (typeName.EndsWith("Controller"))
                controllerName = typeName.Substring(0, typeName.IndexOf("Controller"));
            
            else controllerName = typeName;

            var className = $"{controllerName}Api";
            var absoluteTypeScriptFilePath = Path.Combine(_absoluteTypeScriptOutputPath,$"{className}.ts");            

            if (File.Exists(absoluteTypeScriptFilePath))
                File.Delete(absoluteTypeScriptFilePath);

            var streamOutput = File.CreateText(absoluteTypeScriptFilePath);
            streamOutput.WriteLine("import { CancellationToken } from '@alumis/cancellationtoken';");
            streamOutput.WriteLine("import { getJsonAsync, postAsync, postParseJsonAsync, IHttpOptions } from '@alumis/http';"); 
            streamOutput.WriteLine("");
            streamOutput.WriteLine($"export class {className} {{");

            var controllerMethodInfoList = _controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.DeclaringType != typeof(Controller) && typeof(Controller).IsAssignableFrom(m.DeclaringType))
                .Where(m => m.GetBaseDefinition() == m).ToList();

            foreach(var m in controllerMethodInfoList)
            {
                var typeScriptAttribute = m.GetCustomAttribute<TypeScriptAttribute>();

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
                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}return await getJsonAsync<{GetMemberTypeName(GetMethodReturnType(m))}>(args, cancellationToken);");                
                }

                // method end
                streamOutput.WriteLine($"{INDENTATION}}}");           
            }

            streamOutput.WriteLine("}");
            streamOutput.WriteLine("");   

            return streamOutput;         
        }
    }
}