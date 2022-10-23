using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using System.IO;
using System.Collections.Generic;
using System;
using Newtonsoft.Json;
using System.Linq;

namespace ThunderRoad
{
    public class AssetBundleBuilderGUI : EditorWindow
    {
        public static List<AssetBundleGroup> assetBundleGroups = new List<AssetBundleGroup>();

        public static string gamePath;

        public static bool clearCache;
        public static bool runGameAfterBuild;
        public static string runGameArguments;
        public static bool cleanDestination = true;
        public static bool openFolderAfterExport = false;
        public static bool forceLinearFog = true;
        private Vector2 scrollPos;
        public static bool showManifest = false;

        [MenuItem("ThunderRoad (SDK)/Asset Bundle Builder")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow<AssetBundleBuilderGUI>("Asset Bundle Builder");
        }

        private void OnFocus()
        {
            gamePath = EditorPrefs.GetString("TRAB.GamePath");
            clearCache = EditorPrefs.GetBool("TRAB.ClearCache");
            runGameAfterBuild = EditorPrefs.GetBool("TRAB.RunGameAfterBuild");
            cleanDestination = EditorPrefs.GetBool("TRAB.CleanDestination");
            openFolderAfterExport = EditorPrefs.GetBool("TRAB.OpenFolderAfterExport");
            runGameArguments = EditorPrefs.GetString("TRAB.RunGameArguments");
            forceLinearFog = EditorPrefs.GetBool("TRAB.ForceLinearFog");

            assetBundleGroups = new List<AssetBundleGroup>();
            foreach (AssetBundleGroup assetBundleGroup in EditorCommon.GetAllProjectAssets<AssetBundleGroup>())
            {
                assetBundleGroups.Add(assetBundleGroup);
            }
        }

        public void RefreshAvailableAAGroups()
        {
            foreach (AddressableAssetGroup aaGroup in EditorCommon.GetAllProjectAssets<AddressableAssetGroup>())
            {
                if (AddressableAssetSettingsDefaultObject.Settings.groups.Contains(aaGroup)) continue;
                AddressableAssetSettingsDefaultObject.Settings.groups.Add(aaGroup);
            }
        }


        private void OnGUI()
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Label(new GUIContent("AssetBundle builder (" + EditorUserBuildSettings.activeBuildTarget + ")"), new GUIStyle("BoldLabel"));
            GUILayout.Space(5);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            int selectedCount = 0;
            foreach (AssetBundleGroup assetBundleGroup in assetBundleGroups.OrderBy(v => v.folderName))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginVertical(GUI.skin.box);
                {
                    GUILayout.Space(5); // Padding to top of group BG
                    EditorGUILayout.BeginHorizontal();
                    {
                        assetBundleGroup.selected = EditorGUILayout.Toggle(assetBundleGroup.selected, GUILayout.MaxWidth(20));


                        /* File/ModFolder renaming */
                        EditorGUI.BeginChangeCheck();
                        string folderName = EditorGUILayout.DelayedTextField(assetBundleGroup.folderName, GUILayout.MaxWidth(200));
                        if (EditorGUI.EndChangeCheck())
                        {
                            string path = AssetDatabase.GetAssetPath(assetBundleGroup);

                            if (!string.IsNullOrEmpty(path))
                            {
                                string output = AssetDatabase.RenameAsset(path, folderName);

                                if (!assetBundleGroup.isDefault)
                                {
                                    string catalogPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, assetBundleGroup.folderName);
                                    string newCatalogPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, folderName);
                                    if (Directory.Exists(catalogPath))
                                    {
                                        Directory.Move(catalogPath, newCatalogPath);
                                    }
                                    else
                                    {
                                        Directory.CreateDirectory(newCatalogPath);
                                    }

                                    // update manifest mod name
                                    //read manifest
                                    ModData modData = ReadManifest(folderName);
                                    modData.Name = folderName; //update the mod name to match new folder name
                                    //Save mod data
                                    CreateModManifest(modData);
                                    
                                    //Rename mod folder
                                    if (AssetDatabase.IsValidFolder($"Assets/Mods/{assetBundleGroup.folderName}"))
                                    {
                                        AssetDatabase.MoveAsset($"Assets/Mods/{assetBundleGroup.folderName}", $"Assets/Mods/{folderName}");
                                        Debug.Log($"Renamed Mod folder from Assets/Mods/{assetBundleGroup.folderName} to Assets/Mods/{folderName}");
                                    }
                                }

                                if (!string.IsNullOrEmpty(output))
                                    Debug.LogWarning("Failed to rename! " + output);
                                else
                                {
                                    assetBundleGroup.folderName = folderName;
                                }
                            }
                        }

                        if (assetBundleGroup.selected)
                            selectedCount++;

                        // Disabled link can be clicked to highlight AssetBundleGroup in project files
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField(assetBundleGroup, typeof(AssetBundleGroup), false);
                        EditorGUI.EndDisabledGroup();

                        if (GUILayout.Button("X", GUILayout.MaxWidth(20)) && EditorUtility.DisplayDialogComplex("Warning", $"Are you sure you want to delete group '{assetBundleGroup.folderName}'? Your Json files will be moved to '_deleted_{assetBundleGroup.folderName}'", "Yes", "Cancel", "No") == 0)
                        {
                            string path = AssetDatabase.GetAssetPath(assetBundleGroup);
                            int bundleIndex = assetBundleGroups.IndexOf(assetBundleGroup);

                            if (!string.IsNullOrEmpty(path) && AssetDatabase.MoveAssetToTrash(path))
                            {
                                assetBundleGroups.RemoveAt(bundleIndex);
                                if (!assetBundleGroup.isDefault)
                                {
                                    //Delete catalog
                                    string catalogPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, assetBundleGroup.folderName);
                                    string deletedCatalogPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, "_deleted_" + assetBundleGroup.folderName);
                                    Debug.Log($"Deleting: {catalogPath} by moving it to {deletedCatalogPath}");
                                    Directory.Move(catalogPath, deletedCatalogPath);
                                }
                                return; // Exit to redraw
                            }
                            else
                            {
                                Debug.LogError("Failed to delete group!");
                            }

                        }


                    }
                    EditorGUILayout.EndHorizontal();

                    /* Listing addressable groups of selected asset group */
                    if (assetBundleGroup.selected)
                    {
                        GUILayout.Space(3);

                        GUILayout.Label("Export Settings", new GUIStyle("BoldLabel"));

                        /* Export Settings */

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(25);
                        assetBundleGroup.exportAfterBuild = EditorGUILayout.Toggle(assetBundleGroup.exportAfterBuild, GUILayout.MaxWidth(20));
                        GUILayout.Label("Export on build");

                        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android && GUILayout.Button("Export only catalog", GUILayout.Width(120)))
                        {
                            CopyCatalog(assetBundleGroup);
                        }

                        GUILayout.Space(25);
                        if (GUILayout.Button("Export now", GUILayout.Width(120)))
                        {
                            if (string.IsNullOrEmpty(AssetBundleBuilder.exportFolderName)) AssetBundleBuilder.exportFolderName = assetBundleGroup.folderName;
                            string assetsFolderPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.addressableEditorPath);
                            AssetBundleBuilder.CopyAssetsToBuild(assetsFolderPath, AssetBundleBuilder.exportFolderName, assetBundleGroup.isDefault, gamePath);
                            CopyCatalog(assetBundleGroup);
                            CopyDll(assetBundleGroup);
                            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android && openFolderAfterExport)
                            {
                                Application.OpenURL($"file://{GetWindowsAssetPath(assetBundleGroup)}");
                            }
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Space(8);
                        if (!assetBundleGroup.isDefault)
                        {
                            GUILayout.BeginHorizontal();

                            if (GUILayout.Button("Open Catalog", GUILayout.Width(120)))
                            {
                                string catalogPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, assetBundleGroup.folderName);
                                Directory.CreateDirectory(catalogPath);
                                Application.OpenURL($"file://{catalogPath}");
                                Debug.Log(catalogPath);
                            }

                            if (GUILayout.Button("Create Project Mod Folder"))
                            {
                                if (!AssetDatabase.IsValidFolder("Assets/Mods"))
                                {
                                    AssetDatabase.CreateFolder("Assets", "Mods");
                                }
                                if (!AssetDatabase.IsValidFolder($"Assets/Mods/{assetBundleGroup.folderName}"))
                                {
                                    AssetDatabase.CreateFolder("Assets/Mods", assetBundleGroup.folderName);
                                    AssetDatabase.CreateFolder($"Assets/Mods/{assetBundleGroup.folderName}", "Scripts");
                                    AssetDatabase.CreateFolder($"Assets/Mods/{assetBundleGroup.folderName}", "Scenes");
                                    AssetDatabase.CreateFolder($"Assets/Mods/{assetBundleGroup.folderName}", "Audio");
                                    AssetDatabase.CreateFolder($"Assets/Mods/{assetBundleGroup.folderName}", "Models");
                                    AssetDatabase.CreateFolder($"Assets/Mods/{assetBundleGroup.folderName}", "Prefabs");
                                    AssetDatabase.CreateFolder($"Assets/Mods/{assetBundleGroup.folderName}", "Shaders");
                                    AssetDatabase.CreateFolder($"Assets/Mods/{assetBundleGroup.folderName}", "Materials");
                                    //This will create an assembly def and also reference the real ThunderRoad and ThunderRoad plugins dll in the game install folder
                                    //So modders can write scripts as they would in an IDE directly referecing the DLLs - without conflicting with the SDK assemblies
                                    string assemblyDef = "{" + $"	\"name\": \"{assetBundleGroup.folderName}\"," + "  \"precompiledReferences\": [\"ThunderRoad.PluginsReal.dll\",\"ThunderRoadReal.dll\"]," + "	\"overrideReferences\": true," + "	\"autoReferenced\": true" + "}";

                                    File.WriteAllText($"Assets/Mods/{assetBundleGroup.folderName}/{assetBundleGroup.folderName}.asmdef", assemblyDef);
                                    AssetDatabase.Refresh();

                                }

                            }

                            GUILayout.EndHorizontal();

                            GUILayout.Space(8);
                            showManifest = EditorGUILayout.Foldout(showManifest, "Manifest");
                            if (showManifest)
                            {
                                /* Manifest update */
                                ModData modData = ReadManifest(assetBundleGroup.folderName);

                                EditorGUI.BeginChangeCheck();
                                modData.Name = assetBundleGroup.folderName;
                                modData.Description = EditorGUILayout.DelayedTextField("Description", modData.Description);
                                modData.Author = EditorGUILayout.DelayedTextField("Author", modData.Author);
                                modData.ModVersion = EditorGUILayout.DelayedTextField("ModVersion", modData.ModVersion);
                                modData.GameVersion = EditorGUILayout.DelayedTextField("GameVersion", modData.GameVersion);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    //Save mod data
                                    CreateModManifest(modData);
                                }
                            }
                        }
                        GUILayout.Space(8);
                        GUILayout.Label("Addressable Groups", new GUIStyle("BoldLabel"));

                        /* Render Fields */
                        for (int i = 0; i < assetBundleGroup.addressableAssetGroups.Count; i++)
                        {
                            EditorGUILayout.BeginHorizontal();
                            {
                                GUILayout.Space(25); // Indent entry


                                assetBundleGroup.addressableAssetGroups[i] =
                                    (AddressableAssetGroup)EditorGUILayout.ObjectField(assetBundleGroup.addressableAssetGroups[i], typeof(AddressableAssetGroup), false);

                                // Remove group entry
                                if (GUILayout.Button("-", GUILayout.MaxWidth(20)))
                                {
                                    assetBundleGroup.addressableAssetGroups.RemoveAt(i);
                                }

                            }
                            EditorGUILayout.EndHorizontal();
                        }

                        /* Render Last Controls */
                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.FlexibleSpace(); // Float button right
                            // New addressable group entry
                            if (GUILayout.Button("+", GUILayout.MaxWidth(20)))
                            {
                                assetBundleGroup.addressableAssetGroups.Add(null);
                            }
                        }
                        GUILayout.EndHorizontal();

                    }
                    GUILayout.Space(5); // Padding to bottom of group BG
                }
                EditorGUILayout.EndVertical();


                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(assetBundleGroup);
                }
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Create New Asset Group"))
            {
                if (File.Exists("Assets/SDK/AssetBundleGroups/NewMod.asset"))
                {
                    Debug.LogWarning("You already have a new asset group");
                }
                else
                {
                    AssetBundleGroup newGroup = CreateInstance<AssetBundleGroup>();
                    newGroup.folderName = "NewMod";
                    newGroup.addressableAssetGroups = new List<AddressableAssetGroup>();

                    AssetDatabase.CreateAsset(newGroup, "Assets/SDK/AssetBundleGroups/NewMod.asset");
                    assetBundleGroups.Add(newGroup);

                    if (!newGroup.isDefault)
                    {
                        CreateCatalogFolder(newGroup);
                        ModData modData = new ModData {
                            Name = newGroup.folderName,
                            Description = "",
                            Author = "",
                            ModVersion = "1.0.0",
                            GameVersion = "0.0.0.0"
                        };

                        CreateModManifest(modData);
                    }
                }
            }

            GUILayout.Space(5);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            EditorGUILayout.BeginHorizontal();
            {
                if (selectedCount == 0) EditorGUI.BeginDisabledGroup(true);
                if (GUILayout.Button("Build asset bundle group(s) selected"))
                {
                    BuildSelected();
                }
                EditorGUI.EndDisabledGroup();
                clearCache = GUILayout.Toggle(clearCache, new GUIContent("Clear build cache", ""));
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Label(new GUIContent("Export parameters"), new GUIStyle("BoldLabel"));
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();

            if (selectedCount != 1) EditorGUI.BeginDisabledGroup(true);
            bool newRunGameAfterBuild = GUILayout.Toggle(runGameAfterBuild, "Run game after build", GUILayout.Width(150));
            if (newRunGameAfterBuild != runGameAfterBuild)
            {
                EditorPrefs.SetBool("TRAB.RunGameAfterBuild", newRunGameAfterBuild);
                runGameAfterBuild = newRunGameAfterBuild;
            }
            EditorGUI.EndDisabledGroup();

            bool newCleanDestination = GUILayout.Toggle(cleanDestination, "Clean destination", GUILayout.Width(150));
            if (newCleanDestination != cleanDestination)
            {
                EditorPrefs.SetBool("TRAB.CleanDestination", newCleanDestination);
                cleanDestination = newCleanDestination;
            }
            bool newOpenFolderAfterExport = GUILayout.Toggle(openFolderAfterExport, "Open folder after export", GUILayout.Width(150));
            if (newOpenFolderAfterExport != openFolderAfterExport)
            {
                EditorPrefs.SetBool("TRAB.OpenFolderAfterExport", newOpenFolderAfterExport);
                openFolderAfterExport = newOpenFolderAfterExport;
            }
            GUILayout.EndHorizontal();

            if (runGameAfterBuild)
            {
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Arguments"), new GUIStyle("BoldLabel"), GUILayout.Width(150));
                string newRunGameArguments = GUILayout.TextField(runGameArguments, 25);
                if (newRunGameArguments != runGameArguments)
                {
                    EditorPrefs.SetString("TRAB.RunGameArguments", newRunGameArguments);
                    runGameArguments = newRunGameArguments;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Game folder Directory"), new GUIStyle("BoldLabel"), GUILayout.Width(150));
            if (GUILayout.Button(gamePath, new GUIStyle("textField")))
            {
                gamePath = EditorUtility.OpenFolderPanel("Select game folder", "", "");
                EditorPrefs.SetString("TRAB.GamePath", gamePath);
            }
            GUILayout.EndHorizontal();

            //This section should only appear for modders, where none of the assetBundles are "default"
            if (assetBundleGroups.All(bundle => !bundle.isDefault))
            {
                GUILayout.BeginHorizontal();
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android && GUILayout.Button("Setup ThunderRoad Package"))
                {
                    SetupThunderRoadPackage();
                }
                GUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void SetupThunderRoadPackage()
        { }

        public static void CopyCatalog(AssetBundleGroup assetBundleGroup)
        {
            //ExportFolderName may be null if a build hasnt been done, but copying json should still be feasible
            if (string.IsNullOrEmpty(AssetBundleBuilder.exportFolderName)) AssetBundleBuilder.exportFolderName = assetBundleGroup.folderName;
            string catalogFullPath = Path.Combine(Directory.GetCurrentDirectory(), AssetBundleBuilder.catalogLocalPath);
            string destinationCatalogPath = GetWindowsAssetPath(assetBundleGroup);
            AssetBundleBuilder.CopyDirectory(catalogFullPath, destinationCatalogPath);
            Debug.Log($"Copied catalog folder {catalogFullPath} to {destinationCatalogPath}");
        }
        public static ModData ReadManifest(string folderName)
        {
            string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, folderName, "manifest.json");
            if (File.Exists(manifestPath))
            {
                return JsonConvert.DeserializeObject<ModData>(File.ReadAllText(manifestPath));
            }
            return new ModData();
        }
        public static void CreateModManifest(ModData modData)
        {
            string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, modData.Name, "manifest.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(modData, Formatting.Indented));
            Debug.Log($"Updated manifest at: {manifestPath}");

        }
        public static void CreateCatalogFolder(AssetBundleGroup newGroup)
        {
            string catalogPath = Path.Combine(Directory.GetCurrentDirectory(), GameSettings.instance.catalogsEditorPath, newGroup.folderName);
            Directory.CreateDirectory(catalogPath);
        }

        protected static void BuildSelected()
        {
            foreach (AssetBundleGroup assetBundleGroup in assetBundleGroups)
            {
                if (assetBundleGroup.selected) Build(assetBundleGroup);
            }

            if ((EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64 || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows) && runGameAfterBuild)
            {
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                process.StartInfo.FileName = Path.Combine(gamePath, PlayerSettings.productName + ".exe");
                process.StartInfo.Arguments = runGameArguments;
                process.Start();
                Debug.Log("Start game: " + process.StartInfo.FileName + " " + process.StartInfo.Arguments);
            }

            // The end
            System.Media.SystemSounds.Asterisk.Play();
        }

        protected static void Build(AssetBundleGroup assetBundleGroup)
        {
            string gameName = PlayerSettings.productName;

            AssetBundleBuilder.Build(assetBundleGroup, clearCache);

            // Export
            if (assetBundleGroup.exportAfterBuild)
            {
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64 || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows)
                {
                    if (!File.Exists(Path.Combine(gamePath, gameName + ".exe")))
                    {
                        Debug.LogError("Target game not supported!");
                        return;
                    }

                    // Get paths
                    string assetsFullPath = Path.Combine(Directory.GetCurrentDirectory(), AssetBundleBuilder.assetsLocalPath);
                    string catalogFullPath = Path.Combine(Directory.GetCurrentDirectory(), AssetBundleBuilder.catalogLocalPath);
                    string destinationAssetsPath = "";
                    string destinationCatalogPath = "";
                    destinationAssetsPath = destinationCatalogPath = GetWindowsAssetPath(assetBundleGroup);

                    // Create folders if needed
                    if (!File.Exists(destinationAssetsPath)) Directory.CreateDirectory(destinationAssetsPath);
                    if (!File.Exists(destinationCatalogPath)) Directory.CreateDirectory(destinationCatalogPath);

                    // Clean destination path
                    if (cleanDestination)
                    {
                        foreach (string filePath in Directory.GetFiles(destinationAssetsPath, "*.*", SearchOption.AllDirectories)) File.Delete(filePath);
                        foreach (string filePath in Directory.GetFiles(destinationCatalogPath, "*.*", SearchOption.AllDirectories)) File.Delete(filePath);
                    }
                    else
                    {
                        foreach (string filePath in Directory.GetFiles(destinationAssetsPath, "catalog_*.json", SearchOption.AllDirectories)) File.Delete(filePath);
                        foreach (string filePath in Directory.GetFiles(destinationAssetsPath, "catalog_*.hash", SearchOption.AllDirectories)) File.Delete(filePath);

                        foreach (string filePath in Directory.GetFiles(destinationCatalogPath, "catalog_*.json", SearchOption.AllDirectories)) File.Delete(filePath);
                        foreach (string filePath in Directory.GetFiles(destinationCatalogPath, "catalog_*.hash", SearchOption.AllDirectories)) File.Delete(filePath);
                    }

                    // Copy addressable assets to destination path
                    AssetBundleBuilder.CopyDirectory(assetsFullPath, destinationAssetsPath);
                    Debug.Log("Copied addressable asset folder " + assetsFullPath + " to " + destinationAssetsPath);

                    // Copy json catalog to destination path
                    AssetBundleBuilder.CopyDirectory(catalogFullPath, destinationCatalogPath);
                    Debug.Log("Copied catalog folder " + catalogFullPath + " to " + destinationCatalogPath);

                    CopyDll(assetBundleGroup);

                    if (openFolderAfterExport)
                    {
                        Application.OpenURL($"file://{destinationAssetsPath}");
                    }
                }
                else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                {
                    string adbPath = GetAdbPath();
                    if (!File.Exists(adbPath))
                    {
                        Debug.LogError("Android SDK is not installed!");
                        Debug.LogError("Path not found " + adbPath);
                        return;
                    }
                    AndroidPushAssets(assetBundleGroup.isDefault, AssetBundleBuilder.exportFolderName);
                }
            }

            Debug.Log("Export done");
        }
        public static void CopyDll(AssetBundleGroup assetBundleGroup)
        {
            string destinationCatalogPath = GetWindowsAssetPath(assetBundleGroup);
            // Copy plugin dll if any
            string dllPath = $"{Path.Combine("BuildStaging", "Plugins", AssetBundleBuilder.exportFolderName)}/bin/Release/netstandard2.0/{AssetBundleBuilder.exportFolderName}.dll";
            if (File.Exists(dllPath))
            {
                File.Copy(dllPath, $"{destinationCatalogPath}/{AssetBundleBuilder.exportFolderName}.dll", true);
                Debug.Log($"Copied dll {dllPath} to {destinationCatalogPath}");
            }
            else
            {
                //try to grab it from the scriptAssemblies directory if its a mod
                if (!assetBundleGroup.isDefault)
                {
                    dllPath = $"{Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies")}/{AssetBundleBuilder.exportFolderName}.dll";
                    if (File.Exists(dllPath))
                    {
                        File.Copy(dllPath, $"{destinationCatalogPath}/{AssetBundleBuilder.exportFolderName}.dll", true);
                        Debug.Log($"Copied dll {dllPath} to {destinationCatalogPath}");
                    }
                    string pdbPath = $"{Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies")}/{AssetBundleBuilder.exportFolderName}.pdb";
                    if (File.Exists(pdbPath))
                    {
                        File.Copy(pdbPath, $"{destinationCatalogPath}/{AssetBundleBuilder.exportFolderName}.pdb", true);
                        Debug.Log($"Copied pdb {pdbPath} to {destinationCatalogPath}");
                    }
                }
            }
        }
        public static string GetWindowsAssetPath(AssetBundleGroup assetBundleGroup)
        {
            string gameName = PlayerSettings.productName;
            if (assetBundleGroup.isDefault) return Path.Combine(gamePath, $"{gameName}_Data/StreamingAssets/Default");
            return Path.Combine(gamePath, $"{gameName}_Data/StreamingAssets/Mods", AssetBundleBuilder.exportFolderName);
        }

        public static void AndroidPushAssets(bool toDefault, string folderName)
        {
            string buildPath = Path.Combine(Directory.GetCurrentDirectory(), "BuildStaging/AddressableAssets/Android", folderName);

            System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo.FileName = GetAdbPath();
            if (toDefault)
            {
                process.StartInfo.Arguments = "push " + buildPath + "/. /sdcard/Android/obb/" + PlayerSettings.applicationIdentifier;
            }
            else
            {
                process.StartInfo.Arguments = "push " + buildPath + "/. /sdcard/Android/data/" + PlayerSettings.applicationIdentifier + "/files/Mods/" + folderName;
            }
            process.Start();
            process.WaitForExit();

            // 0 == Success
            if (process.ExitCode != 0)
            {
                Debug.LogError("No Quest device found, assets will not be exported");
                return;
            }

            Debug.Log("Assets pushed to Quest device");
        }

        public static string GetAdbPath()
        {
            return Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
        }
    }

    [JsonObject]
    [Serializable]
    public class AssetBundleBuilderProfile
    {
        public AssetBundleBuilderProfile(string n = "", string f = "", Dictionary<string, bool> g = null)
        {
            profileName = n;
            exportFolder = f;
            groups = g;
        }

        public string profileName = "";
        public string exportFolder = "";
        public Dictionary<string, bool> groups = new Dictionary<string, bool>();
    }
}
