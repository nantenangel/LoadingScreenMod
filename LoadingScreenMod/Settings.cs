using System;
using System.IO;
using System.Xml.Serialization;
using ColossalFramework.IO;
using ColossalFramework.UI;
using ICities;

namespace LoadingScreenMod
{
    public class Settings
    {
        const string FILENAME = "LoadingScreenMod.xml";

        public int version = 5;
        public bool loadEnabled = true;
        public bool loadUsed = true;
        public bool shareTextures = true;
        public bool shareMaterials = true;
        public bool shareMeshes = true;
        public bool reportAssets = false;
        public string reportDir = "";

        static Settings singleton;
        internal static string DefaultSavePath => Path.Combine(Path.Combine(DataLocation.localApplicationData, "Report"), "LoadingScreenMod");

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

            if (string.IsNullOrEmpty(s.reportDir = s.reportDir?.Trim()))
                s.reportDir = DefaultSavePath;

            s.version = 5;
            return s;
        }

        void Save()
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Settings));

                using (StreamWriter writer = new StreamWriter(FILENAME))
                    serializer.Serialize(writer, this);
            }
            catch (Exception e)
            {
                Util.DebugPrint("Settings.Save");
                UnityEngine.Debug.LogException(e);
            }
        }

        internal void OnSettingsUI(UIHelperBase helper)
        {
            UIHelper group = CreateGroup(helper, "Loading options for custom assets", "Custom means workshop assets and assets created by yourself");
            Check(group, "Load enabled assets", "Load the assets enabled in Content Manager", loadEnabled, b => { loadEnabled = b; Save(); });
            Check(group, "Load used assets", "Load the assets you have placed in your city", loadUsed, b => { loadUsed = b; Save(); });
            Check(group, "Share textures", "Replace exact duplicates by references", shareTextures, b => { shareTextures = b; Save(); });
            Check(group, "Share materials", "Replace exact duplicates by references", shareMaterials, b => { shareMaterials = b; Save(); });
            Check(group, "Share meshes", "Replace exact duplicates by references", shareMeshes, b => { shareMeshes = b; Save(); });

            group = CreateGroup(helper, "Reports");
            Check(group, "Save assets report in this directory:", "Save a report of missing, failed and used assets", reportAssets, b => { reportAssets = b; Save(); });
            TextField(group, reportDir, OnReportDirChanged);
        }

        UIHelper CreateGroup(UIHelperBase parent, string name, string tooltip = null)
        {
            UIHelper group = parent.AddGroup(name) as UIHelper;

            if (!string.IsNullOrEmpty(tooltip))
            {
                UIPanel content = group.self as UIPanel;
                UIPanel container = content?.parent as UIPanel;
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

        void TextField(UIHelper group, string text, OnTextChanged action)
        {
            try
            {
                UITextField field = group.AddTextfield(" ", " ", action, null) as UITextField;
                field.maxLength = 1024;
                field.text = text;
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

        void OnReportDirChanged(string text)
        {
            if (text != reportDir)
            {
                reportDir = text;
                Save();
            }
        }
    }
}
