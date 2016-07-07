﻿/*****************************************************************************
 * The MIT License (MIT)
 * 
 * Copyright (c) 2016 MOARdV
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 ****************************************************************************/
using MoonSharp.Interpreter;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AvionicsSystems
{
    /// <summary>
    /// MASLoader loads data at startup.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class MASLoader : MonoBehaviour
    {
        /// <summary>
        /// Version of the DLL.
        /// </summary>
        static public string asVersion;

        static public Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        static private string[] systemFonts;

        /// <summary>
        /// Dictionary of all shaders found in the asset bundle.
        /// </summary>
        static public Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();

        /// <summary>
        /// Text of all of the scripts found in config nodes.
        /// </summary>
        static public List<string> userScripts = new List<string>();

        /// <summary>
        /// Dictionary of all RPM-compatible named colors.
        /// </summary>
        static public Dictionary<string, Color32> namedColors = new Dictionary<string, Color32>();

        /// <summary>
        /// Where is my config file?
        /// </summary>
        static private readonly string configFileName = "GameData/AvionicsSystems/PluginData/AvionicsSystems.cfg";

        /// <summary>
        /// Does the config file say I should use verbose logging?
        /// </summary>
        static public bool VerboseLogging = false;

        MASLoader()
        {
            DontDestroyOnLoad(this);
        }

        /// <summary>
        /// Load a named font - preferably using an AssetBundle font, but also
        /// allowing system fonts.
        /// </summary>
        /// <param name="fontName"></param>
        /// <returns></returns>
        internal static Font LoadFont(string fontName)
        {
            if(fonts.ContainsKey(fontName))
            {
                return fonts[fontName];
            }
            else if(systemFonts == null)
            {
                systemFonts = Font.GetOSInstalledFontNames();
            }

            if (systemFonts.Contains(fontName))
            {
                return Font.CreateDynamicFontFromOSFont(fontName, 32);
            }
            else
            {
                return Font.CreateDynamicFontFromOSFont("Arial", 32);
            }
        }

        /// <summary>
        /// Awake() - Load components used by the mod.
        /// </summary>
        public void Awake()
        {
            asVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            UnityEngine.Debug.Log(String.Format("[ASLoader] Avionics Systems version {0}", asVersion));

            if (KSPAssets.Loaders.AssetLoader.Ready == false)
            {
                //Utility.LogErrorMessage(this, "Unable to load shaders - AssetLoader is not ready.");
                throw new Exception("ASLoader: Unable to load shaders - AssetLoader is not ready.");
            }

            KSPAssets.AssetDefinition[] asShaders = KSPAssets.Loaders.AssetLoader.GetAssetDefinitionsWithType("AvionicsSystems/moardvavionicssystems", typeof(Shader));
            if (asShaders == null || asShaders.Length == 0)
            {
                Utility.LogErrorMessage(this, "Unable to load shaders - No shaders found in AS asset bundle.");
                throw new Exception("ASLoader: No shaders in asset bundle.");
            }

            if (!GameDatabase.Instance.IsReady())
            {
                Utility.LogErrorMessage(this, "GameDatabase.IsReady is false");
                throw new Exception("ASLoader: GameDatabase is not ready.  Unable to continue.");
            }

            ConfigNode config = ConfigNode.Load(KSPUtil.ApplicationRootPath + configFileName);
            if (config != null && config.CountNodes > 0)
            {
                config = config.GetNode("AvionicsSystems");
            }
            else
            {
                config = null;
            }

            if (config != null)
            {
                bool logging = false;
                if (config.TryGetValue("VerboseLogging", ref logging))
                {
                    VerboseLogging = logging;
                    Utility.LogMessage(this, "VerboseLogging = {0}", VerboseLogging);
                }
            }

            // HACK: Pass only one of the asset definitions, since LoadAssets
            // behaves badly if we ask it to load more than one.  If that ever
            // gets fixed, I can clean up AssetsLoaded drastically.
            KSPAssets.Loaders.AssetLoader.LoadAssets(AssetsLoaded, asShaders[0]);

            StartCoroutine("LoadAvionicsSystemAssets");
            RegisterWithModuleManager();
        }

        /// <summary>
        /// Coroutine for adding scripts to the Lua context.  Paced to load one
        /// string per frame.
        /// 
        /// It also looks for existing global RasterPropMonitor COLOR_ definitions.
        /// </summary>
        /// <returns>null when done</returns>
        private IEnumerator LoadAvionicsSystemAssets()
        {
            userScripts.Clear();
            ConfigNode[] userScriptNodes = GameDatabase.Instance.GetConfigNodes("MAS_LUA");
            if (userScriptNodes.Length > 0)
            {
                for (int nodeIdx = 0; nodeIdx < userScriptNodes.Length; ++nodeIdx)
                {
                    if (userScriptNodes[nodeIdx].HasValue("name"))
                    {
                        ConfigNode node = userScriptNodes[nodeIdx];
                        string[] scripts = node.GetValues("script");
                        Utility.LogMessage(this, "Parsing MAS_LUA node \"{0}\" ({1} script references)", node.GetValue("name"), scripts.Length);

                        for (int scriptIdx = 0; scriptIdx < scripts.Length; ++scriptIdx)
                        {
                            userScripts.Add(string.Join(Environment.NewLine, File.ReadAllLines(KSPUtil.ApplicationRootPath + "GameData/" + scripts[scriptIdx], Encoding.UTF8)));
                            yield return new WaitForEndOfFrame();
                        }
                    }
                }
            }

            namedColors.Clear();
            ConfigNode[] rpmColorNodes = GameDatabase.Instance.GetConfigNodes("RPM_GLOBALCOLORSETUP");
            for (int colorNodeIdx = 0; colorNodeIdx < rpmColorNodes.Length; ++colorNodeIdx)
            {
                ConfigNode[] colorDef = rpmColorNodes[colorNodeIdx].GetNodes("COLORDEFINITION");
                for (int defIdx = 0; defIdx < colorDef.Length; ++defIdx)
                {
                    if (colorDef[defIdx].HasValue("name") && colorDef[defIdx].HasValue("color"))
                    {
                        string name = "COLOR_" + (colorDef[defIdx].GetValue("name").Trim());
                        Color32 color = ConfigNode.ParseColor32(colorDef[defIdx].GetValue("color").Trim());
                        if (namedColors.ContainsKey(name))
                        {
                            namedColors[name] = color;
                        }
                        else
                        {
                            namedColors.Add(name, color);
                        }

                        Utility.LogMessage(this, "{0} = {1}", name, color);
                    }
                }
                yield return new WaitForEndOfFrame();
            }

            yield return null;
        }

        /// <summary>
        /// Callback that fires once the requested assets have loaded.
        /// </summary>
        /// <param name="loader">Object containing our loaded assets (see comments in this method)</param>
        private void AssetsLoaded(KSPAssets.Loaders.AssetLoader.Loader loader)
        {
            // This is an unforunate hack.  AssetLoader.LoadAssets barfs if
            // multiple assets are loaded, leaving us with only one valid asset
            // and some nulls afterwards in loader.objects.  We are forced to
            // traverse the LoadedBundles list to find our loaded bundle so we
            // can find the rest of our shaders.
            string aShaderName = string.Empty;
            for (int i = 0; i < loader.objects.Length; ++i)
            {
                UnityEngine.Object o = loader.objects[i];
                if (o != null && o is Shader)
                {
                    // We'll remember the name of whichever shader we were
                    // able to load.
                    aShaderName = o.name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(aShaderName))
            {
                Utility.LogErrorMessage(this, "Unable to find a named shader in loader.objects");
                return;
            }

            var loadedBundles = KSPAssets.Loaders.AssetLoader.LoadedBundles;
            if (loadedBundles == null)
            {
                Utility.LogErrorMessage(this, "Unable to find any loaded bundles in AssetLoader");
                return;
            }

            // Iterate over all loadedBundles.  Experimentally, my bundle was
            // the only one in the array, but I expect that to change as other
            // mods use asset bundles (maybe none of the mods I have load this
            // early).
            for (int i = 0; i < loadedBundles.Count; ++i)
            {
                Shader[] foundShaders = null;
                Font[] foundFonts = null;
                bool theRightBundle = false;

                try
                {
                    // Try to get a list of all the shaders in the bundle.
                    foundShaders = loadedBundles[i].LoadAllAssets<Shader>();
                    if (foundShaders != null)
                    {
                        // Look through all the shaders to see if our named
                        // shader is one of them.  If so, we assume this is
                        // the bundle we want.
                        for (int shaderIdx = 0; shaderIdx < foundShaders.Length; ++shaderIdx)
                        {
                            if (foundShaders[shaderIdx].name == aShaderName)
                            {
                                theRightBundle = true;
                                break;
                            }
                        }
                    }
                    foundFonts = loadedBundles[i].LoadAllAssets<Font>();
                }
                catch { }

                if (theRightBundle)
                {
                    // If we found our bundle, set up our shaders
                    // dictionary and bail - our mission is complete.
                    Utility.LogMessage(this, "Found {0} AS shaders and {1} fonts.", foundShaders.Length, foundFonts.Length);
                    for (int j = 0; j < foundShaders.Length; ++j)
                    {
                        if (!foundShaders[j].isSupported)
                        {
                            Utility.LogErrorMessage(this, "Shader {0} - unsupported in this configuration", foundShaders[j].name);
                        }
                        shaders[foundShaders[j].name] = foundShaders[j];
                    }
                    for (int j = 0; j < foundFonts.Length; ++j)
                    {
                        fonts[foundFonts[j].name] = foundFonts[j];
                    }
                    return;
                }
            }

            Utility.LogErrorMessage(this, "No AvionicsSystems bundled assets were loaded - how did this callback execute?");
        }

        /// <summary>
        /// Trigger a coroutine that will reload values that MM may have changed.
        /// </summary>
        public void PostPatchCallback()
        {
            StartCoroutine("LoadAvionicsSystemAssets");
        }

        /// <summary>
        /// Let ModuleManager know that I care about it reloading and patching values.
        /// </summary>
        private void RegisterWithModuleManager()
        {
            // Shh!  Using Linq.  But this is a one-shot event.
            var mmPatchLoader = AssemblyLoader.loadedAssemblies
                .Select(a => a.assembly.GetExportedTypes())
                .SelectMany(t => t)
                .FirstOrDefault(t => t.FullName == "ModuleManager.MMPatchLoader");

            if (mmPatchLoader == null)
            {
                return;
            }

            MethodInfo addPostPatchCallback = mmPatchLoader.GetMethod("addPostPatchCallback", BindingFlags.Static | BindingFlags.Public);

            if (addPostPatchCallback == null)
            {
                return;
            }

            try
            {
                var parms = addPostPatchCallback.GetParameters();
                if (parms.Length < 1)
                {
                    return;
                }

                Delegate callback = Delegate.CreateDelegate(parms[0].ParameterType, this, "PostPatchCallback");

                object[] args = new object[] { callback };

                addPostPatchCallback.Invoke(null, args);
            }
            catch (Exception e)
            {
                Utility.LogMessage(this, "addPostPatchCallback threw {0}", e);
            }
        }
    }
}