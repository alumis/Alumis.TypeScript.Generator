using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Alumis.Typescript.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Alumis.TypeScript.Generator
{
    public class AssemblyTypeCompiler : TypeCompilerBase
    {
        AssemblyJson _assemblyJson;
        string _typingsOutputPath;
        string _typeScriptOutputPath;

        public AssemblyTypeCompiler(AssemblyJson assemblyJson, string typingsOutputPath, string typeScriptOutputPath)
        {
            _assemblyJson = assemblyJson;
            _typingsOutputPath = typingsOutputPath;
            _typeScriptOutputPath = typeScriptOutputPath;
        }

        public void CompileAndFlush()
        {
            //var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Directory.GetCurrentDirectory(),_assemblyJson.Path));
            var assembly = Assembly.LoadFrom(Path.Combine(Directory.GetCurrentDirectory(),_assemblyJson.Path));
            var types = assembly.GetTypes();
            var absoluteTypingsPath = Path.Combine(Directory.GetCurrentDirectory(), _typingsOutputPath, _assemblyJson.TypingsFileName);
            var absoluteTypeScriptOutputPath = Path.Combine(Directory.GetCurrentDirectory(), _typeScriptOutputPath);
            var typingsStream = File.CreateText(absoluteTypingsPath);
            typingsStream.WriteLine("");
            var typeScriptStreams = new HashSet<StreamWriter>();

            if (_assemblyJson.Types != null)            
                types = types.Where(t => _assemblyJson.Types.Contains(t.FullName)).ToArray();

            var controllerTypes = types.Where(t => typeof(Controller).IsAssignableFrom(t)).ToList();

            foreach(var t in controllerTypes)
            {
                var typeScriptAttribute = t.GetCustomAttribute<TypeScriptAttribute>();

                if (typeScriptAttribute != null && !typeScriptAttribute.Include)
                    continue;

                var controllerTypeCompiler = new ControllerTypeCompiler(t, absoluteTypeScriptOutputPath);
                typeScriptStreams.Add(controllerTypeCompiler.Compile());
                MergeCompiledTypes(controllerTypeCompiler.CompiledTypes);                 
            }

            var hubTypes = types.Where(t => typeof(Hub).IsAssignableFrom(t)).ToList();

            foreach(var t in hubTypes)
            {
                var typeScriptAttribute = t.GetCustomAttribute<TypeScriptAttribute>();

                if (typeScriptAttribute != null && !typeScriptAttribute.Include)
                    continue;

                if (!_assemblyJson.Hubs.TryGetValue(GetTypeName(t), out HubJson hubJson))
                    throw new ArgumentException($"Missing config for hub {GetTypeName(t)}");

                var hubTypeCompiler = new HubTypeCompiler(t, absoluteTypeScriptOutputPath, hubJson);
                typeScriptStreams.Add(hubTypeCompiler.Compile());
                MergeCompiledTypes(hubTypeCompiler.CompiledTypes);   
            }


            foreach(DictionaryEntry e in CompiledTypes)
            {
                typingsStream.Write(((CompiledType)e.Value).Value);
                typingsStream.WriteLine("");
            }

            typingsStream.Flush();

            foreach(var s in typeScriptStreams)
                s.Flush();
        }

        void MergeCompiledTypes(OrderedDictionary compiledTypes)
        {
            foreach(DictionaryEntry e in compiledTypes)            
                if (!CompiledTypes.Contains(e.Key))                
                    CompiledTypes[e.Key] = e.Value;
        }
    }
}