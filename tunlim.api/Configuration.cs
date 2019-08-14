using System;
using System.Collections.Generic;
using System.IO;

namespace tunlim.api
{
    internal class Configuration
    {
        internal static string GetContent(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var streamReader = new StreamReader(fs, true))
            {
                return streamReader.ReadToEnd();
            }
        }

        internal static string GetValue(string key)
        {
            var item = Environment.GetEnvironmentVariable(key);

            if (!string.IsNullOrEmpty(item))
                return item;

            var content = GetContent("appsettings.json");
            var json = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
            return json[key];
        }

        internal static string GetApiServer()
        {
            return GetValue("APISERVER");
        }
    }
}
