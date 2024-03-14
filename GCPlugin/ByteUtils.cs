using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GCPlugin
{
    public static class ByteUtils
    {
        private static readonly string[] s_suffix = new string[] { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB", "RB", "QB" }; // Lets hope we don't get to the QB range...
        public static string BytesToString(long bytes)
        {
            if (bytes < 0) return $"-{BytesToString(-bytes)}";
            var result = (double)bytes;
            var index = 0;
            while (result > 1024)
            {
                result /= 1024;
                index++;
            }
            return $"{(index > 0 ? $"{result:F2}" : $"{bytes}")} {s_suffix[index]}";
        }
    }
}
