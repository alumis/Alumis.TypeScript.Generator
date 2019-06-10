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
using System.Runtime.Loader;

namespace Alumis.TypeScript.Generator
{
    class Program
    {        
        static void Main(string[] args)
        {
            ConfigJson configJson = null;

            for(int i = 0; i < args.Length; ++i)
            {
                var a = args[i];

                if (a == "--config")
                {
                    if (i == args.Length - 1)
                        throw new ArgumentException("Missing required path value for the --config argument.");
                    
                    var path = args[++i];
                    var absoluteConfigPath = Path.Combine(Directory.GetCurrentDirectory(), path);

                    configJson = ReadConfigFile(absoluteConfigPath);

                    Directory.SetCurrentDirectory(Path.GetDirectoryName(absoluteConfigPath));
                }
            }

            var currentDirectory = Directory.GetCurrentDirectory();

            if (configJson == null)
                configJson = ReadConfigFile(Path.Combine(currentDirectory, "tsgenerator.json"));            

            foreach(var a in configJson.Assemblies)            
                new AssemblyTypeCompiler(a, configJson.TypingsOutputPath, configJson.TypeScriptOutputPath).CompileAndFlush();
        }

        public static ConfigJson ReadConfigFile(string absolutePath)
        {
            var attr = File.GetAttributes(absolutePath);

            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                absolutePath = Path.Combine(absolutePath,"tsgenerator.json");            

            if (!File.Exists(absolutePath))            
                    throw new ArgumentException($"Could not find config file: {absolutePath}.");

            var config = JsonConvert.DeserializeObject<ConfigJson>(File.ReadAllText(absolutePath));

            if (string.IsNullOrEmpty(config.TypeScriptOutputPath))            
                throw new ArgumentException("Missing required property 'TypeScriptOutputPath'");

            if (string.IsNullOrEmpty(config.TypingsOutputPath))
                throw new ArgumentException("Missing required property 'TypingsOutputPath'");

            if (config.Assemblies == null)
                throw new ArgumentException("Missing required property 'Assemblies'");

            foreach (var a in config.Assemblies)
            {
                if (string.IsNullOrEmpty(a.Path))
                    throw new ArgumentException("Missing required property 'Path' in 'Assemblies'");

                if (string.IsNullOrEmpty(a.TypingsFileName))
                    throw new ArgumentException("Missing required property 'TypingsFileName' in 'Assemblies'");
            }

            return config;
        }
    }
}
