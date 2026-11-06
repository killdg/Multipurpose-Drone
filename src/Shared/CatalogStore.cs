using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MultipurposeDrone.LanguagePack
{
    internal sealed class Catalog
    {
        private readonly Dictionary<string, Dictionary<string, string>> languages;

        internal Catalog(string directory, Func<string, Dictionary<string, string>> parse)
        {
            // Only read the JSON shipped alongside this add-on.
            languages = Directory.EnumerateFiles(directory, "*.json").ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                path => parse(File.ReadAllText(path)),
                StringComparer.OrdinalIgnoreCase);
        }

        internal string Text(string? locale, string english)
        {
            return Find(locale, english) ?? english;
        }

        internal string? Find(string? locale, string key)
        {
            string code = (locale ?? "en").Replace('_', '-').ToLowerInvariant();
            code = code switch
            {
                "es-mx" or "es-419" => "es-mx",
                "zh" or "zh-cn" or "zh-hans" => "ch",
                _ => code.Split('-')[0]
            };
            code = code switch
            {
                "de" => "ge",
                "ja" => "jp",
                "ko" => "kr",
                "pt" => "po",
                "cs" => "cz",
                "tr" => "tu",
                _ => code
            };
            return languages.TryGetValue(code, out var entries)
                && entries.TryGetValue(key, out var translation)
                && !string.IsNullOrWhiteSpace(translation)
                    ? translation
                    : null;
        }
    }
}
