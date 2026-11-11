using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal static class L
    {
        public const string ProductName = "RAM HAX";
        private static readonly string[] codes = { "en", "ar", "de", "fr" };
        private static readonly CultureInfo[] cultures = {
            CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("ar-EG"),
            CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("fr-FR")
        };
        private static readonly Dictionary<string, string[]> translations = LoadTranslations();
        private static volatile int selected = 1;

        public static string Language { get { return codes[selected]; } }
        public static int Index { get { return selected; } }
        public static string CodeAt(int index) { return codes[index]; }
        public static CultureInfo Culture { get { return cultures[selected]; } }
        public static bool Rtl { get { return Culture.TextInfo.IsRightToLeft; } }
        public static RightToLeft Direction { get { return Rtl ? RightToLeft.Yes : RightToLeft.No; } }

        public static void UseLanguage(string code)
        {
            int index = Array.IndexOf(codes, code);
            selected = index < 0 ? 1 : index;
            System.Threading.Thread.CurrentThread.CurrentUICulture = Culture;
        }

        public static string Text(string key)
        {
            string[] row;
            return translations.TryGetValue(key, out row) ? row[selected] : key;
        }

        public static string Format(string key, params object[] values)
        {
            return String.Format(Culture, Text(key), values);
        }

        public static string InDevelopment(string key)
        {
            return Format("{0} (under development)", Text(key));
        }

        private static Dictionary<string, string[]> LoadTranslations()
        {
            var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
            using (var reader = new StreamReader(typeof(L).Assembly.GetManifestResourceStream("App.Translations.tsv")))
            {
                reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] columns = line.Split('\t');
                    if (columns.Length != 5) throw new InvalidDataException("Invalid translation row: " + line);
                    for (int i = 0; i < columns.Length; i++)
                        columns[i] = columns[i].Replace("\\n", "\n");
                    rows.Add(columns[0], new[] { columns[1], columns[2], columns[3], columns[4] });
                }
            }
            return rows;
        }
    }
}
