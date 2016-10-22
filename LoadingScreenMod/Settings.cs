using System;
using System.IO;
using System.Xml.Serialization;
using ColossalFramework.IO;
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
        public string reportDir = "";

        [XmlIgnoreAttribute]
        bool dirty = false;

        static Settings singleton;
        internal static UIHelperBase helper;
        static bool Dirty => singleton != null && singleton.dirty;
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

            s.version = 3;
            return s;
        }

        void Save()
        {
            try
            {
                dirty = false;
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

        static internal void OnSettingsUI(UIHelperBase newHelper)
        {
            UIComponent comp = Self(helper);

            if (comp != null)
                comp.eventVisibilityChanged -= OnVisibilityChanged;

            helper = newHelper;
            comp = Self(newHelper);
            comp.eventVisibilityChanged -= OnVisibilityChanged;
            comp.eventVisibilityChanged += OnVisibilityChanged;
        }

        static UIComponent Self(UIHelperBase h) => ((UIHelper) h)?.self as UIComponent;

        static void OnVisibilityChanged(UIComponent comp, bool visible)
        {
            if (visible && comp == Self(helper) && comp.childCount == 0)
                settings.LateSettingsUI(helper);
            else if (!visible && Dirty)
                settings.Save();
        }

        void LateSettingsUI(UIHelperBase helper)
        {
            UIHelper group = CreateGroup(helper, "Loading options for custom assets", "Custom means workshop assets and assets created by yourself");
            Check(group, "Load enabled assets", "Load the assets enabled in Content Manager", loadEnabled, b => { loadEnabled = b; dirty = true; });
            Check(group, "Load used assets", "Load the assets you have placed in your city", loadUsed, b => { loadUsed = b; dirty = true; });
            Check(group, "Share textures", "Replace exact duplicates by references", shareTextures, b => { shareTextures = b; dirty = true; });
            Check(group, "Share materials", "Replace exact duplicates by references", shareMaterials, b => { shareMaterials = b; dirty = true; });
            Check(group, "Share meshes", "Replace exact duplicates by references", shareMeshes, b => { shareMeshes = b; dirty = true; });

            group = CreateGroup(helper, "Reports");
            Check(group, "Save assets report in this directory:", "Save a report of missing, failed and used assets", reportAssets, b => { reportAssets = b; dirty = true; });
            TextField(group, reportDir, OnReportDirChanged);
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

        void TextField(UIHelper group, string text, OnTextChanged action)
        {
            try
            {
                UITextField field = group.AddTextfield(" ", text, action, null) as UITextField;
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
            reportDir = text;
            dirty = true;
        }
    }
}
