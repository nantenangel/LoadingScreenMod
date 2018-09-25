using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LoadingScreenModTest
{
    sealed class ByNames
    {
        readonly HashSet<string> names = new HashSet<string>();

        public ByNames() { }
        public bool Matches(string name) => names.Contains(name);
        public void AddName(string name) => names.Add(name);
    }

    sealed class ByPatterns
    {
        readonly List<Regex> patterns = new List<Regex>();

        public ByPatterns() { }

        public bool Matches(string name)
        {
            for (int i = 0; i < patterns.Count; i++)
                if (patterns[i].IsMatch(name))
                    return true;

            return false;
        }

        public void AddPattern(string pattern) => patterns.Add(new Regex(pattern));
    }

    sealed class Matcher
    {
        readonly ByNames byNames = new ByNames();
        readonly ByPatterns byPatterns = new ByPatterns();
        readonly ByPatterns[] services, subServices;

        internal Matcher(int serviceCount, int subServiceCount)
        {
            this.services = new ByPatterns[serviceCount];
            this.subServices = new ByPatterns[subServiceCount];
        }

        internal bool Matches(PrefabInfo prefab)
        {
            return false;
        }

        internal static Matcher[] Load(string filePath)
        {
            Dictionary<string, int> servicePrefixes = Util.GetEnumMap(typeof(ItemClass.Service));
            Dictionary<string, int> subServicePrefixes = Util.GetEnumMap(typeof(ItemClass.SubService));
            Matcher skip = new Matcher(servicePrefixes.Count, subServicePrefixes.Count);
            Matcher except = new Matcher(servicePrefixes.Count, subServicePrefixes.Count);
            string[] lines = File.ReadAllLines(filePath);
            Regex syntax = new Regex(@"^(?:([Ee]xcept|[Ss]kip)\s*:)?(?:([a-zA-Z ]+):)?(.*)$");

            foreach (string raw in lines)
            {
                string line = raw.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                Matcher matcher;
                string prefix, patternOrName;
                int i = line.IndexOf(':');
                int j = line.IndexOf('@');
                bool isComplex = i > 0 && (i < j || j < 0);

                if (isComplex)
                {
                    Match m = syntax.Match(line);
                    GroupCollection groups;

                    if (!m.Success || (groups = m.Groups).Count != 4)
                    {
                        Msg(line, "syntax error");
                        continue;
                    }

                    string s = groups[1].Value;
                    matcher = string.IsNullOrEmpty(s) || s.ToUpperInvariant() == "SKIP" ? skip : except;

                    s = groups[2].Value;
                    prefix = string.IsNullOrEmpty(s) ? string.Empty : s.Replace(" ", string.Empty).ToUpperInvariant();

                    s = groups[3].Value;
                    patternOrName = string.IsNullOrEmpty(s) ? string.Empty : s.TrimStart(null);
                }
                else
                {
                    matcher = skip;
                    prefix = string.Empty;
                    patternOrName = line;
                }

                ByPatterns[] array;
                int index;
                string pattern;

                if (prefix == string.Empty)
                {
                    array = null;
                    index = 0;
                }
                else if (servicePrefixes.TryGetValue(prefix, out index))
                    array = matcher.services;
                else if (subServicePrefixes.TryGetValue(prefix, out index))
                    array = matcher.subServices;
                else
                {
                    Msg(line, "unknown prefix");
                    continue;
                }

                if (patternOrName.StartsWith("@"))
                    pattern = patternOrName.Substring(1);
                else if (patternOrName.IndexOf('*') >= 0 || patternOrName.IndexOf('?') >= 0)
                {
                    if (patternOrName.IndexOf(':') >= 0)
                    {
                        Msg(line, "syntax error");
                        continue;
                    }

                    pattern = "^" + patternOrName.ToUpperInvariant().Replace('?', '.').Replace("*", ".*") + "$";
                }
                else
                    pattern = null;

                if (pattern != null)
                    if (array == null)
                        matcher.byPatterns.AddPattern(pattern);
                    else
                    {
                        if (array[index] == null)
                            array[index] = new ByPatterns();

                        array[index].AddPattern(pattern);
                    }
                else
                {
                    if (array != null)
                        Msg(line, "service prefix ignored because it is not needed");

                    matcher.byNames.AddName(patternOrName.ToUpperInvariant());
                }
            }

            return new Matcher[] { skip, except };
        }

        static void Msg(string line, string msg) => Util.DebugPrint(line + " : " + msg);

        static void TestRegex()
        {
            //Regex pattern = new Regex(@"^(?:([a-zA-Z]+)\s*:\s*){1,2}(.*)$");
            Regex pattern = new Regex(@"^(?:([Ee]xcept|[Ss]kip)\s*:)?(?:([a-zA-Z ]+)\s*:)?(.*)$");

            string[] ss = {
                @"Oil 3x3 Processing",
                @"IndustrialOil:*Processing*",
                @"Except:H1 2x2 Sweatshop06",
                @"except : IndustrialGeneric:*Sweat:shop*",
                @"Except: Residential Low Eco: *Sweatshop*",
                @"IndustrialOil:@.*Processing.*",
                @"skip: IndustrialOil:",
                @"Industrial  Oil : ???"};

            foreach (string s in ss)
            {
                Console.WriteLine();
                Console.WriteLine(s);
                Match m = pattern.Match(s);

                if (m.Success)
                {
                    GroupCollection groups = m.Groups;

                    for (int i = 1; i < groups.Count; i++)
                        Console.WriteLine(" " + i + ": " + groups[i].Value);
                }
            }
        }

        /*
         * Oil 3x3 Processing
         * *Processing*
         * *1x? Shop*
         * 
         * Industrial:*Processing*
         * IndustrialOil:*Processing*
         * 
         * IndustrialGeneric:*
         * Except:H1 2x2 Sweatshop06
         * 
         * IndustrialGeneric:*
         * Except:IndustrialGeneric:*Sweatshop*
         * 
         * @.*Processing.*
         * IndustrialOil:@.*Processing.*
         */
    }
}
