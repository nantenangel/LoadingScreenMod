using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ColossalFramework.Steamworks;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace LoadingScreenMod
{
    public class Settings
    {
        const string FILENAME = "LoadingScreenMod.xml";

        public int version = 3;
        public bool loadEnabled = true;
        public bool loadUsed = true;
        public bool shareTextures = true;
        public bool shareMaterials = true;
        public bool shareMeshes = true;
        public bool reportAssets = false;

        public bool skipResLo = false;
        public bool skipResHi = false;
        public bool skipComLo = false;
        public bool skipComHi = false;
        public bool skipIndGen = false;
        public bool skipIndSpe = false;
        public bool skipComSpe = false;
        public bool skipOffice = false;
        public bool skipThese = false;
        public string skippedNames = "";
        public bool applyToEuropean = false;

        [XmlIgnoreAttribute]
        internal readonly bool[] skip = new bool[10];

        [XmlIgnoreAttribute]
        readonly char[] delimiters = {',', ';', ':'};

        [XmlIgnoreAttribute]
        readonly HashSet<string> skippedSet = new HashSet<string>();

        public bool SkipAny => Array.Exists(skip, b => b);
        public bool SkipThis(string name) => skipThese && skippedSet.Contains(name);

        static Settings singleton;

        public static Settings settings
        {
            get
            {
                if (singleton == null)
                    singleton = Load();

                return singleton;
            }
        }

        Settings() { }

        static Settings Load()
        {
            Settings s;

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Settings));

                using (StreamReader reader = new StreamReader(FILENAME))
                    s = (Settings) serializer.Deserialize(reader);
            }
            catch (Exception) { s = new Settings(); }

            s.version = 3;
            s.Setup();
            return s;
        }

        public void Save()
        {
            try
            {
                Setup();
                //XmlSerializer serializer = new XmlSerializer(typeof(Settings));

                //using (StreamWriter writer = new StreamWriter(FILENAME))
                //    serializer.Serialize(writer, this);
            }
            catch (Exception e)
            {
                Util.DebugPrint("Settings.Save");
                UnityEngine.Debug.LogException(e);
            }
        }

        void Setup()
        {
            skippedSet.Clear();

            if (skipThese && !string.IsNullOrEmpty(skippedNames))
                foreach (string s in skippedNames.Split(delimiters, StringSplitOptions.RemoveEmptyEntries))
                    skippedSet.Add(s.Trim());

            skip[0] = false; skip[1] = skipResLo; skip[2] = skipResHi; skip[3] = skipComLo; skip[4] = skipComHi;
            skip[5] = skipIndGen; skip[6] = skipIndSpe; skip[7] = skipComSpe; skip[8] = skipOffice; skip[9] = skippedSet.Count > 0;
        }

        internal void OnSettingsUI(UIHelperBase helper)
        {
            UIHelper group = CreateGroup(helper, "Loading options for custom assets", "Custom means workshop assets and assets created by yourself");
            Check(group, "Load enabled assets", "Load the assets enabled in Content Manager", loadEnabled, b => { loadEnabled = b; Save(); });
            Check(group, "Load used assets", "Load the assets you have placed in your city", loadUsed, b => { loadUsed = b; Save(); });
            Check(group, "Share textures", "Replace exact duplicates by references", shareTextures, b => { shareTextures = b; Save(); });
            Check(group, "Share materials", "Replace exact duplicates by references", shareMaterials, b => { shareMaterials = b; Save(); });
            Check(group, "Share meshes", "Replace exact duplicates by references", shareMeshes, b => { shareMeshes = b; Save(); });

            group = CreateGroup(helper, "Skip unused standard buildings  [Experimental]", "This means the buildings included in the base game and DLCs");
            Check(group, "Residential Low", null, skipResLo, b => { skipResLo = b; Save(); });
            Check(group, "Residential High", null, skipResHi, b => { skipResHi = b; Save(); });
            Check(group, "Commercial Low", null, skipComLo, b => { skipComLo = b; Save(); });
            Check(group, "Commercial High", null, skipComHi, b => { skipComHi = b; Save(); });
            Check(group, "Commercial Tourism&Leisure", null, skipComSpe, b => { skipComSpe = b; Save(); });
            Check(group, "Offices", null, skipOffice, b => { skipOffice = b; Save(); });
            Check(group, "Industry Generic", null, skipIndGen, b => { skipIndGen = b; Save(); });
            Check(group, "Industry Specialized", null, skipIndSpe, b => { skipIndSpe = b; Save(); });
            Check(group, "Also skip these named buildings:", "A comma-separated list of building names", skipThese, b => { skipThese = b; Save(); });
            TextField(group, skippedNames, OnSubmitted);

            if (Steam.IsOverlayEnabled())
                Button(group, "Show building names and images using browser", "Opens the Steam web browser", OnWebButton);

            Check(group, "Apply my selections to the European/Vanilla style", "Skip buildings in the built-in district style, too?", applyToEuropean, b => { applyToEuropean = b; Save(); });

            group = CreateGroup(helper, "Reports");
            Check(group, "Save assets report", "Save a report of missing, failed and used assets in " + Util.GetSavePath(), reportAssets, b => { reportAssets = b; Save(); });
        }

        UIHelper CreateGroup(UIHelperBase parent, string name, string tooltip = null)
        {
            UIHelper group = parent.AddGroup(name) as UIHelper;
            UIPanel content = group.self as UIPanel;
            UIPanel container = content?.parent as UIPanel;
            RectOffset rect = content?.autoLayoutPadding;

            if (rect != null)
                rect.bottom /= 2;

            rect = container?.autoLayoutPadding;

            if (rect != null)
                rect.bottom /= 4;

            if (!string.IsNullOrEmpty(tooltip))
            {
                UILabel label = container?.Find<UILabel>("Label");

                if (label != null)
                    label.tooltip = tooltip;
            }

            return group;
        }

        void Check(UIHelper group, string text, string tooltip, bool enabled, OnCheckChanged action)
        {
            try
            {
                UIComponent check = group.AddCheckbox(text, enabled, action) as UIComponent;

                if (tooltip != null)
                    check.tooltip = tooltip;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        void TextField(UIHelper group, string text, OnTextSubmitted action)
        {
            try
            {
                UITextField field = group.AddTextfield(" ", text, t => Util.DebugPrint("Changed to:", t), action) as UITextField;
                field.width *= 2.8f;
                UIComponent parent = field.parent;
                UILabel label = parent?.Find<UILabel>("Label");

                if (label != null)
                {
                    float h = label.height;
                    label.height = 0; label.Hide();
                    parent.height -= h;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        void OnSubmitted(string text)
        {
            if (skippedNames != text)
            {
                Util.DebugPrint("Submitted: ", text);
                skippedNames = text;
                Save();
            }
        }

        void Button(UIHelper group, string text, string tooltip, OnButtonClicked action)
        {
            try
            {
                UIButton button = group.AddButton(text, action) as UIButton;
                button.textScale = 0.875f;

                if (tooltip != null)
                    button.tooltip = tooltip;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        void OnWebButton()
        {
            Steam.ActivateGameOverlayToWebPage("https://cdn.rawgit.com/thale5/Buildings/t1/index.htm");
        }
    }
}
