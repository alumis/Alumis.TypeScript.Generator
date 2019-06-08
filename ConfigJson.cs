using System.Collections.Generic;

namespace Alumis.TypeScript.Generator
{

    public class ConfigJson 
    {
        public string TypeScriptOutputPath { get; set; }
        public string TypingsOutputPath { get; set; }
        public AssemblyJson[] Assemblies { get; set; }
    }

    public class AssemblyJson
    {
        public string Path { get; set; }
        public string[] Types { get; set; }
        public string TypingsFileName { get; set;}
        public Dictionary<string, HubJson> Hubs { get; set; }
    }

    public class HubJson
    {
        public string Route { get; set; }
        public bool UseMessagePackProtocol { get; set; }
    }


}