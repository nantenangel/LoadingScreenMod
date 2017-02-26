using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ColossalFramework.Packaging;

namespace LoadingScreenMod
{
    internal sealed class AssetReport : Instance<AssetReport>
    {
        List<string> failed = new List<string>();
        Dictionary<string, List<string>> duplicate = new Dictionary<string, List<string>>();
        List<string> notFound = new List<string>();
        Dictionary<string, HashSet<string>> notFoundIndirect = new Dictionary<string, HashSet<string>>();
        HashSet<string> packageNames = new HashSet<string>();
        StreamWriter w;
        const string steamid = @"<a target=""_blank"" href=""https://steamcommunity.com/sharedfiles/filedetails/?id=";

        private AssetReport() { }

        internal void Dispose()
        {
            failed.Clear(); duplicate.Clear(); notFoundIndirect.Clear(); packageNames.Clear();
            failed = null; duplicate = null; notFoundIndirect = null; packageNames = null; instance = null;
        }

        internal void AssetFailed(string name) => failed.Add(name);

        internal void Duplicate(string name, string path)
        {
            List<string> list;

            if (duplicate.TryGetValue(name, out list) && list != null)
                list.Add(path);
            else
                duplicate[name] = new List<string> { path };
        }

        internal void NotFound(string name) => notFound.Add(name);

        internal void NotFound(string name, string referencedBy)
        {
            HashSet<string> set;

            if (notFoundIndirect.TryGetValue(name, out set) && set != null)
                set.Add(referencedBy);
            else
                notFoundIndirect[name] = new HashSet<string> { referencedBy };
        }

        internal void AddPackage(Package p) => packageNames.Add(p.packageName);

        internal void Save()
        {
            try
            {
                w = new StreamWriter(Util.GetFileName(AssetLoader.AssetName(LevelLoader.instance.cityName) + "-AssetsReport", "htm"));
                w.WriteLine(@"<!DOCTYPE html><html lang=""en""><head><meta charset=""UTF-8""><title>Assets Report</title><style>");
                w.WriteLine(@"* {font-family: sans-serif;}");
                w.WriteLine(@".my {display: -webkit-flex; display: flex;}");
                w.WriteLine(@".my div {min-width: 32%; margin: 5px 5px 5px 20px;}");
                w.WriteLine(@"h1 {margin-top: 40px; border-bottom: 2px solid black;}");
                w.WriteLine(@"</style></head><body>");

                H1(AssetLoader.AssetName(LevelLoader.instance.cityName));
                int seconds = Profiling.Millis / 1000 + 4;
                string loadingTime = string.Concat((seconds / 60).ToString(), ":", (seconds % 60).ToString("00"));
                Italics(string.Concat("Report created at time ", loadingTime, "."));

                Italics("To stop saving these files, disable the option \"Save assets report\" in Loading Screen Mod.");
                Italics("You can safely delete this file. No-one reads it except you.");

                Save(failed, "Assets that failed to load", "No failed assets.");
                SaveDuplicates("Duplicate assets");
                H2("Assets that were not found");

                if (Settings.settings.loadUsed)
                    SaveNotFound();
                else
                    Italics("Enable the option \"Load used assets\" to track missing assets.");

                if (Settings.settings.loadUsed)
                {
                    H1("The following custom assets are used in this city");
                    List<string> buildings = new List<string>(UsedAssets.instance.Buildings), props = new List<string>(UsedAssets.instance.Props),
                        trees = new List<string>(UsedAssets.instance.Trees), vehicles = new List<string>(UsedAssets.instance.Vehicles),
                        indirectProps = new List<string>(UsedAssets.instance.IndirectProps), indirectTrees = new List<string>(UsedAssets.instance.IndirectTrees);
                    Save(buildings, "Buildings and parks"); Save(props, "Props"); Save(trees, "Trees"); Save(vehicles, "Vehicles");
                    Save(indirectProps, "Props in buildings and parks"); Save(indirectTrees, "Trees in buildings and parks");
                    HashSet<string> paths = GetPackagePaths(buildings, props, trees, vehicles, indirectProps, indirectTrees);

                    H1("The following loaded assets are currently unnecessary (not used in this city)");
                    Italics("There are three reasons why an asset may appear in this section: (a) The asset is enabled but unused (b) The asset is a prop or tree in an enabled but unused building or park (c) The asset is included in an enabled district style and unused.");
                    Save(AssetLoader.instance.Buildings.Where(s => !AssetLoader.instance.IsIntersection(s) && !Used(s, paths)).ToList(), "Buildings and parks");
                    Save(AssetLoader.instance.Props.Where(s => !Used(s, paths)).ToList(), "Props");
                    Save(AssetLoader.instance.Trees.Where(s => !Used(s, paths)).ToList(), "Trees");
                    Save(AssetLoader.instance.Vehicles.Where(s => !Used(s, paths)).ToList(), "Vehicles");
                }
                else
                {
                    H1("Used assets");
                    Italics("To list the custom assets used in this city, enable the option \"Load used assets\" in Loading Screen Mod.");
                }

                w.WriteLine(@"</body></html>");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                w?.Dispose();
                w = null;
            }
        }

        void Save(List<string> lines, string heading = "", string emptyMsg = "", bool missedReferences = false)
        {
            if (!string.IsNullOrEmpty(heading))
                H2(heading);

            if (lines.Count == 0)
            {
                if (!string.IsNullOrEmpty(emptyMsg))
                    Italics(emptyMsg);
            }
            else
            {
                lines.Sort();

                if (!missedReferences)
                    foreach (var n in lines)
                        Para(Ref(n));
                else
                {
                    foreach (var n in lines)
                    {
                        string s = Ref(n);
                        MissedReference(ref s, n, true);
                        Para(s);
                    }
                }
            }
        }

        void SaveDuplicates(string heading)
        {
            H2(heading);

            if (duplicate.Count == 0)
            {
                Italics("No duplicates were found (in Cities Skylines, each 'PackageName.AssetName' must be unique).");
                return;
            }

            List<string> keys = new List<string>(duplicate.Keys);
            keys.Sort();

            foreach (var key in keys)
            {
                string s = string.Concat(Ref(key), "</div><div>Duplicate found:");

                foreach (string path in duplicate[key])
                    s = string.Concat(s, " ", Enc(path));

                Para(s);
            }
        }

        void SaveNotFound()
        {
            if (notFound.Count == 0 && notFoundIndirect.Count == 0)
            {
                Italics("No missing assets.");
                return;
            }

            if (notFound.Count > 0)
            {
                Italics("Note: the following assets are used in your city but could not be found. You should get the assets if possible. These cases can break savegames.");
                Save(notFound, string.Empty, string.Empty, true);
            }

            if (notFoundIndirect.Count > 0)
            {
                if (notFound.Count > 0)
                    w.WriteLine("<br>");

                Italics("Note: the following missing assets are used in buildings and parks. These cases do <b>not</b> break savegames.");
                List<string> keys = new List<string>(notFoundIndirect.Keys);
                keys.Sort();

                foreach (var key in keys)
                {
                    HashSet<string> set = notFoundIndirect[key];
                    string refkey = Ref(key);

                    if (set == null)
                        Para(refkey);
                    else
                    {
                        string s = string.Concat(refkey, "</div><div>Required in: ");
                        string pn;
                        int i = 0;
                        bool fromWorkshop = false;

                        foreach (string fullName in set)
                        {
                            s = string.Concat(s, i++ > 0 ? "<br>" : string.Empty, Ref(fullName));
                            fromWorkshop = fromWorkshop || IsWorkshopPackage(fullName, out pn);
                        }

                        if (!MissedReference(ref s, key, false) && fromWorkshop && !IsWorkshopPackage(key, out pn))
                        {
                            if (IsPrivatePackage(key))
                                s = string.Concat(s, @"<br><b>Workshop asset requires private content, seems like asset <a target=""_blank"" href=""http://steamcommunity.com/workshop/filedetails/discussion/667342976/357284767251931800/"">bug</a></b>");
                            else if (key.EndsWith("_Data"))
                                s = string.Concat(s, "<br><b>Probably a Workshop prop or tree but no link is available</b>");
                            else
                                s = string.Concat(s, "<br><b>Workshop asset requires DLC or Deluxe content?</b>");
                        }

                        Para(s);
                    }
                }
            }
        }

        HashSet<string> GetPackagePaths(params List<string>[] names)
        {
            HashSet<string> paths = new HashSet<string>();

            for (int i = 0; i < names.Length; i++)
                for (int j = 0; j < names[i].Count; j++)
                {
                    Package.Asset asset = CustomDeserializer.FindAsset(names[i][j]);
                    string path = asset?.package.packagePath;

                    if (path != null)
                        paths.Add(path);
                }

            return paths;
        }

        bool Used(string fullName, HashSet<string> packagePaths)
        {
            Package.Asset asset = CustomDeserializer.FindAsset(fullName);
            string path = asset?.package.packagePath;
            return path != null && packagePaths.Contains(path);
        }

        bool MissedReference(ref string s, string key, bool div)
        {
            string packageName, assetName;

            if (GetNames(key, out packageName, out assetName) && packageNames.Contains(packageName))
            {
                s = string.Concat(s, div ? "</div><div>" : "<br>", "<b>You have ", Ref(packageName), " but it does not contain ", assetName,
                    @". Name probably <a target=""_blank"" href=""http://steamcommunity.com/workshop/filedetails/discussion/667342976/141136086940263481/"">changed</a> by the author</b>");
                return true;
            }

            return false;
        }

        void Para(string line) => w.WriteLine(string.Concat("<div class=\"my\"><div>", line, "</div></div>"));
        void Italics(string line) => Para("<i>" + line + "</i>");
        void H1(string line) => w.WriteLine(string.Concat("<h1>", line, "</h1>"));
        void H2(string line) => w.WriteLine(string.Concat("<h2>", line, "</h2>"));

        static string Ref(string name)
        {
            string pn;

            if (IsWorkshopPackage(name, out pn))
                return string.Concat(steamid, Enc(pn), "\">", Enc(name), "</a>");
            else
                return Enc(name);
        }

        static bool GetNames(string fullName, out string packageName, out string assetName)
        {
            int j = fullName.IndexOf('.');

            if (j > 0 && j < fullName.Length - 1)
            {
                packageName = fullName.Substring(0, j);
                assetName = fullName.Substring(j + 1);
                return true;
            }
            else
            {
                packageName = assetName = string.Empty;
                return false;
            }
        }

        static bool IsWorkshopPackage(string name, out string packageName)
        {
            int j = name.IndexOf('.');
            packageName = j > 0 && j < name.Length - 1 ? name.Substring(0, j) : name;
            ulong id;
            return ulong.TryParse(packageName, out id) && id > 9999999;
        }

        static bool IsPrivatePackage(string fullName)
        {
            string pn;

            // Private: a local asset created by the player (not from the workshop).
            // My rationale is the following:
            // 43453453.Name -> Workshop
            // Name.Name     -> Private
            // Name          -> Either an old-format (early 2015) reference, or something from DLC/Deluxe packs.
            //                  If loading is not successful then cannot tell for sure, assumed DLC/Deluxe when reported as not found.

            if (IsWorkshopPackage(fullName, out pn))
                return false;
            else
                return fullName.IndexOf('.') >= 0;
        }

        // From a more recent mono version.
        // See license https://github.com/mono/mono/blob/master/mcs/class/System.Web/System.Web.Util/HttpEncoder.cs
        static string Enc(string s)
        {
            bool needEncode = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '&' || c == '"' || c == '<' || c == '>' || c > 159 || c == '\'')
                {
                    needEncode = true;
                    break;
                }
            }

            if (!needEncode)
                return s;

            StringBuilder output = new StringBuilder();
            int len = s.Length;

            for (int i = 0; i < len; i++)
            {
                char ch = s[i];

                switch (ch)
                {
                    case '&':
                        output.Append("&amp;");
                        break;
                    case '>':
                        output.Append("&gt;");
                        break;
                    case '<':
                        output.Append("&lt;");
                        break;
                    case '"':
                        output.Append("&quot;");
                        break;
                    case '\'':
                        output.Append("&#39;");
                        break;
                    case '\uff1c':
                        output.Append("&#65308;");
                        break;
                    case '\uff1e':
                        output.Append("&#65310;");
                        break;
                    default:
                        output.Append(ch);
                        break;
                }
            }

            return output.ToString();
        }
    }
}
