using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.Packaging;

namespace LoadingScreenMod
{
    internal sealed class AssetReport
    {
        internal static AssetReport instance;
        List<string> failed = new List<string>();
        Dictionary<string, List<string>> duplicate = new Dictionary<string, List<string>>();
        List<string> notFound = new List<string>();
        Dictionary<string, HashSet<Package.Asset>> notFoundIndirect = new Dictionary<string, HashSet<Package.Asset>>();
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
            {
                list = new List<string>(1);
                list.Add(path);
                duplicate[name] = list;
            }
        }

        internal void NotFound(string name) => notFound.Add(name);

        internal void NotFound(string name, Package.Asset referencedBy)
        {
            HashSet<Package.Asset> set;

            if (notFoundIndirect.TryGetValue(name, out set) && set != null)
                set.Add(referencedBy);
            else
            {
                set = new HashSet<Package.Asset>();
                set.Add(referencedBy);
                notFoundIndirect[name] = set;
            }
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
                w.WriteLine(@"</style></head><body>");

                H1(AssetLoader.AssetName(LevelLoader.instance.cityName));
                Para("<i>To stop saving these files, disable the option \"Save assets report\" in Loading Screen Mod.</i>");
                Para("<i>You can safely delete this file. No-one reads it except you.</i>");

                Save(failed, "Assets that failed to load", "<i>No failed assets.</i>");
                SaveDuplicates("Duplicate assets");
                SaveNotFound("Assets that were not found");

                if (Settings.settings.loadUsed)
                {
                    H1("The following custom assets were used in this city when it was saved");
                    Save(new List<string>(UsedAssets.instance.Buildings), "Buildings");
                    Save(new List<string>(UsedAssets.instance.Props), "Props");
                    Save(new List<string>(UsedAssets.instance.Trees), "Trees");
                    Save(new List<string>(UsedAssets.instance.Vehicles), "Vehicles");
                }
                else
                {
                    Para("<i>Enable the option \"Load used assets\" to track missing assets.</i>");
                    H1("Used assets");
                    Para("<i>To also list the custom assets used in this city, enable the option \"Load used assets\" in Loading Screen Mod.</i>");
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
                    Para(emptyMsg);
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
                Para("<i>No duplicates were found (in Cities Skylines, each 'PackageName.AssetName' must be unique).</i>");
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

        void SaveNotFound(string heading)
        {
            H2(heading);

            if (notFound.Count == 0 && notFoundIndirect.Count == 0)
            {
                Para("<i>No missing assets.</i>");
                return;
            }

            if (notFound.Count > 0)
            {
                Para("<i>Note: the following assets are used in your city but could not be found. You should get the assets if possible. These cases can break savegames.</i>");
                Save(notFound);
            }

            if (notFoundIndirect.Count > 0)
            {
                if (notFound.Count > 0)
                    w.WriteLine("<br>");

                Para("<i>Note: the following missing assets are used in buildings and parks. These cases should <b>not</b> break savegames.</i>");
                List<string> keys = new List<string>(notFoundIndirect.Keys);
                keys.Sort();

                foreach (var key in keys)
                {
                    HashSet<Package.Asset> set = notFoundIndirect[key];
                    string refkey = Ref(key);

                    if (set == null)
                        Para(refkey);
                    else
                    {
                        string s = string.Concat(refkey, "</div><div>Required in:");
                        ulong id;
                        bool fromWorkshop = false;

                        foreach (Package.Asset asset in set)
                        {
                            s = string.Concat(s, " ", Ref(asset));
                            fromWorkshop = fromWorkshop || AssetLoader.IsWorkshopPackage(asset.package, out id);
                        }

                        if (fromWorkshop && !AssetLoader.IsWorkshopPackage(key, out id))
                        {
                            if (AssetLoader.IsPrivatePackage(key))
                                s = string.Concat(s, " <b>Workshop asset requires private content, seems like asset bug?</b>");
                            else if (key.EndsWith("_Data"))
                                s = string.Concat(s, " <b>Probably a Workshop prop or tree but no link is available</b>");
                            else
                                s = string.Concat(s, " <b>Workshop asset requires DLC/Deluxe/Pre-order content?</b>");
                        }

                        Para(s);
                    }
                }
            }
        }

        void Para(string line) => w.WriteLine(string.Concat("<div class=\"my\"><div>", line, "</div></div>"));
        void H1(string line) => w.WriteLine(string.Concat("<h1>", line, "</h1>"));
        void H2(string line) => w.WriteLine(string.Concat("<h2>", line, "</h2>"));

        string Ref(Package.Asset asset)
        {
            ulong id;

            if (AssetLoader.IsWorkshopPackage(asset.package, out id))
                return string.Concat(steamid, id.ToString(), "\">", asset.fullName, "</a>");
            else
                return asset.fullName;
        }

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
