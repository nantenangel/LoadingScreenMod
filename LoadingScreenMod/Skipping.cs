using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LoadingScreenModTest
{
    interface IMatcher
    {
        bool Matches(string name);
    }

    sealed class ByNames : IMatcher
    {
        readonly HashSet<string> names = new HashSet<string>();

        public ByNames() { }
        public bool Matches(string name) => names.Contains(name);
        public void AddName(string name) => names.Add(name);
    }

    sealed class ByPatterns : IMatcher
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

        internal bool Matches(string name)
        {
            return false;
        }

        internal static void Load(string filePath)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                Dictionary<string, int> servicePrefixes = Util.GetEnumMap(typeof(ItemClass.Service));
                Dictionary<string, int> subServicePrefixes = Util.GetEnumMap(typeof(ItemClass.SubService));
                Matcher skip = new Matcher(servicePrefixes.Count, subServicePrefixes.Count);
                Matcher except = new Matcher(servicePrefixes.Count, subServicePrefixes.Count);
                Regex syntax = new Regex(@"^(?:([Ee]xcept|[Ss]kip)\s*:)?(?:([a-zA-Z ]+):)?(.*)$");

                foreach (string raw in lines)
                {
                    string line = raw.Trim();

                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    bool isSkip;
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
                        isSkip = string.IsNullOrEmpty(s) || s.ToUpperInvariant() == "SKIP";

                        s = groups[2].Value;
                        prefix = string.IsNullOrEmpty(s) ? string.Empty : s.Replace(" ", string.Empty).ToUpperInvariant();

                        s = groups[3].Value;
                        patternOrName = string.IsNullOrEmpty(s) ? string.Empty : s.TrimStart(null);
                    }
                    else
                    {
                        isSkip = true;
                        prefix = string.Empty;
                        patternOrName = line;
                    }

                    Matcher matcher = isSkip ? skip : except;
                    ByPatterns patterns;

                    if (prefix == string.Empty)
                        patterns = matcher.byPatterns;
                    else if (servicePrefixes.TryGetValue(prefix, out int index))
                        patterns = matcher.services[index];
                    else if (subServicePrefixes.TryGetValue(prefix, out index))
                        patterns = matcher.subServices[index];
                    else
                    {
                        Msg(line, "unknown prefix");
                        continue;
                    }

                    // AsPatternOrName(patternOrName, out bool isPattern, out bool isError)
                }
            }
            catch (Exception e)
            {
                Util.DebugPrint("Matcher.Load");
                UnityEngine.Debug.LogException(e);
            }
        }

        static string AsPattern(string s)
        {
            if (s.StartsWith("@"))
                return s.Substring(1);
            else if (s.IndexOf('*') >= 0 || s.IndexOf('?') >= 0)
                return "^" + s.Replace('?', '.').Replace("*", ".*") + "$";
            else
                return string.Empty;
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
