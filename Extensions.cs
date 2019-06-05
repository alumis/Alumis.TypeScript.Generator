using System;
using System.Text;

namespace Alumis.TypeScript.Generator
{
    public static class Extensions
    {
        public static string Repeat(this string str, int n)
        {
            var sb = new StringBuilder();

            for (var i = 0; i < n; ++i)
                sb.Append(str);

            return sb.ToString();
        }
    }
}