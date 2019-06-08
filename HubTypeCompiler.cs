using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using Alumis.Typescript.Attributes;
using Microsoft.AspNetCore.SignalR;

namespace Alumis.TypeScript.Generator
{
    public class HubTypeCompiler : TypeCompilerBase
    {
        private readonly Type _hubType;
        private readonly HubJson _hubJson;
        private readonly string _absoluteTypeScriptOutputPath;        

        public HubTypeCompiler(Type hubType, string absoluteTypeScriptOutputPath, HubJson hubJson)
        {
            _hubType = hubType;
            _absoluteTypeScriptOutputPath = absoluteTypeScriptOutputPath;
            _hubJson = hubJson;
        }

        public StreamWriter Compile()
        {
            var absoluteTypeScriptFilePath = Path.Combine(_absoluteTypeScriptOutputPath,$"{HubName}.ts");

            if (File.Exists(absoluteTypeScriptFilePath))
                File.Delete(absoluteTypeScriptFilePath);

            var streamOutput = File.CreateText(absoluteTypeScriptFilePath);
            streamOutput.WriteLine("import * as signalR from '@aspnet/signalr';");

            if (_hubJson.UseMessagePackProtocol)            
                streamOutput.WriteLine("import { MessagePackHubProtocol } from '@aspnet/signalr-protocol-msgpack';");            

            streamOutput.WriteLine("import { o } from '@alumis/observables';");    
            streamOutput.WriteLine("import { CancellationToken, OperationCancelledError } from '@alumis/cancellationtoken';");
            streamOutput.WriteLine("");
            streamOutput.WriteLine($"export class {HubName} {{");
            streamOutput.WriteLine($"{INDENTATION}");
            streamOutput.WriteLine($"{INDENTATION}static connectionState = o(signalR.HubConnectionState.Disconnected);");
            streamOutput.WriteLine($"{INDENTATION}static connection: signalR.HubConnection;");
            streamOutput.WriteLine($"{INDENTATION}");
            streamOutput.WriteLine($"{INDENTATION}static async startAsync() {{");
            streamOutput.WriteLine($"{INDENTATION.Repeat(2)}");
            streamOutput.WriteLine($"{INDENTATION.Repeat(2)}if (!{HubName}.connection) {{");
            streamOutput.WriteLine($"{INDENTATION.Repeat(3)}");
            streamOutput.WriteLine($"{INDENTATION.Repeat(3)}{HubName}.connection = new signalR.HubConnectionBuilder()");
            streamOutput.WriteLine($"{INDENTATION.Repeat(4)}.withUrl('{_hubJson.Route}')");

            if (_hubJson.UseMessagePackProtocol)
                streamOutput.WriteLine($"{INDENTATION.Repeat(4)}.withHubProtocol(new MessagePackHubProtocol())");

            streamOutput.WriteLine($"{INDENTATION.Repeat(4)}.build();");
            streamOutput.WriteLine($"{INDENTATION.Repeat(3)}");
            streamOutput.WriteLine($"{INDENTATION.Repeat(3)}{HubName}.connection.onclose = () => {{");
            streamOutput.WriteLine($"{INDENTATION.Repeat(4)}{HubName}.connectionState.value = signalR.HubConnectionState.Disconnected;");
            streamOutput.WriteLine($"{INDENTATION.Repeat(3)}}};");

            var baseType = _hubType.BaseType;

            if (baseType.IsGenericType)
            {                
                var clientType = baseType.GetGenericArguments()[0];                
                var clientMethodInfoList = clientType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy).ToList();

                foreach(var m in clientMethodInfoList)
                {
                    var typeScriptAttribute = m.GetCustomAttribute<TypeScriptAttribute>();

                    if (typeScriptAttribute != null && !typeScriptAttribute.Include) 
                        continue;

                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}{HubName}.connection.on('{m.Name}', ({CompileMethodInfoParameters(m)}) => {{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}if ({HubName}.{CompileClientMethodHandlerSetName(m)})");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(5)}for(let h of {HubName}.{CompileClientMethodHandlerSetName(m)})");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(6)}h({CompileMethodInfoParameterNames(m)})");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}}});");
                }
            }

            streamOutput.WriteLine($"{INDENTATION.Repeat(2)}}}");
            streamOutput.WriteLine($"{INDENTATION.Repeat(2)}");
            streamOutput.WriteLine($"{INDENTATION.Repeat(2)}await this.connection.start();");
            streamOutput.WriteLine($"{INDENTATION.Repeat(2)}{HubName}.connectionState.value = signalR.HubConnectionState.Connected;");
            streamOutput.WriteLine($"{INDENTATION}}}");

            var methodInfoList = _hubType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.DeclaringType != typeof(Hub) && typeof(Hub).IsAssignableFrom(m.DeclaringType))
                .Where(m => m.GetBaseDefinition() == m).ToList();

            foreach(var m in methodInfoList)
            {
                var typeScriptAttribute = m.GetCustomAttribute<TypeScriptAttribute>();

                if (typeScriptAttribute != null && !typeScriptAttribute.Include) 
                    continue;

                var hubMethodNameAttribute = m.GetCustomAttribute<HubMethodNameAttribute>();
                string hubMethodName;
                
                if (hubMethodNameAttribute != null)
                    hubMethodName = hubMethodNameAttribute.Name;

                else hubMethodName = m.Name;

                var returnType = GetMethodReturnType(m);
                

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ChannelReader<>))
                {                
                    streamOutput.WriteLine($"{INDENTATION}");

                    var returnTypeString = GetMemberTypeName(returnType.GetGenericArguments()[0]);
                    var parameters = m.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)).ToArray();
                    var parametersString =  CompileParameters(parameters);

                    if (parametersString.Length > 0)
                        parametersString += ", ";

                    parametersString += $"callback: (data: {returnTypeString}) => any";
                    parametersString += ", cancellationToken?: CancellationToken";

                    streamOutput.WriteLine($"{INDENTATION}static {CamelCase(hubMethodName)}Async({parametersString}) {{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}return new Promise((resolve, reject) => {{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}const subscription = {HubName}.connection.stream<{returnTypeString}>('{hubMethodName}', {CompileParameterNames(parameters)})");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}.subscribe({{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(5)}next: callback,");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(5)}complete: resolve,");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(5)}error: reject");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}}});");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}if (cancellationToken) {{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}cancellationToken.addListener(cancellationListener);");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}function cancellationListener() {{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(5)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(5)}subscription.dispose();");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(5)}reject(new OperationCancelledError());");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(4)}}}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}}}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}}});");
                    streamOutput.WriteLine($"{INDENTATION}}}");
                }
                else
                {
                    var returnTypeString = GetMemberTypeName(GetMethodReturnType(m));

                    streamOutput.WriteLine($"{INDENTATION}");
                    streamOutput.WriteLine($"{INDENTATION}static {CamelCase(hubMethodName)}Async({CompileMethodInfoParameters(m)}) : Promise<{returnTypeString}> {{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}");

                    string parameterNamesString = "";

                    if (m.GetParameters().Length > 0)
                        parameterNamesString = $", {CompileMethodInfoParameterNames(m)}";

                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}return {HubName}.connection.invoke('{hubMethodName}'{parameterNamesString});");
                    streamOutput.WriteLine($"{INDENTATION}}}");
                }


            }

            if (baseType.IsGenericType)
            {
                var clientType = baseType.GetGenericArguments()[0];                
                var clientMethodInfoList = clientType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetBaseDefinition() == m).ToList();

                foreach(var m in clientMethodInfoList)
                {
                    var typeScriptAttribute = m.GetCustomAttribute<TypeScriptAttribute>();

                    if (typeScriptAttribute != null && !typeScriptAttribute.Include) 
                        continue;

                    streamOutput.WriteLine($"{INDENTATION}");
                    streamOutput.WriteLine($"{INDENTATION}private static {CompileClientMethodHandlerSetName(m)}: Set<({CompileMethodInfoParameters(m)}) => any>;");
                    streamOutput.WriteLine($"{INDENTATION}");
                    streamOutput.WriteLine($"{INDENTATION}static on{m.Name}(handler: ({CompileMethodInfoParameters(m)}) => any) {{");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}if (!{HubName}.{CompileClientMethodHandlerSetName(m)})");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}{HubName}.{CompileClientMethodHandlerSetName(m)} = new Set([handler]);");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(2)}");
                    streamOutput.WriteLine($"{INDENTATION.Repeat(3)}else {HubName}.{CompileClientMethodHandlerSetName(m)}.add(handler);");
                    streamOutput.WriteLine($"{INDENTATION}}}");
                }
            }

            streamOutput.WriteLine("}");
            streamOutput.WriteLine("");

            return streamOutput;
        }

        protected override Type GetMethodReturnType(MethodInfo methodInfo)
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
            }

            return returnType;
        }

        string HubName => $"{GetTypeName(_hubType)}";
        string CompileMethodInfoParameters(MethodInfo methodInfo) => CompileParameters(methodInfo.GetParameters());
        string CompileParameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p => CompileParameter(p)).ToList());
        string CompileMethodInfoParameterNames(MethodInfo methodInfo) => CompileParameterNames(methodInfo.GetParameters());
        string CompileParameterNames(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p => CamelCase(p.Name)));
        string CompileClientMethodHandlerSetName(MethodInfo methodInfo) => $"_{CamelCase(methodInfo.Name)}HandlerSet";
    }
}