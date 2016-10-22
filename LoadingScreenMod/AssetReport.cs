using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoadingScreenMod
{
    internal sealed class AssetReport
    {
        internal static AssetReport instance;
        List<string> failed = new List<string>();
        Dictionary<string, List<string>> duplicate = new Dictionary<string, List<string>>();
        List<string> notFound = new List<string>();
        Dictionary<string, HashSet<string>> notFoundIndirect = new Dictionary<string, HashSet<string>>();
        StreamWriter w;
        const string steamid = @"<a target=""_blank"" href=""https://steamcommunity.com/sharedfiles/filedetails/?id=";

        internal AssetReport()
        {
            instance = this;
        }

        internal void Dispose()
        {
            failed.Clear(); duplicate.Clear(); notFoundIndirect.Clear();
            instance = null; failed = null; duplicate = null; notFoundIndirect = null;
        }

        internal void Failed(string name) => failed.Add(name);

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

        internal void Save()
        {
            try
            {
                w = new StreamWriter(Util.GetFileName(AssetLoader.AssetName(LevelLoader.instance.cityName) + "-AssetsReport", "htm"));
                w.WriteLine(@"<!DOCTYPE html><html><head><meta charset=""UTF-8""><title>Assets Report</title><style>");
                w.WriteLine(@"* {font-family: sans-serif;}");
                w.WriteLine(@".my {display: -webkit-flex; display: flex;}");
                w.WriteLine(@".my div {min-width: 30%; margin: 4px 4px 4px 20px;}");
                w.WriteLine(@"h1 {margin-top: 40px; border-bottom: 2px solid black;}");
                w.WriteLine(@"</style></head><body>");

                H1(AssetLoader.AssetName(LevelLoader.instance.cityName));
                int seconds = Profiling.Millis / 1000 + 4;
                string loadingTime = string.Concat((seconds / 60).ToString(), ":", (seconds % 60).ToString("00"));
                Italics(string.Concat("Loading time " + loadingTime, "."));

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
                    Save(new List<string>(UsedAssets.instance.Buildings), "Buildings and parks");
                    Save(new List<string>(UsedAssets.instance.Props), "Props");
                    Save(new List<string>(UsedAssets.instance.Trees), "Trees");
                    Save(new List<string>(UsedAssets.instance.Vehicles), "Vehicles");
                    Save(new List<string>(UsedAssets.instance.IndirectProps), "Props in buildings and parks");
                    Save(new List<string>(UsedAssets.instance.IndirectTrees), "Trees in buildings and parks");

                    H1("The following loaded assets are currently unnecessary (not used in this city)");
                    Italics("There are three reasons why an asset may appear in this section: (a) The asset is enabled in Content Manager (b) The asset is a prop or tree in an enabled building or park (c) The asset is included in an enabled district style.");
                    Save(AssetLoader.instance.Buildings.Where(s => !AssetLoader.instance.IsIntersection(s) && !UsedAssets.instance.GotBuilding(s)).ToList(), "Buildings and parks");
                    Save(AssetLoader.instance.Props.Where(s => !UsedAssets.instance.GotProp(s) && !UsedAssets.instance.GotIndirectProp(s)).ToList(), "Props");
                    Save(AssetLoader.instance.Trees.Where(s => !UsedAssets.instance.GotTree(s) && !UsedAssets.instance.GotIndirectTree(s)).ToList(), "Trees");
                    Save(AssetLoader.instance.Vehicles.Where(s => !UsedAssets.instance.GotVehicle(s)).ToList(), "Vehicles");
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

        void Save(List<string> lines, string heading = "", string emptyMsg = "")
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

                foreach (var s in lines)
                    Para(Ref(s));
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
                    s = string.Concat(s, " ", path);

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
                Save(notFound);
            }

            if (notFoundIndirect.Count > 0)
            {
                if (notFound.Count > 0)
                    w.WriteLine("<br>");

                Italics("Note: the following missing assets are used in buildings and parks. These cases should <b>not</b> break savegames.");
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
                        string s = string.Concat(refkey, "</div><div>Required in:");
                        ulong id;
                        bool fromWorkshop = false;

                        foreach (string fullName in set)
                        {
                            s = string.Concat(s, " ", Ref(fullName));
                            fromWorkshop = fromWorkshop || AssetLoader.IsWorkshopPackage(fullName, out id);
                        }

                        if (fromWorkshop && !AssetLoader.IsWorkshopPackage(key, out id))
                        {
                            if (AssetLoader.IsPrivatePackage(key))
                                s = string.Concat(s, " <b>Workshop asset requires private content, seems like asset bug?</b>");
                            else if (key.EndsWith("_Data"))
                                s = string.Concat(s, " <b>Probably a Workshop prop or tree but no link is available</b>");
                            else
                                s = string.Concat(s, " <b>Workshop asset requires DLC or Deluxe content?</b>");
                        }

                        Para(s);
                    }
                }
            }
        }

        void Para(string line) => w.WriteLine(string.Concat("<div class=\"my\"><div>", line, "</div></div>"));
        void Italics(string line) => Para("<i>" + line + "</i>");
        void H1(string line) => w.WriteLine(string.Concat("<h1>", line, "</h1>"));
        void H2(string line) => w.WriteLine(string.Concat("<h2>", line, "</h2>"));

        string Ref(string fullName)
        {
            ulong id;

            if (AssetLoader.IsWorkshopPackage(fullName, out id))
                return string.Concat(steamid, id.ToString(), "\">", fullName, "</a>");
            else
                return fullName;
        }
    }
}
