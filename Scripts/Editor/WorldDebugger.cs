﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using VRC.Core;
#if VRC_SDK_VRCSDK3
using VRC.SDKBase;
#endif
#if VRC_SDK_VRCSDK2
using VRCSDK2;
#endif
#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#endif

namespace VRWorldToolkit.WorldDebugger
{
    public class WorldDebugger : EditorWindow
    {
        private static Texture _BadFPS;
        private static Texture _GoodFPS;
        private static Texture _Tips;
        private static Texture _Info;
        private static Texture _Error;
        private static Texture _Warning;

        private static bool recheck = true;

        public enum MessageType
        {
            BadFPS = 0,
            GoodFPS = 1,
            Tips = 2,
            Error = 3,
            Warning = 4,
            Info = 5
        }

        static Texture GetDebuggerIcon(MessageType info_type)
        {
            if (!_BadFPS)
                _BadFPS = Resources.Load<Texture>("DebuggerIcons/Bad_FPS_Icon");
            if (!_GoodFPS)
                _GoodFPS = Resources.Load<Texture>("DebuggerIcons/Good_FPS_Icon");
            if (!_Tips)
                _Tips = Resources.Load<Texture>("DebuggerIcons/Performance_Tips");
            if (!_Info)
                _Info = Resources.Load<Texture>("DebuggerIcons/Performance_Info");
            if (!_Error)
                _Error = Resources.Load<Texture>("DebuggerIcons/Error_Icon");
            if (!_Warning)
                _Warning = Resources.Load<Texture>("DebuggerIcons/Warning_Icon");

            switch (info_type)
            {
                case MessageType.BadFPS:
                    return _BadFPS;
                case MessageType.GoodFPS:
                    return _GoodFPS;
                case MessageType.Tips:
                    return _Tips;
                case MessageType.Info:
                    return _Info;
                case MessageType.Error:
                    return _Error;
                case MessageType.Warning:
                    return _Warning;
            }

            return _Info;
        }

        class SingleMessage
        {
            public string variable;
            public string variable2;
            public GameObject[] selectObjects;
            public System.Action autoFix;
            public string assetPath;

            public SingleMessage(string variable)
            {
                this.variable = variable;
            }

            public SingleMessage(string variable, string variable2)
            {
                this.variable = variable;
                this.variable2 = variable2;
            }

            public SingleMessage(GameObject[] selectObjects)
            {
                this.selectObjects = selectObjects;
            }

            public SingleMessage(GameObject selectObjects)
            {
                this.selectObjects = new GameObject[] { selectObjects };
            }

            public SingleMessage(System.Action autoFix)
            {
                this.autoFix = autoFix;
            }

            public SingleMessage setSelectObject(GameObject[] selectObjects)
            {
                this.selectObjects = selectObjects;
                return this;
            }

            public SingleMessage setSelectObject(GameObject selectObjects)
            {
                this.selectObjects = new GameObject[] { selectObjects };
                return this;
            }

            public SingleMessage setAutoFix(System.Action autoFix)
            {
                this.autoFix = autoFix;
                return this;
            }

            public SingleMessage setAssetPath(string assetPath)
            {
                this.assetPath = assetPath;
                return this;
            }
        }

        class MessageGroup : IEquatable<MessageGroup>
        {
            public bool showAll = false;
            public string message;
            public string combinedMessage;
            public MessageType messageType;

            public string documentation;

            public System.Action groupAutoFix;

            public List<SingleMessage> messageList = new List<SingleMessage>();

            public MessageGroup(string message, MessageType messageType)
            {
                this.message = message;
                this.messageType = messageType;
            }

            public MessageGroup(string message, string combinedMessage, MessageType messageType)
            {
                this.message = message;
                this.combinedMessage = combinedMessage;
                this.messageType = messageType;
            }

            public MessageGroup setGroupAutoFix(System.Action groupAutoFix)
            {
                this.groupAutoFix = groupAutoFix;
                return this;
            }

            public MessageGroup setDocumentation(string documentation)
            {
                this.documentation = documentation;
                return this;
            }

            public MessageGroup addSingleMessage(SingleMessage message)
            {
                messageList.Add(message);
                return this;
            }

            public MessageGroup setMessageList(List<SingleMessage> messageList)
            {
                this.messageList = messageList;
                return this;
            }

            public int getTotalCount()
            {
                int count = 0;

                if (messageList != null)
                {
                    foreach (var item in messageList)
                    {
                        if (item.selectObjects != null)
                        {
                            count += item.selectObjects.Count();
                        }
                        else
                        {
                            if (item.assetPath != null)
                            {
                                count++;
                            }
                        }
                    }
                }

                if (count == 0)
                {
                    return messageList.Count;
                }

                return count;
            }

            public GameObject[] getSelectObjects()
            {
                List<GameObject> objs = new List<GameObject>();
                foreach (var item in messageList.Where(o => o.selectObjects != null))
                {
                    objs.AddRange(item.selectObjects);
                }
                return objs.ToArray();
            }

            public System.Action[] getSeparateActions()
            {
                return messageList.Where(m => m.autoFix != null).Select(m => m.autoFix).ToArray();
            }

            public bool buttons()
            {
                bool buttons = false;
                if (getSelectObjects().Any() || groupAutoFix != null || getSeparateActions().Any() || groupAutoFix != null || documentation != null)
                {
                    buttons = true;
                }
                return buttons;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as MessageGroup);
            }

            public bool Equals(MessageGroup other)
            {
                return other != null &&
                       message == other.message &&
                       combinedMessage == other.combinedMessage &&
                       messageType == other.messageType;
            }

            public override int GetHashCode()
            {
                var hashCode = 1898092009;
                hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(message);
                hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(combinedMessage);
                hashCode = hashCode * -1521134295 + messageType.GetHashCode();
                return hashCode;
            }

            public static bool operator ==(MessageGroup group1, MessageGroup group2)
            {
                return EqualityComparer<MessageGroup>.Default.Equals(group1, group2);
            }

            public static bool operator !=(MessageGroup group1, MessageGroup group2)
            {
                return !(group1 == group2);
            }
        }

        class MessageCategory
        {
            public List<MessageGroup> messageGroups = new List<MessageGroup>();

            Dictionary<int, bool> expandedGroups = new Dictionary<int, bool>();

            public string listName;
            public bool enabled;

            public MessageCategory(string listName)
            {
                this.listName = listName;
                enabled = false;
            }

            public void addMessageGroup(MessageGroup debuggerMessage)
            {
                messageGroups.Add(debuggerMessage);
            }

            public void ClearMessages()
            {
                messageGroups.Clear();
            }

            public bool isExpanded(MessageGroup mg)
            {
                int hash = mg.GetHashCode();
                if (expandedGroups.ContainsKey(hash))
                {
                    return expandedGroups[hash];
                }
                return false;
            }

            public void setExpanded(MessageGroup mg, bool expanded)
            {
                int hash = mg.GetHashCode();
                if (expandedGroups.ContainsKey(hash))
                {
                    expandedGroups[hash] = expanded;
                }
                else
                {
                    expandedGroups.Add(hash, expanded);
                }
            }
        }

        class MessageCategoryList
        {
            public List<MessageCategory> messageCategory = new List<MessageCategory>();

            public MessageCategory AddMessageCategory(string name)
            {
                MessageCategory newMessageCategory = new MessageCategory(name);
                messageCategory.Add(newMessageCategory);
                return newMessageCategory;
            }

            public void DrawTabSelector()
            {
                EditorGUILayout.BeginHorizontal();

                bool disableAll = false;

                foreach (var item in messageCategory)
                {
                    string button = "miniButtonMid";
                    if (messageCategory.First() == item)
                    {
                        button = "miniButtonLeft";
                    }
                    else if (messageCategory.Last() == item)
                    {
                        button = "miniButtonRight";
                    }
                    bool currentState = item.enabled;
                    item.enabled = GUILayout.Toggle(item.enabled, item.listName, button);
                    if (currentState != item.enabled)
                    {
                        disableAll = true;
                    }
                }
                if (disableAll && Event.current.clickCount == 2)
                {
                    foreach (var item2 in messageCategory)
                    {
                        item2.enabled = false;
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            public void ClearCategories()
            {
                foreach (var item in messageCategory)
                {
                    item.ClearMessages();
                }
            }

            private bool AllDisabled()
            {
                bool disabled = true;
                foreach (var item in messageCategory)
                {
                    if (item.enabled)
                    {
                        disabled = false;
                    }
                }
                return disabled;
            }

            private static string dynamicVariable = "%variable%";
            private static string dynamicVariable2 = "%variable2%";
            private static string countVariable = "%count%";
            private static GUIStyle boxStyle = new GUIStyle("HelpBox");

            public void DrawMessages()
            {
                foreach (var group in messageCategory)
                {
                    if (group.enabled || AllDisabled())
                    {
                        GUILayout.Label(group.listName, EditorStyles.boldLabel);

                        var buttonWidth = 80;
                        var buttonHeight = 20;

                        if (group.messageGroups.Count == 0)
                        {
                            DrawMessage("No messages found for " + group.listName + ".", MessageType.Info);
                        }

                        foreach (var messageGroup in group.messageGroups)
                        {
                            bool hasButtons = messageGroup.buttons();

                            if (messageGroup.messageList.Count > 0)
                            {
                                if (messageGroup.combinedMessage != null && messageGroup.messageList.Count != 1)
                                {
                                    EditorGUILayout.BeginHorizontal();

                                    string finalMessage = messageGroup.combinedMessage;

                                    finalMessage = finalMessage.Replace(countVariable, messageGroup.getTotalCount().ToString());

                                    if (hasButtons)
                                    {
                                        GUIContent Box = new GUIContent(finalMessage, GetDebuggerIcon(messageGroup.messageType));
                                        GUILayout.Box(Box, boxStyle, GUILayout.MinHeight(42), GUILayout.MinWidth(EditorGUIUtility.currentViewWidth - 107));

                                        EditorGUILayout.BeginVertical();

                                        if (messageGroup.documentation != null)
                                        {
                                            if (GUILayout.Button("Info", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                            {
                                                if (messageGroup.documentation != null)
                                                {
                                                    Application.OpenURL(messageGroup.documentation);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            EditorGUI.BeginDisabledGroup(messageGroup.getSelectObjects().Length == 0);

                                            if (GUILayout.Button("Select", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                            {
                                                Selection.objects = messageGroup.getSelectObjects();
                                            }

                                            EditorGUI.EndDisabledGroup();
                                        }

                                        EditorGUI.EndDisabledGroup();

                                        EditorGUI.BeginDisabledGroup(messageGroup.groupAutoFix == null);

                                        if (GUILayout.Button("Auto Fix", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                        {
                                            messageGroup.groupAutoFix();
                                            recheck = true;
                                        }

                                        EditorGUI.EndDisabledGroup();

                                        EditorGUILayout.EndVertical();
                                    }
                                    else
                                    {
                                        DrawMessage(finalMessage, messageGroup.messageType);
                                    }

                                    EditorGUILayout.EndHorizontal();

                                    bool expanded = group.isExpanded(messageGroup);

                                    expanded = EditorGUILayout.Foldout(expanded, "Show separate messages");

                                    group.setExpanded(messageGroup, expanded);

                                    GUIStyle boxStyleGray = new GUIStyle("HelpBox");
                                    boxStyleGray.margin = new RectOffset(18, 4, 4, 4);

                                    if (expanded)
                                    {
                                        foreach (var message in messageGroup.messageList)
                                        {
                                            EditorGUILayout.BeginHorizontal();

                                            string finalInvidualMessage = messageGroup.message;

                                            if (message.variable != null)
                                            {
                                                finalInvidualMessage = finalInvidualMessage.Replace(dynamicVariable, message.variable);
                                            }

                                            if (message.variable != null)
                                            {
                                                finalInvidualMessage = finalInvidualMessage.Replace(dynamicVariable2, message.variable2);
                                            }

                                            if (hasButtons)
                                            {
                                                GUIContent Box = new GUIContent(finalInvidualMessage, GetDebuggerIcon(messageGroup.messageType));
                                                GUILayout.Box(Box, boxStyleGray, GUILayout.MinHeight(42), GUILayout.MinWidth(EditorGUIUtility.currentViewWidth - 121));

                                                EditorGUILayout.BeginVertical();

                                                EditorGUI.BeginDisabledGroup(!(message.selectObjects != null || message.assetPath != null));

                                                if (GUILayout.Button("Select", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                                {
                                                    if (message.assetPath != null)
                                                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(message.assetPath));

                                                    else
                                                        Selection.objects = message.selectObjects;
                                                }

                                                EditorGUI.EndDisabledGroup();

                                                EditorGUI.BeginDisabledGroup(message.autoFix == null);

                                                if (GUILayout.Button("Auto Fix", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                                {
                                                    message.autoFix();
                                                    recheck = true;
                                                }

                                                EditorGUI.EndDisabledGroup();

                                                EditorGUILayout.EndVertical();
                                            }
                                            else
                                            {
                                                DrawMessage(finalInvidualMessage, messageGroup.messageType);
                                            }

                                            EditorGUILayout.EndHorizontal();
                                        }

                                        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                                    }
                                }
                                else
                                {
                                    foreach (var message in messageGroup.messageList)
                                    {
                                        EditorGUILayout.BeginHorizontal();

                                        string finalMessage = messageGroup.message;

                                        if (message.variable != null)
                                        {
                                            finalMessage = finalMessage.Replace(dynamicVariable, message.variable);
                                        }

                                        if (message.variable != null)
                                        {
                                            finalMessage = finalMessage.Replace(dynamicVariable2, message.variable2);
                                        }

                                        if (hasButtons)
                                        {
                                            GUIContent Box = new GUIContent(finalMessage, GetDebuggerIcon(messageGroup.messageType));
                                            GUILayout.Box(Box, boxStyle, GUILayout.MinHeight(42), GUILayout.MinWidth(EditorGUIUtility.currentViewWidth - 107));

                                            EditorGUILayout.BeginVertical();

                                            EditorGUI.BeginDisabledGroup(!(message.selectObjects != null || message.assetPath != null));

                                            if (GUILayout.Button("Select", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                            {
                                                if (message.assetPath != null)
                                                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(message.assetPath));

                                                else
                                                    Selection.objects = message.selectObjects;
                                            }

                                            EditorGUI.EndDisabledGroup();

                                            EditorGUI.BeginDisabledGroup(message.autoFix == null);

                                            if (GUILayout.Button("Auto Fix", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                            {
                                                message.autoFix();
                                                recheck = true;
                                            }

                                            EditorGUI.EndDisabledGroup();

                                            EditorGUILayout.EndVertical();
                                        }
                                        else
                                        {
                                            DrawMessage(finalMessage, messageGroup.messageType);
                                        }

                                        EditorGUILayout.EndHorizontal();
                                    }
                                }
                            }
                            else
                            {
                                if (hasButtons)
                                {
                                    EditorGUILayout.BeginHorizontal();

                                    GUIContent Box = new GUIContent(messageGroup.message, GetDebuggerIcon(messageGroup.messageType));
                                    GUILayout.Box(Box, boxStyle, GUILayout.MinHeight(42), GUILayout.MinWidth(EditorGUIUtility.currentViewWidth - 107));

                                    EditorGUILayout.BeginVertical();

                                    EditorGUI.BeginDisabledGroup(messageGroup.documentation == null);

                                    if (GUILayout.Button("Info", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                    {
                                        if (messageGroup.documentation != null)
                                        {
                                            Application.OpenURL(messageGroup.documentation);
                                        }
                                    }

                                    EditorGUI.EndDisabledGroup();

                                    EditorGUI.BeginDisabledGroup(messageGroup.groupAutoFix == null);

                                    if (GUILayout.Button("Auto Fix", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                    {
                                        if (messageGroup.groupAutoFix != null)
                                        {
                                            messageGroup.groupAutoFix();
                                            recheck = true;
                                        }
                                    }

                                    EditorGUI.EndDisabledGroup();

                                    EditorGUILayout.EndVertical();

                                    EditorGUILayout.EndHorizontal();
                                }
                                else
                                {
                                    DrawMessage(messageGroup.message, messageGroup.messageType);
                                }
                            }
                        }
                    }
                }
            }

            public void DrawMessage(string messageText, MessageType type)
            {
                GUIContent Box = new GUIContent(messageText, GetDebuggerIcon(type));
                GUILayout.Box(Box, boxStyle, GUILayout.MinHeight(42), GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth - 18));
            }
        }

        Vector2 scrollPos;

        static string lastBuild = "Library/LastBuild.buildreport";

        static string buildReportDir = "Assets/_LastBuild/";

        static string assetPath = "Assets/_LastBuild/LastBuild.buildreport";

        static DateTime timeNow;

        private BuildReport buildReport;

        [MenuItem("VRWorld Toolkit/Open World Debugger", false, 0)]
        public static void ShowWindow()
        {
            if (!Directory.Exists(buildReportDir))
                Directory.CreateDirectory(buildReportDir);

            if (File.Exists(lastBuild))
            {
                File.Copy(lastBuild, assetPath, true);
                AssetDatabase.ImportAsset(assetPath);
            }

            var window = EditorWindow.GetWindow(typeof(WorldDebugger));
            window.titleContent = new GUIContent("World Debugger");
            window.minSize = new Vector2(530, 600);
            window.Show();
        }

        #region Actions
        System.Action SelectAsset(GameObject obj)
        {
            return () =>
            {
                Selection.activeObject = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(obj));
            };
        }

        System.Action SetGenerateLightmapUV(ModelImporter importer)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Enable lightmap UV generation?", "This operation will enable the lightmap UV generation on the mesh " + importer.name + ". Do you want to continue?", "Yes", "Cancel"))
                {
                    importer.generateSecondaryUV = true;
                    importer.SaveAndReimport();
                }
            };
        }

        System.Action SetGenerateLightmapUV(List<ModelImporter> importers)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Enable lightmap UV generation?", "This operation will enable the lightmap UV generation on " + importers.Count + " meshes. Do you want to continue?", "Yes", "Cancel"))
                {
                    foreach (var importer in importers)
                    {
                        importer.generateSecondaryUV = true;
                        importer.SaveAndReimport();
                    }
                }
            };
        }

        System.Action DisableComponent(Behaviour behaviour)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Disable component?", "This operation will disable the " + behaviour.GetType() + " on the GameObject \"" + behaviour.gameObject.name + "\". Do you want to continue?", "Yes", "Cancel"))
                {
                    behaviour.enabled = false;
                }
            };
        }

        System.Action DisableComponent(Behaviour[] behaviours)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Disable component?", "This operation will disable the " + behaviours[0].GetType() + " component on " + behaviours.Count().ToString() + " GameObjects. Do you want to continue?", "Yes", "Cancel"))
                {
                    foreach (var behaviour in behaviours)
                    {
                        behaviour.enabled = false;
                    }
                }
            };
        }

        System.Action SetObjectLayer(GameObject obj, string layer)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Change tag?", "This operation will change " + obj.name + " layer to " + layer + ". Do you want to continue?", "Yes", "Cancel"))
                {
                    obj.layer = LayerMask.NameToLayer(layer);
                }
            };
        }

        System.Action SetObjectLayer(GameObject[] objs, string layer)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Change layer?", "This operation will change " + objs.Length + " GameObjects layer to " + layer + ". Do you want to continue?", "Yes", "Cancel"))
                {
                    foreach (var obj in objs)
                    {
                        obj.layer = LayerMask.NameToLayer(layer);
                    }
                }
            };
        }

        System.Action SetLightmapSize(int newSize)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Change lightmap size?", "This operation will change your lightmap size from " + LightmapEditorSettings.maxAtlasSize + " to " + newSize + ". Do you want to continue?", "Yes", "Cancel"))
                {
                    LightmapEditorSettings.maxAtlasSize = newSize;
                }
            };
        }

        System.Action SetEnviromentReflections(DefaultReflectionMode reflections)
        {
            return () =>
            {
                RenderSettings.defaultReflectionMode = reflections;
            };
        }

        System.Action SetAmbientMode(AmbientMode ambientMode)
        {
            return () =>
            {
                RenderSettings.ambientMode = ambientMode;
            };
        }

        System.Action SetGameObjectTag(GameObject obj, string tag)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Change tag?", "This operation will change " + obj.name + " tag to " + tag + ". Do you want to continue?", "Yes", "Cancel"))
                {
                    obj.tag = tag;
                }
            };
        }

        System.Action SetGameObjectTag(GameObject[] objs, string tag)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Change tag?", "This operation will change " + objs.Length + " GameObjects tag to " + tag + ". Do you want to continue?", "Yes", "Cancel"))
                {
                    foreach (var obj in objs)
                    {
                        obj.tag = tag;
                    }
                }
            };
        }

        System.Action ChangeShader(Material material, String shader)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Change shader?", "This operation will change the shader of the material " + material.name + " to " + shader + ". Do You want to continue?", "Yes", "Cancel"))
                {
                    Shader standard = Shader.Find(shader);
                    material.shader = standard;
                }
            };
        }

        System.Action ChangeShader(Material[] materials, String shader)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Change shader?", "This operation will change the shader of " + materials.Length + " materials to " + shader + ". Do You want to continue?", "Yes", "Cancel"))
                {
                    Shader standard = Shader.Find(shader);
                    foreach (var material in materials)
                    {
                        material.shader = standard;
                    }
                }
            };
        }

        System.Action RemoveOverlappingLightprobes(LightProbeGroup lpg)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Remove overlapping light probes?", "This operation will remove any overlapping light probes in the group \"" + lpg.gameObject.name + "\". Do You want to continue?", "Yes", "Cancel"))
                {
                    lpg.probePositions = lpg.probePositions.Distinct().ToArray();
                }
            };
        }

        System.Action RemoveOverlappingLightprobes(LightProbeGroup[] lpgs)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Remove overlapping light probes?", "This operation will remove any overlapping light probes found in the current scene. Do You want to continue?", "Yes", "Cancel"))
                {
                    foreach (var lpg in lpgs)
                    {
                        lpg.probePositions = lpg.probePositions.Distinct().ToArray();
                    }
                }
            };
        }

        System.Action RemoveRedundantLightProbes(LightProbeGroup[] lpgs)
        {
            return () =>
            {
                if (LightmapSettings.lightProbes != null)
                {
                    var probes = LightmapSettings.lightProbes.positions;
                    if (EditorUtility.DisplayDialog("Remove redundant light probes?", "This operation will attempt to remove any redundant light probes in the current scene. Bake your lighting before this operation to avoid any correct light probes getting removed. Do You want to continue?", "Yes", "Cancel"))
                    {
                        foreach (var lpg in lpgs)
                        {
                            lpg.probePositions = lpg.probePositions.Distinct().Where(p => !probes.Contains(p)).ToArray();
                        }
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Baked lightprobes not found!", "Bake your lighting first before attempting to remove redundant light probes.", "Ok");
                }
            };
        }

        System.Action FixSpawns(VRC_SceneDescriptor descriptor)
        {
            return () =>
            {
                descriptor.spawns = descriptor.spawns.Where(c => c != null).ToArray();
                if (descriptor.spawns.Length == 0)
                {
                    descriptor.spawns = new Transform[] { descriptor.gameObject.transform };
                }
            };
        }

        System.Action SetErrorPause(bool enabled)
        {
            return () =>
            {
                ConsoleFlagUtil.SetConsoleErrorPause(enabled);
            };
        }

#if UNITY_POST_PROCESSING_STACK_V2
        System.Action SetReferenceCamera(VRC_SceneDescriptor descriptor)
        {
            return () =>
            {
                if (EditorUtility.DisplayDialog("Setup reference camera?", "This operation will try to set the reference camera in your scene descriptor. Do you want to continue?", "Yes", "Cancel"))
                {
                    if (Camera.main == null)
                    {
                        GameObject camera = new GameObject("Main Camera");
                        camera.AddComponent<Camera>();
                        camera.AddComponent<AudioListener>();
                        camera.tag = "MainCamera";
                    }
                    descriptor.ReferenceCamera = Camera.main.gameObject;
                    if (!Camera.main.gameObject.GetComponent<PostProcessLayer>())
                    {
                        descriptor.ReferenceCamera.gameObject.AddComponent(typeof(PostProcessLayer));
                        PostProcessLayer postprocess_layer = descriptor.ReferenceCamera.gameObject.GetComponent(typeof(PostProcessLayer)) as PostProcessLayer;
                        postprocess_layer.volumeLayer = LayerMask.GetMask("Water");
                    }
                }
            };
        }

        System.Action AddDefaultPPVolume()
        {
            return () =>
            {
                //Copy the example profile to the Post Processing folder
                if (!Directory.Exists("Assets/Post Processing"))
                    AssetDatabase.CreateFolder("Assets", "Post Processing");
                if (AssetDatabase.LoadAssetAtPath("Assets/Post Processing/SilentProfile.asset", typeof(PostProcessProfile)) == null)
                {
                    string path = AssetDatabase.GUIDToAssetPath("eaac6f7291834264f97854154e89bf76");
                    if (path != null)
                    {
                        AssetDatabase.CopyAsset(path, "Assets/Post Processing/SilentProfile.asset");
                    }
                }

                //Set up the post process volume
                PostProcessVolume volume = GameObject.Instantiate(PostProcessManager.instance.QuickVolume(16, 100f));
                if (File.Exists("Assets/Post Processing/SilentProfile.asset"))
                    volume.sharedProfile = (PostProcessProfile)AssetDatabase.LoadAssetAtPath("Assets/Post Processing/SilentProfile.asset", typeof(PostProcessProfile));
                volume.gameObject.name = "Post Processing Volume";
                volume.gameObject.layer = LayerMask.NameToLayer("Water");
            };
        }

        public enum RemovePSEffect
        {
            AmbientOcclusion = 0,
            ScreenSpaceReflections = 1,
            BloomDirt = 2
        }

        System.Action RemovePostProcessSetting(PostProcessProfile postprocess_profile, RemovePSEffect effect)
        {
            return () =>
            {
                switch (effect)
                {
                    case RemovePSEffect.AmbientOcclusion:
                        postprocess_profile.RemoveSettings<AmbientOcclusion>();
                        break;
                    case RemovePSEffect.ScreenSpaceReflections:
                        postprocess_profile.RemoveSettings<ScreenSpaceReflections>();
                        break;
                    case RemovePSEffect.BloomDirt:
                        postprocess_profile.GetSetting<Bloom>().dirtTexture.value = null;
                        postprocess_profile.GetSetting<Bloom>().dirtIntensity.value = 0;
                        break;
                    default:
                        break;
                }
            };
        }
#endif
        #endregion

        #region Texts
        private readonly string noSceneDescriptor = "Your scene has no Scene Descriptor. Please add one yourself, or drag the VRCWorld prefab to your scene.";
        private readonly string tooManySceneDescriptors = "You have multiple Scene Descriptors, you can only have one Scene Descriptor in a scene.";
        private readonly string tooManyPipelineManagers = "Your scene has multiple Pipeline Managers in it this can break the world upload process.";
        private readonly string worldDescriptorFar = "Your Scene Descriptor is %variable% units far from the the zero point in Unity. Having your world center out this far will cause some noticable jittering on models. You should move your world closer to the zero point of your scene.";
        private readonly string worldDescriptorOff = "Your Scene Descriptor is %variable% units far from the the zero point in Unity. It's usually good practice to try to keep it as close as possible to the absolute zero point to avoid floating point errors.";
        private readonly string noSpawnPointSet = "There are no spawn points set in your Scene Descriptor. Spawning into a world with no spawn point will cause you to get thrown back to your home world.";
        private readonly string nullSpawnPoint = "There is a null spawn point set in your Scene Descriptor. Spawning into a null spawn point will cause you to get thrown back to your home world.";
        private readonly string colliderUnderSpawnIsTrigger = "The only collider (%variable%) under your spawn point %variable2% has been set as a trigger! Spawning into a world with nothing to stand on will cause the players to fall forever.";
        private readonly string noColliderUnderSpawn = "Your spawn point %variable% doesn't have anything underneath it. Spawning into a world with nothing to stand on will cause the players to fall forever";
        private readonly string noPlayerMods = "No Player Mods found in the scene. Player mods are used for adding jumping and changing walking speed.";
        private readonly string triggerTriggerNotTrigger = "You have an OnEnterTrigger or OnExitTrigger Trigger \"%variable%\", but it's Collider has not been set as Is Trigger. These Triggers need to have a Collider set to be Is Trigger to work.";
        private readonly string colliderTriggerIsTrigger = "You have an OnEnterCollider or OnExitCollider Trigger \"%variable%\" that has a Collider set to be Is Trigger. These only react if the collider on the object has not been set to be Is Trigger.";
        private readonly string triggerTriggerNoCollider = "You have an OnEnterTrigger or OnExitTrigger Trigger \"%variable%\" that doesn't have a Collider on it.";
        private readonly string colliderTriggerNoCollider = "You have an OnEnterCollider or OnExitCollider Trigger \"%variable%\" that doesn't have a Collider on it.";
        private readonly string triggerTriggerWrongLayer = "You have an OnEnterTrigger or OnExitTrigger Trigger (%variable%) that is not on the MirrorReflection layer.";
        private readonly string combinedTriggerTriggerWrongLayer = "You have %count% OnEnterTrigger or OnExitTrigger Triggers that are not on the MirrorReflection layer. This can stop raycasts from working properly breaking buttons, UI Menus and pickups for example.";
        private readonly string mirrorOnByDefault = "The mirror %variable% is on by default.";
        private readonly string combinedMirrorsOnByDefault = "The scene has %count% mirrors on by default. This is a very bad practice and you should disable any mirrors in your world by default.";
        private readonly string mirrorWithDefaultLayers = "The mirror %variable% has the default Reflect Layers set.";
        private readonly string combinedMirrorWithDefaultLayers = "You have %count% mirrors that have the default Reflect Layers set. Only having the layers you need enabled in mirrors can save a lot of frames especially in populated instances.";
        private readonly string bakedOcclusionCulling = "Baked Occlusion Culling found.";
        private readonly string noOcclusionCulling = "You haven't baked Occlusion Culling yet. Occlusion culling gives you a lot more performance in your world, especially in larger worlds that have multiple rooms/areas.";
        private readonly string activeCameraOutputtingToRenderTexture = "Your scene has an active camera (%variable%) outputting to a render texture. This will affect performance negatively by causing more drawcalls to happen. Ideally you would only have it enabled when needed.";
        private readonly string combinedActiveCamerasOutputtingToRenderTextures = "The current scene has %count% active cameras outputting to render textures. This will affect performance negatively by causing more drawcalls to happen. Ideally you would only have them enabled when needed.";
        private readonly string noToonShaders = "You shouldn't use toon shaders for world building, as they're missing crucial things for making worlds. For world building the most recommended shader is Standard.";
        private readonly string nonCrunchedTextures = "%variable%% of the textures used in your scene haven't been crunch compressed. Crunch compression can greatly reduce the size of your world download. It can be accessed from the texture's import settings.";
        private readonly string switchToProgressive = "The scene is currently using the Enlighten lightmapper, which has been deprecated in newer versions of Unity. You should consider switching to Progressive for improved fidelity and performance.";
        private readonly string singleColorEnviromentLighting = "Consider changing your Enviroment Lighting to Gradient from Flat.";
        private readonly string darkEnviromentLighting = "Using dark colours for Environment Lighting can cause avatars to look weird. Only use dark Environment Lighting if your world has dark lighting.";
        private readonly string customEnviromentReflectionsNull = "Your Enviroment Reflections have been set to custom, but you haven't defined a custom cubemap!";
        private readonly string noLightmapUV = "Model found in the scene %variable% is set to be lightmapped but doesn't have Lightmap UVs.";
        private readonly string combineNoLightmapUV = "The current scene has %count% models set to be lightmapped that don't have Lightmap UVs. This causes issues when baking lighting. You can enable generating Lightmap UV's in the model's import settings.";
        private readonly string lightsNotBaked = "The current scenes lighting is not baked. Consider baking your lights for improved performance.";
        private readonly string considerLargerLightmaps = "Consider increasing your Lightmap Size from %variable% to 4096. This allows for more stuff to fit on a single lightmap, leaving less textures that need to be sampled.";
        private readonly string considerSmallerLightmaps = "Baking lightmaps at 4096 with Progressive GPU will silently fall back to CPU Progressive because it needs more than 12GB GPU Memory to be able to bake with GPU Progressive.";
        private readonly string nonBakedBakedLights = "The light \"%variable%\" is set to be baked/mixed but it hasn't been baked yet! Baked lights that haven't been baked yet function as realtime lights ingame.";
        private readonly string combinedNonBakedBakedLights = "The scene contains %count% baked/mixed lights that haven't been baked! Baked lights that haven't been baked yet function as realtime lights ingame.";
        private readonly string lightingDataAssetInfo = "Your lighting data asset takes up %variable% MB of your world's size. This contains your scene's light probe data and realtime GI data.";
        private readonly string noLightProbes = "No light probes found in the current scene, which means your baked lights won't affect dynamic objects such as players and pickups.";
        private readonly string lightProbeCountNotBaked = "The current scene contains %variable% light probes, but %variable2% of them haven't been baked yet.";
        private readonly string lightProbesRemovedNotReBaked = "You've removed some light probes after the last bake, bake them again to update your scene's lighting data. The lighting data contains %variable% baked light probes and the current scene has %variable2% light probes.";
        private readonly string lightProbeCount = "The current scene contains %variable% baked light probes.";
        private readonly string overlappingLightProbes = "Light Probe Group \"%variable%\" has %variable2% overlapping light probes. These can cause a slowdown in the editor and won't get baked because Unity will skip any extra overlapping probes.";
        private readonly string combinedOverlappingLightProbes = "%count% Light Probe Groups with overlapping light probes found. These can cause a slowdown in the editor and won't get baked because Unity will skip any extra overlapping probes.";
        private readonly string noReflectionProbes = "Your scene has no active reflection probes. Reflection probes are needed to have proper reflections on reflective materials.";
        private readonly string reflectionProbesSomeUnbaked = "The reflection probe named \"%variable%\" is unbaked.";
        private readonly string combinedReflectionProbesSomeUnbaked = "Your scene has %count% unbaked reflection probes.";
        private readonly string reflectionProbeCountText = "Your scene has %variable% baked reflection probes.";
        private readonly string postProcessingImportedButNotSetup = "Your project has Post Processing imported, but you haven't set it up yet.";
        private readonly string noReferenceCameraSet = "Your Scene Descriptor has no Reference Camera set. Without a Reference Camera set, you won't be able to see Post Processing ingame.";
        private readonly string noPostProcessingVolumes = "You don't have any Post Processing Volumes in your scene. A Post Processing Volume is needed to apply effects to the camera's Post Processing Layer.";
        private readonly string referenceCameraNoPostProcessingLayer = "Your Reference Camera doesn't have a Post Processing Layer on it. A Post Processing Layer is needed for the Post Processing Volume to affect the camera.";
        private readonly string volumeBlendingLayerNotSet = "You don't have a Volume Blending Layer set in your Post Process Layer, so post processing won't work. Using the Water layer is recommended.";
        private readonly string postProcessingVolumeNotGlobalNoCollider = "The Post Processing Volume \"%variable%\" isn't marked as Global and doesn't have a collider. It won't affect the camera without one of these set on it.";
        private readonly string noProfileSet = "You don't have a profile set in the Post Processing Volume %variable%";
        private readonly string volumeOnWrongLayer = "Your Post Processing Volume \"%variable%\" is not on one of the layers set in your cameras Post Processing Layer setting. (Currently: %variable2%)";
        private readonly string dontUseNoneForTonemapping = "Use either Neutral or ACES for Color Grading tonemapping, using None is the same as not using Color Grading.";
        private readonly string tooHighBloomIntensity = "Don't raise the Bloom intensity too high! You should use a low Bloom intensity, between 0.01 to 0.3.";
        private readonly string tooHighBloomThreshold = "You should avoid having your Bloom threshold set high. It might cause unexpected problems with avatars. Ideally you should keep it at 0, but always below 1.0.";
        private readonly string noBloomDirtInVR = "Don't use Bloom Dirt, it looks really bad in VR!";
        private readonly string noAmbientOcclusion = "Don't use Ambient Occlusion in VRChat! VRchat is using Forward rendering, so it gets applied on top of everything else, which is bad! It also has a super high rendering cost in VR.";
        private readonly string depthOfFieldWarning = "Depth of field has a high performance cost, and is very disorientating in VR. If you really want to use depth of field, have it be disabled by default.";
        private readonly string screenSpaceReflectionsWarning = "Screen Space Reflections only works when using deferred rendering. VRchat isn't using deferred rendering, so this will have no effect on the main camera.";
        private readonly string vignetteWarning = "Only use vignette in very small amounts. A powerful vignette can cause sickness in VR.";
        private readonly string noPostProcessingImported = "You haven't imported Post Processing to your project yet.";
        private readonly string questBakedLightingWarning = "You should bake lights for content build for Quest.";
        private readonly string ambientModeSetToCustom = "Your Environment Lighting setting is broken. This will override all light probes in the scene with black ambient light. Please change it to something else.";
        private readonly string noProblemsFoundInPP = "No problems found in your post processing setup. In some cases where post processing is working in editor but not in game it's possible some imported asset is causing it not to function properly.";
        private readonly string bakeryLightNotSetEditorOnly = "Your Bakery light named %variable% is not set to be EditorOnly this causes unnecessary errors in the output log loading into a world in VRChat because external scripts get removed in the upload process.";
        private readonly string combinedBakeryLightNotSetEditorOnly = "You have %count% Bakery lights are not set to be EditorOnly this causes unnecessary errors in the output log loading into a world in VRChat because external scripts get removed in the upload process.";
        private readonly string bakeryLightUnityLight = "Your Bakery light named %variable% has a Unity Light component on it this won't get baked with Bakery and will keep acting as real time even if set to baked.";
        private readonly string combinedBakeryLightUnityLight = "You have %count% Bakery lights that have a Unity Light component on it these will not get baked with Bakery and will keep acting as real time lights even if set to baked.";
        private readonly string missingShaderWarning = "The material %variable% found in your scene has a missing or broken shader.";
        private readonly string combinedMissingShaderWarning = "You have %count% materials found in your scene that have missing or broken shaders.";
        private readonly string errorPauseWarning = "You have Error Pause enabled in your console this can cause your world upload to fail by interrupting the build process.";
        #endregion

        MessageCategory general;
        MessageCategory optimization;
        MessageCategory lighting;
        MessageCategory postProcessing;

        public void CheckScene()
        {
            masterList.ClearCategories();

            if (general == null)
                general = masterList.AddMessageCategory("General");

            if (optimization == null)
                optimization = masterList.AddMessageCategory("Optimization");

            if (lighting == null)
                lighting = masterList.AddMessageCategory("Lighting");

            if (postProcessing == null)
                postProcessing = masterList.AddMessageCategory("Post Processing");

            //General Checks

            //Get Descriptors
            VRC_SceneDescriptor[] descriptors = FindObjectsOfType(typeof(VRC_SceneDescriptor)) as VRC_SceneDescriptor[];
            long descriptorCount = descriptors.Length;
            VRC_SceneDescriptor sceneDescriptor;
            PipelineManager[] pipelines = FindObjectsOfType(typeof(PipelineManager)) as PipelineManager[];

            //Check if a descriptor exists
            if (descriptorCount == 0)
            {
                general.addMessageGroup(new MessageGroup(noSceneDescriptor, MessageType.Error));
                return;
            }
            else
            {
                sceneDescriptor = descriptors[0];

                //Make sure only one descriptor exists
                if (descriptorCount > 1)
                {
                    general.addMessageGroup(new MessageGroup(tooManySceneDescriptors, MessageType.Info).addSingleMessage(new SingleMessage(Array.ConvertAll(descriptors, s => s.gameObject))));
                    return;
                }
                else if (pipelines.Length > 1)
                {
                    general.addMessageGroup(new MessageGroup(tooManyPipelineManagers, MessageType.Error).addSingleMessage(new SingleMessage(Array.ConvertAll(pipelines.ToArray(), s => s.gameObject))));
                }

                //Check how far the descriptor is from zero point for floating point errors
                int descriptorRemoteness = (int)Vector3.Distance(sceneDescriptor.transform.position, new Vector3(0.0f, 0.0f, 0.0f));

                if (descriptorRemoteness > 1000)
                {
                    general.addMessageGroup(new MessageGroup(worldDescriptorFar, MessageType.Error).addSingleMessage(new SingleMessage(descriptorRemoteness.ToString()).setSelectObject(Array.ConvertAll(descriptors, s => s.gameObject))));
                }
                else if (descriptorRemoteness > 250)
                {
                    general.addMessageGroup(new MessageGroup(worldDescriptorOff, MessageType.Error).addSingleMessage(new SingleMessage(descriptorRemoteness.ToString()).setSelectObject(Array.ConvertAll(descriptors, s => s.gameObject))));
                }
            }

            //Check if console has error pause on
            if (ConsoleFlagUtil.GetConsoleErrorPause())
            {
                general.addMessageGroup(new MessageGroup(errorPauseWarning, MessageType.Error).addSingleMessage(new SingleMessage(SetErrorPause(false))));
            }

            //Get spawn points for any possible problems
            Transform[] spawns = sceneDescriptor.spawns.Where(s => s != null).ToArray();

            int spawnsLength = sceneDescriptor.spawns.Length;
            bool emptySpawns = false;

            if (spawnsLength != spawns.Length)
            {
                emptySpawns = true;
            }

            if (spawns.Length == 0)
            {
                general.addMessageGroup(new MessageGroup(noSpawnPointSet, MessageType.Error).addSingleMessage(new SingleMessage(sceneDescriptor.gameObject).setAutoFix(FixSpawns(sceneDescriptor))));
            }
            else
            {
                if (emptySpawns)
                {
                    general.addMessageGroup(new MessageGroup(nullSpawnPoint, MessageType.Error).addSingleMessage(new SingleMessage(sceneDescriptor.gameObject).setAutoFix(FixSpawns(sceneDescriptor))));
                }

                foreach (var item in sceneDescriptor.spawns)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    RaycastHit hit;
                    if (!Physics.Raycast(item.position + new Vector3(0, 0.01f, 0), Vector3.down, out hit, Mathf.Infinity, 0, QueryTriggerInteraction.Ignore))
                    {
                        if (Physics.Raycast(item.position + new Vector3(0, 0.01f, 0), Vector3.down, out hit, Mathf.Infinity))
                        {
                            if (hit.collider.isTrigger)
                            {
                                general.addMessageGroup(new MessageGroup(colliderUnderSpawnIsTrigger, MessageType.Error).addSingleMessage(new SingleMessage(hit.collider.name, item.gameObject.name).setSelectObject(item.gameObject)));
                            }
                        }
                        else
                        {
                            general.addMessageGroup(new MessageGroup(noColliderUnderSpawn, MessageType.Error).addSingleMessage(new SingleMessage(item.gameObject.name).setSelectObject(item.gameObject)));
                        }
                    }
                }
            }

#if VRC_SDK_VRCSDK2
            //Check if the world has playermods defined
            VRC_PlayerMods[] playermods = FindObjectsOfType(typeof(VRC_PlayerMods)) as VRC_PlayerMods[];
            if (playermods.Length == 0)
            {
                general.addMessageGroup(new MessageGroup(noPlayerMods, MessageType.Tips));
            }

            //Get triggers in the world
            VRC_Trigger[] trigger_scripts = (VRC_Trigger[])VRC_Trigger.FindObjectsOfType(typeof(VRC_Trigger));

            List<GameObject> triggerWrongLayer = new List<GameObject>();

            //Check for OnEnterTriggers to make sure they are on mirrorreflection layer
            foreach (var trigger_script in trigger_scripts)
            {
                foreach (var trigger in trigger_script.Triggers)
                {
                    if (trigger.TriggerType == VRC_Trigger.TriggerType.OnEnterTrigger || trigger.TriggerType == VRC_Trigger.TriggerType.OnExitTrigger || trigger.TriggerType == VRC_Trigger.TriggerType.OnEnterCollider || trigger.TriggerType == VRC_Trigger.TriggerType.OnExitCollider)
                    {
                        if (trigger_script.gameObject.GetComponent<Collider>())
                        {
                            var collider = trigger_script.gameObject.GetComponent<Collider>();
                            if ((trigger.TriggerType.ToString() == "OnExitTrigger" || trigger.TriggerType.ToString() == "OnEnterTrigger") && !collider.isTrigger)
                            {
                                general.addMessageGroup(new MessageGroup(triggerTriggerNotTrigger, MessageType.Error).addSingleMessage(new SingleMessage(trigger_script.name).setSelectObject(trigger_script.gameObject)));
                            }
                            else if ((trigger.TriggerType.ToString() == "OnExitCollider" || trigger.TriggerType.ToString() == "OnEnterCollider") && collider.isTrigger)
                            {
                                general.addMessageGroup(new MessageGroup(colliderTriggerIsTrigger, MessageType.Error).addSingleMessage(new SingleMessage(trigger_script.name).setSelectObject(trigger_script.gameObject)));
                            }
                        }
                        else
                        {
                            if (trigger.TriggerType == VRC_Trigger.TriggerType.OnEnterTrigger || trigger.TriggerType == VRC_Trigger.TriggerType.OnExitTrigger)
                            {
                                general.addMessageGroup(new MessageGroup(triggerTriggerNoCollider, MessageType.Error).addSingleMessage(new SingleMessage(trigger_script.name).setSelectObject(trigger_script.gameObject)));
                            }
                            else if (trigger.TriggerType == VRC_Trigger.TriggerType.OnEnterCollider || trigger.TriggerType == VRC_Trigger.TriggerType.OnExitCollider)
                            {
                                general.addMessageGroup(new MessageGroup(colliderTriggerNoCollider, MessageType.Error).addSingleMessage(new SingleMessage(trigger_script.name).setSelectObject(trigger_script.gameObject)));
                            }
                        }
                        if ((trigger.TriggerType.ToString() == "OnEnterTrigger" || trigger.TriggerType.ToString() == "OnExitTrigger") && trigger_script.gameObject.layer != LayerMask.NameToLayer("MirrorReflection"))
                        {
                            triggerWrongLayer.Add(trigger_script.gameObject);
                        }
                    }
                }
            }

            if (triggerWrongLayer.Count > 0)
            {
                MessageGroup triggerWrongLayerGroup = new MessageGroup(triggerTriggerWrongLayer, combinedTriggerTriggerWrongLayer, MessageType.Warning);
                foreach (var item in triggerWrongLayer)
                {
                    triggerWrongLayerGroup.addSingleMessage(new SingleMessage(item.name).setSelectObject(item.gameObject).setAutoFix(SetObjectLayer(item.gameObject, "MirrorReflection")));
                }
                general.addMessageGroup(triggerWrongLayerGroup.setGroupAutoFix(SetObjectLayer(triggerWrongLayerGroup.getSelectObjects(), "MirrorReflection")));
            }
#endif

            //Optimization Checks

            //Check for occlusion culling
            if (StaticOcclusionCulling.umbraDataSize > 0)
            {
                optimization.addMessageGroup(new MessageGroup(bakedOcclusionCulling, MessageType.GoodFPS));
            }
            else
            {
                optimization.addMessageGroup(new MessageGroup(noOcclusionCulling, MessageType.Tips).setDocumentation("https://docs.unity3d.com/2018.4/Documentation/Manual/occlusion-culling-getting-started.html"));
            }

            //Check if there's any active cameras outputting to render textures
            List<GameObject> activeCameras = new List<GameObject>();
            int cameraCount = 0;
            Camera[] cameras = GameObject.FindObjectsOfType<Camera>();

            foreach (Camera camera in cameras)
            {
                if (camera.targetTexture)
                {
                    cameraCount++;
                    activeCameras.Add(camera.gameObject);
                }
            }

            if (cameraCount > 0 && cameraCount != 1)
            {
                MessageGroup activeCamerasMessages = new MessageGroup(activeCameraOutputtingToRenderTexture, combinedActiveCamerasOutputtingToRenderTextures, MessageType.BadFPS);
                foreach (var camera in activeCameras)
                {
                    activeCamerasMessages.addSingleMessage(new SingleMessage(cameraCount.ToString()).setSelectObject(camera.gameObject));
                }
                optimization.addMessageGroup(activeCamerasMessages);
            }

            //Get active mirrors in the world and complain about them
            VRC_MirrorReflection[] mirrors = FindObjectsOfType(typeof(VRC_MirrorReflection)) as VRC_MirrorReflection[];
            if (mirrors.Length > 0)
            {
                MessageGroup activeCamerasMessage = new MessageGroup(mirrorOnByDefault, combinedMirrorsOnByDefault, MessageType.BadFPS);
                foreach (var mirror in mirrors)
                {
                    activeCamerasMessage.addSingleMessage(new SingleMessage(mirror.name).setSelectObject(mirror.gameObject));
                }
                optimization.addMessageGroup(activeCamerasMessage);
            }

            //Lighting Checks

            if (RenderSettings.ambientMode.Equals(AmbientMode.Custom))
            {
                lighting.addMessageGroup(new MessageGroup(ambientModeSetToCustom, MessageType.Error).addSingleMessage(new SingleMessage(SetAmbientMode(AmbientMode.Skybox))));
            }

            if (RenderSettings.ambientMode.Equals(AmbientMode.Flat))
            {
                lighting.addMessageGroup(new MessageGroup(singleColorEnviromentLighting, MessageType.Tips));
            }

            if ((Helper.GetBrightness(RenderSettings.ambientLight) < 0.1f && RenderSettings.ambientMode.Equals(AmbientMode.Flat)) || (Helper.GetBrightness(RenderSettings.ambientSkyColor) < 0.1f && RenderSettings.ambientMode.Equals(AmbientMode.Trilight)) || (Helper.GetBrightness(RenderSettings.ambientEquatorColor) < 0.1f && RenderSettings.ambientMode.Equals(AmbientMode.Trilight)) || (Helper.GetBrightness(RenderSettings.ambientGroundColor) < 0.1f && RenderSettings.ambientMode.Equals(AmbientMode.Trilight)))
            {
                lighting.addMessageGroup(new MessageGroup(darkEnviromentLighting, MessageType.Tips));
            }

            if (RenderSettings.defaultReflectionMode.Equals(DefaultReflectionMode.Custom) && !RenderSettings.customReflection)
            {
                lighting.addMessageGroup(new MessageGroup(customEnviromentReflectionsNull, MessageType.Error).addSingleMessage(new SingleMessage(SetEnviromentReflections(DefaultReflectionMode.Skybox))));
            }

            bool bakedLighting = false;

#if BAKERY_INCLUDED
            List<GameObject> bakeryLights = new List<GameObject>();
            //TODO: Investigate whether or not these should be included
            //bakeryLights.AddRange(Array.ConvertAll(FindObjectsOfType(typeof(BakeryDirectLight)) as BakeryDirectLight[], s => s.gameObject));
            bakeryLights.AddRange(Array.ConvertAll(FindObjectsOfType(typeof(BakeryPointLight)) as BakeryPointLight[], s => s.gameObject));
            bakeryLights.AddRange(Array.ConvertAll(FindObjectsOfType(typeof(BakerySkyLight)) as BakerySkyLight[], s => s.gameObject));

            if (bakeryLights.Count > 0)
            {
                List<GameObject> notEditorOnly = new List<GameObject>();
                List<GameObject> unityLightOnBakeryLight = new List<GameObject>();

                bakedLighting = true;

                foreach (var obj in bakeryLights)
                {
                    if (obj.tag != "EditorOnly")
                    {
                        notEditorOnly.Add(obj);
                    }

                    if (obj.GetComponent<Light>())
                    {
                        Light light = obj.GetComponent<Light>();
                        if (!light.bakingOutput.isBaked && light.enabled)
                        {
                            unityLightOnBakeryLight.Add(obj);
                        }
                    }
                }

                if (notEditorOnly.Count > 0)
                {
                    MessageGroup notEditorOnlyGroup = new MessageGroup(bakeryLightNotSetEditorOnly, combinedBakeryLightNotSetEditorOnly, MessageType.Warning);
                    foreach (var item in notEditorOnly)
                    {
                        notEditorOnlyGroup.addSingleMessage(new SingleMessage(item.name).setAutoFix(SetGameObjectTag(item, "EditorOnly")).setSelectObject(item));
                    }
                    lighting.addMessageGroup(notEditorOnlyGroup.setGroupAutoFix(SetGameObjectTag(notEditorOnly.ToArray(), "EditorOnly")));
                }

                if (unityLightOnBakeryLight.Count > 0)
                {
                    MessageGroup unityLightGroup = new MessageGroup(bakeryLightUnityLight, combinedBakeryLightUnityLight, MessageType.Warning);
                    foreach (var item in unityLightOnBakeryLight)
                    {
                        unityLightGroup.addSingleMessage(new SingleMessage(item.name).setAutoFix(DisableComponent(item.GetComponent<Light>())).setSelectObject(item));
                    }
                    lighting.addMessageGroup(unityLightGroup.setGroupAutoFix(DisableComponent(Array.ConvertAll(unityLightOnBakeryLight.ToArray(), s => s.GetComponent<Light>()))));
                }
            }
#endif

            //Get lights in scene
            Light[] lights = FindObjectsOfType<Light>();

            List<GameObject> nonBakedLights = new List<GameObject>();

            //Go trough the lights to check if the scene contains lights set to be baked
            foreach (var light in lights)
            {
                if (light.lightmapBakeType == LightmapBakeType.Baked || light.lightmapBakeType == LightmapBakeType.Mixed)
                {
                    bakedLighting = true;
                    if (!light.bakingOutput.isBaked && light.GetComponent<Light>().enabled)
                    {
                        nonBakedLights.Add(light.gameObject);
                    }
                }
            }

            if (LightmapSettings.lightmaps.Length > 0)
            {
                bakedLighting = true;
            }

            var probes = LightmapSettings.lightProbes;

            //If the scene has baked lights complain about stuff important to baked lighting missing
            if (bakedLighting)
            {
                //Count lightmaps and suggest to use bigger lightmaps if needed
                int lightMapSize = 0;
                lightMapSize = LightmapEditorSettings.maxAtlasSize;
                if (lightMapSize != 4096 && LightmapSettings.lightmaps.Length > 1 && !LightmapEditorSettings.lightmapper.Equals(LightmapEditorSettings.Lightmapper.ProgressiveGPU))
                {
                    if (LightmapSettings.lightmaps[0] != null)
                    {
                        if (LightmapSettings.lightmaps[0].lightmapColor.height != 4096)
                        {
                            lighting.addMessageGroup(new MessageGroup(considerLargerLightmaps, MessageType.Tips).addSingleMessage(new SingleMessage(lightMapSize.ToString()).setAutoFix(SetLightmapSize(4096))));
                        }
                    }
                }

                if (LightmapEditorSettings.lightmapper.Equals(LightmapEditorSettings.Lightmapper.ProgressiveGPU) && lightMapSize == 4096 && SystemInfo.graphicsMemorySize < 12000)
                {
                    lighting.addMessageGroup(new MessageGroup(considerSmallerLightmaps, MessageType.Warning).addSingleMessage(new SingleMessage(lightMapSize.ToString()).setAutoFix(SetLightmapSize(2048))));
                }

                //Count how many light probes the scene has
                long probeCounter = 0;
                long bakedProbes = 0;

                if (probes != null)
                {
                    bakedProbes = probes.count;
                }

                LightProbeGroup[] lightprobegroups = GameObject.FindObjectsOfType<LightProbeGroup>();

                MessageGroup overlappingLightProbesGroup = new MessageGroup(overlappingLightProbes, combinedOverlappingLightProbes, MessageType.Info);

                foreach (LightProbeGroup lightprobegroup in lightprobegroups)
                {
                    if (lightprobegroup.probePositions.GroupBy(p => p).Any(g => g.Count() > 1))
                    {
                        overlappingLightProbesGroup.addSingleMessage(new SingleMessage(lightprobegroup.name, (lightprobegroup.probePositions.Length - lightprobegroup.probePositions.Distinct().ToArray().Length).ToString()).setSelectObject(lightprobegroup.gameObject).setAutoFix(RemoveOverlappingLightprobes(lightprobegroup)));
                    }

                    probeCounter += lightprobegroup.probePositions.Length;
                }

                if (probeCounter > 0)
                {
                    if ((probeCounter - bakedProbes) < 0)
                    {
                        lighting.addMessageGroup(new MessageGroup(lightProbesRemovedNotReBaked, MessageType.Warning).addSingleMessage(new SingleMessage(bakedProbes.ToString(), probeCounter.ToString())));
                    }
                    else
                    {
                        if ((bakedProbes - (0.9 * probeCounter)) < 0)
                        {
                            lighting.addMessageGroup(new MessageGroup(lightProbeCountNotBaked, MessageType.Info).addSingleMessage(new SingleMessage(probeCounter.ToString("n0"), (probeCounter - bakedProbes).ToString("n0"))));
                        }
                        else
                        {
                            lighting.addMessageGroup(new MessageGroup(lightProbeCount, MessageType.Info).addSingleMessage(new SingleMessage(probeCounter.ToString("n0"))));
                        }
                    }
                }

                if (overlappingLightProbesGroup.getTotalCount() > 0)
                {
                    if (overlappingLightProbesGroup.getTotalCount() > 1)
                    {
                        overlappingLightProbesGroup.setGroupAutoFix(RemoveOverlappingLightprobes(lightprobegroups));
                    }

                    lighting.addMessageGroup(overlappingLightProbesGroup);
                }

                //Since the scene has baked lights complain if there's no lightprobes
                else if (probes == null && probeCounter == 0)
                {
                    lighting.addMessageGroup(new MessageGroup(noLightProbes, MessageType.Info).setDocumentation("https://docs.unity3d.com/2018.4/Documentation/Manual/LightProbes.html"));
                }

                //Check lighting data asset size if it exists
                long length = 0;
                if (Lightmapping.lightingDataAsset != null)
                {
                    string lmdName = Lightmapping.lightingDataAsset.name;
                    string pathTo = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset);
                    length = new System.IO.FileInfo(pathTo).Length;
                    lighting.addMessageGroup(new MessageGroup(lightingDataAssetInfo, MessageType.Info).addSingleMessage(new SingleMessage((length / 1024.0f / 1024.0f).ToString("F2"))));
                }

#if !BAKERY_INCLUDED
                if (LightmapEditorSettings.lightmapper.Equals(LightmapEditorSettings.Lightmapper.Enlighten))
                {
                    lighting.addMessageGroup(new MessageGroup(switchToProgressive, MessageType.Tips));
                }
#endif

                if (nonBakedLights.Count != 0)
                {
                    MessageGroup nonBakedLightsGroup = new MessageGroup(nonBakedBakedLights, combinedNonBakedBakedLights, MessageType.BadFPS);
                    foreach (var item in nonBakedLights)
                    {
                        nonBakedLightsGroup.addSingleMessage(new SingleMessage(item.name).setSelectObject(item.gameObject));
                    }
                    lighting.addMessageGroup(nonBakedLightsGroup);
                }
            }
            else
            {
#if UNITY_ANDROID
                lighting.addMessageGroup(new MessageGroup(questBakedLightingWarning, MessageType.BadFPS));
#else
                lighting.addMessageGroup(new MessageGroup(lightsNotBaked, MessageType.Tips).addSingleMessage(new SingleMessage(nonBakedLights.ToArray())));
#endif
            }

            //ReflectionProbes
            ReflectionProbe[] reflectionprobes = GameObject.FindObjectsOfType<ReflectionProbe>();
            List<GameObject> unbakedprobes = new List<GameObject>();
            int reflectionProbeCount = 0;
            int reflectionProbesUnbaked = 0;
            foreach (ReflectionProbe reflectionprobe in reflectionprobes)
            {
                reflectionProbeCount++;
                if (!reflectionprobe.texture)
                {
                    reflectionProbesUnbaked++;
                    unbakedprobes.Add(reflectionprobe.gameObject);
                }
            }

            if (reflectionProbeCount == 0)
            {
                lighting.addMessageGroup(new MessageGroup(noReflectionProbes, MessageType.Tips).setDocumentation("https://docs.unity3d.com/2018.4/Documentation/Manual/class-ReflectionProbe.html"));
            }
            else if (reflectionProbesUnbaked > 0)
            {
                MessageGroup probesUnbakedGroup = new MessageGroup(reflectionProbesSomeUnbaked, combinedReflectionProbesSomeUnbaked, MessageType.Warning);
                foreach (var item in unbakedprobes)
                {
                    probesUnbakedGroup.addSingleMessage(new SingleMessage(item.name).setSelectObject(item));
                }
                lighting.addMessageGroup(probesUnbakedGroup);
            }
            else
            {
                lighting.addMessageGroup(new MessageGroup(reflectionProbeCountText, MessageType.Info).addSingleMessage(new SingleMessage(reflectionProbeCount.ToString())));
            }

            //Post Processing Checks

#if UNITY_POST_PROCESSING_STACK_V2
            PostProcessVolume[] PostProcessVolumes = FindObjectsOfType(typeof(PostProcessVolume)) as PostProcessVolume[];
            bool postProcessLayerExists = false;

            if (Camera.main == null)
            {
                if (sceneDescriptor.ReferenceCamera)
                {
                    if (sceneDescriptor.ReferenceCamera.gameObject.GetComponent(typeof(PostProcessLayer)))
                    {
                        postProcessLayerExists = true;
                    }
                }
            }
            else
            {
                if (Camera.main.gameObject.GetComponent(typeof(PostProcessLayer)))
                {
                    postProcessLayerExists = true;
                }
            }

            if (!sceneDescriptor.ReferenceCamera && PostProcessVolumes.Length == 0 && !postProcessLayerExists)
            {
                postProcessing.addMessageGroup(new MessageGroup(postProcessingImportedButNotSetup, MessageType.Info));
            }
            else
            {
                //Start by checking if reference camera has been set in the Scene Descriptor
                if (!sceneDescriptor.ReferenceCamera)
                {
                    postProcessing.addMessageGroup(new MessageGroup(noReferenceCameraSet, MessageType.Info).addSingleMessage(new SingleMessage(SetReferenceCamera(sceneDescriptor)).setSelectObject(sceneDescriptor.gameObject)));
                }
                else
                {
                    //Check for post process volumes in the scene
                    if (PostProcessVolumes.Length == 0)
                    {
                        postProcessing.addMessageGroup(new MessageGroup(noPostProcessingVolumes, MessageType.Info).addSingleMessage(new SingleMessage(AddDefaultPPVolume())));
                    }
                    else
                    {
                        PostProcessLayer postprocess_layer = sceneDescriptor.ReferenceCamera.GetComponent(typeof(PostProcessLayer)) as PostProcessLayer;
                        if (postprocess_layer == null)
                        {
                            postProcessing.addMessageGroup(new MessageGroup(referenceCameraNoPostProcessingLayer, MessageType.Error).addSingleMessage(new SingleMessage(postprocess_layer.gameObject)));
                        }

                        LayerMask volume_layer = postprocess_layer.volumeLayer;
                        if (volume_layer == LayerMask.GetMask("Nothing"))
                        {
                            postProcessing.addMessageGroup(new MessageGroup(volumeBlendingLayerNotSet, MessageType.Error).addSingleMessage(new SingleMessage(sceneDescriptor.ReferenceCamera.gameObject)));
                        }

                        foreach (PostProcessVolume postprocess_volume in PostProcessVolumes)
                        {
                            //Check if the layer matches the cameras post processing layer
                            if (postprocess_layer.volumeLayer != (postprocess_layer.volumeLayer | (1 << postprocess_volume.gameObject.layer)))
                            {
                                postProcessing.addMessageGroup(new MessageGroup(volumeOnWrongLayer, MessageType.Error).addSingleMessage(new SingleMessage(postprocess_volume.gameObject.name, Helper.GetAllLayersFromMask(postprocess_layer.volumeLayer)).setSelectObject(postprocess_volume.gameObject)));
                            }

                            //Check if the volume has a profile set
                            if (!postprocess_volume.profile && !postprocess_volume.sharedProfile)
                            {
                                postProcessing.addMessageGroup(new MessageGroup(noProfileSet, MessageType.Error).addSingleMessage(new SingleMessage(postprocess_volume.gameObject.name)));
                                continue;
                            }

                            if (!postprocess_volume.isGlobal)
                            {
                                //Check if the collider is either global or has a collider on it
                                if (!postprocess_volume.GetComponent<Collider>())
                                {
                                    GameObject[] objs = { postprocess_volume.gameObject };
                                    postProcessing.addMessageGroup(new MessageGroup(postProcessingVolumeNotGlobalNoCollider, MessageType.Error).addSingleMessage(new SingleMessage(postprocess_volume.name).setSelectObject(postprocess_volume.gameObject)));
                                }
                            }
                            else
                            {
                                //Go trough the profile settings and see if any bad one's are used
                                PostProcessProfile postprocess_profile;

                                if (postprocess_volume.profile)
                                    postprocess_profile = postprocess_volume.profile as PostProcessProfile;
                                else
                                    postprocess_profile = postprocess_volume.sharedProfile as PostProcessProfile;

                                if (postprocess_profile.GetSetting<ColorGrading>())
                                {
                                    if (postprocess_profile.GetSetting<ColorGrading>().tonemapper.value.ToString() == "None")
                                    {
                                        postProcessing.addMessageGroup(new MessageGroup(dontUseNoneForTonemapping, MessageType.Error).addSingleMessage(new SingleMessage(postprocess_layer.gameObject)));
                                    }
                                }

                                if (postprocess_profile.GetSetting<Bloom>())
                                {
                                    if (postprocess_profile.GetSetting<Bloom>().intensity.value > 0.3f)
                                    {
                                        postProcessing.addMessageGroup(new MessageGroup(tooHighBloomIntensity, MessageType.Warning).addSingleMessage(new SingleMessage(postprocess_layer.gameObject)));
                                    }

                                    if (postprocess_profile.GetSetting<Bloom>().threshold.value > 1f)
                                    {
                                        postProcessing.addMessageGroup(new MessageGroup(tooHighBloomThreshold, MessageType.Warning).addSingleMessage(new SingleMessage(postprocess_layer.gameObject)));
                                    }

                                    if (postprocess_profile.GetSetting<Bloom>().dirtTexture.value || postprocess_profile.GetSetting<Bloom>().dirtIntensity.value != 0)
                                    {
                                        postProcessing.addMessageGroup(new MessageGroup(noBloomDirtInVR, MessageType.Error).addSingleMessage(new SingleMessage(RemovePostProcessSetting(postprocess_profile, RemovePSEffect.BloomDirt)).setSelectObject(postprocess_layer.gameObject)));
                                    }
                                }
                                if (postprocess_profile.GetSetting<AmbientOcclusion>())
                                {
                                    postProcessing.addMessageGroup(new MessageGroup(noAmbientOcclusion, MessageType.Error).addSingleMessage(new SingleMessage(RemovePostProcessSetting(postprocess_profile, RemovePSEffect.AmbientOcclusion)).setSelectObject(postprocess_layer.gameObject)));
                                }

                                if (postprocess_profile.GetSetting<DepthOfField>() && postprocess_volume.isGlobal)
                                {
                                    postProcessing.addMessageGroup(new MessageGroup(depthOfFieldWarning, MessageType.Warning).addSingleMessage(new SingleMessage(postprocess_layer.gameObject)));
                                }

                                if (postprocess_profile.GetSetting<ScreenSpaceReflections>())
                                {
                                    postProcessing.addMessageGroup(new MessageGroup(screenSpaceReflectionsWarning, MessageType.Warning).addSingleMessage(new SingleMessage(RemovePostProcessSetting(postprocess_profile, RemovePSEffect.ScreenSpaceReflections)).setSelectObject(postprocess_layer.gameObject)));
                                }

                                if (postprocess_profile.GetSetting<Vignette>())
                                {
                                    postProcessing.addMessageGroup(new MessageGroup(vignetteWarning, MessageType.Warning).addSingleMessage(new SingleMessage(postprocess_layer.gameObject)));
                                }
                            }
                        }
                    }
                }

                if (postProcessing.messageGroups.Count == 0)
                {
                    postProcessing.addMessageGroup(new MessageGroup(noProblemsFoundInPP, MessageType.Info));
                }
            }
#else
            postProcessing.addMessageGroup(new MessageGroup(noPostProcessingImported, MessageType.Info));
#endif

            //Gameobject checks

            List<ModelImporter> importers = new List<ModelImporter>();
            List<string> meshName = new List<string>();

            List<Texture> unCrunchedTextures = new List<Texture>();
            int badShaders = 0;
            int textureCount = 0;

            List<Material> missingShaders = new List<Material>();

            List<Material> checkedMaterials = new List<Material>();

            MessageGroup mirrorsDefaultLayers = new MessageGroup(mirrorWithDefaultLayers, combinedMirrorWithDefaultLayers, MessageType.Tips);

            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
            {
                if (EditorUtility.IsPersistent(gameObject.transform.root.gameObject) && !(gameObject.hideFlags == HideFlags.NotEditable || gameObject.hideFlags == HideFlags.HideAndDontSave))
                    continue;

                if (gameObject.GetComponent<Renderer>())
                {
                    // If baked lighting in the scene check for lightmap uvs
                    if (bakedLighting)
                    {
                        if (GameObjectUtility.AreStaticEditorFlagsSet(gameObject, StaticEditorFlags.LightmapStatic) && gameObject.GetComponent<MeshRenderer>())
                        {
                            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
                            if (meshFilter == null)
                            {
                                continue;
                            }
                            Mesh _sharedMesh = meshFilter.sharedMesh;
                            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(_sharedMesh)) != null)
                            {
                                ModelImporter modelImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(_sharedMesh)) as ModelImporter;
                                if (!importers.Contains(modelImporter))
                                {
                                    if (modelImporter != null)
                                    {
                                        if (!modelImporter.generateSecondaryUV && _sharedMesh.uv2.Length == 0)
                                        {
                                            importers.Add(modelImporter);
                                            meshName.Add(_sharedMesh.name);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (gameObject.GetComponent<VRC_MirrorReflection>())
                    {
                        LayerMask mirrorMask = gameObject.GetComponent<VRC_MirrorReflection>().m_ReflectLayers;
                        if (mirrorMask.value == -1025)
                        {
                            mirrorsDefaultLayers.addSingleMessage(new SingleMessage(gameObject.name).setSelectObject(gameObject));
                        }
                    }

                    // Check materials for problems
                    Renderer meshRenderer = gameObject.GetComponent<Renderer>();

                    foreach (var material in meshRenderer.sharedMaterials)
                    {
                        if (material == null || checkedMaterials.Contains(material))
                            continue;

                        checkedMaterials.Add(material);

                        Shader shader = material.shader;
                        if (shader.name == "Hidden/InternalErrorShader" && !missingShaders.Contains(material))
                            missingShaders.Add(material);

                        if (shader.name.StartsWith(".poiyomi") || shader.name.StartsWith("poiyomi") || shader.name.StartsWith("arktoon") || shader.name.StartsWith("Cubedparadox") || shader.name.StartsWith("Silent's Cel Shading") || shader.name.StartsWith("Xiexe"))
                            badShaders++;

                        for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
                        {
                            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                Texture texture = material.GetTexture(ShaderUtil.GetPropertyName(shader, i));
                                if (AssetDatabase.GetAssetPath(texture) != "" && !unCrunchedTextures.Contains(texture))
                                {
                                    TextureImporter textureImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                                    if (textureImporter != null)
                                    {
                                        if (!unCrunchedTextures.Contains(texture))
                                        {
                                            textureCount++;
                                        }
                                        if (!textureImporter.crunchedCompression && !unCrunchedTextures.Contains(texture) && !textureImporter.textureCompression.Equals(TextureImporterCompression.Uncompressed) && EditorTextureUtil.GetStorageMemorySize(texture) > 500000)
                                        {
                                            unCrunchedTextures.Add(texture);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (mirrorsDefaultLayers.messageList.Count > 0)
            {
                optimization.addMessageGroup(mirrorsDefaultLayers);
            }

            //If more than 10% of shaders used in scene are toon shaders to leave room for people using them for avatar displays
            if (checkedMaterials.Count > 0)
            {
                if ((badShaders / checkedMaterials.Count * 100) > 10)
                {
                    optimization.addMessageGroup(new MessageGroup(noToonShaders, MessageType.Warning));
                }
            }

            //Suggest to crunch textures if there are any uncrunched textures found
            if (textureCount > 0)
            {
                int percent = (int)((float)unCrunchedTextures.Count / (float)textureCount * 100f);
                if (percent > 20)
                {
                    optimization.addMessageGroup(new MessageGroup(nonCrunchedTextures, MessageType.Tips).addSingleMessage(new SingleMessage(percent.ToString())));
                }
            }


            var modelsCount = importers.Count;
            if (modelsCount > 0)
            {
                MessageGroup noUVGroup = new MessageGroup(noLightmapUV, combineNoLightmapUV, MessageType.Warning);
                for (int i = 0; i < modelsCount; i++)
                {
                    string modelName = meshName[i];
                    ModelImporter modelImporter = importers[i];
                    noUVGroup.addSingleMessage(new SingleMessage(modelName).setAutoFix(SetGenerateLightmapUV(modelImporter)).setAssetPath(modelImporter.assetPath));
                }
                lighting.addMessageGroup(noUVGroup.setGroupAutoFix(SetGenerateLightmapUV(importers)).setDocumentation("https://docs.unity3d.com/2018.4/Documentation/Manual/LightingGiUvs-GeneratingLightmappingUVs.html"));
            }

            var missingShadersCount = missingShaders.Count;
            if (missingShadersCount > 0)
            {
                MessageGroup missingsShadersGroup = new MessageGroup(missingShaderWarning, combinedMissingShaderWarning, MessageType.Error);
                foreach (var material in missingShaders)
                {
                    missingsShadersGroup.addSingleMessage(new SingleMessage(material.name).setAssetPath(AssetDatabase.GetAssetPath(material)).setAutoFix(ChangeShader(material, "Standard")));
                }
                general.addMessageGroup(missingsShadersGroup.setGroupAutoFix(ChangeShader(missingShaders.ToArray(), "Standard")));
            }
        }

        MessageCategoryList masterList;

        void Awake()
        {
            timeNow = DateTime.Now.ToUniversalTime();
            buildReport = AssetDatabase.LoadAssetAtPath<BuildReport>(assetPath);
        }

        void OnFocus()
        {
            timeNow = DateTime.Now.ToUniversalTime();
            recheck = true;
        }

        void OnGUI()
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.richText = true;

            if (masterList == null)
            {
                masterList = new MessageCategoryList();
            }

            if (recheck)
            {
                CheckScene();
                recheck = false;
            }

            GUILayout.BeginVertical(EditorStyles.helpBox);

            if (buildReport != null)
            {
                GUILayout.Label("<b>Last build size:</b> " + Helper.FormatSize(buildReport.summary.totalSize), style);

                GUILayout.Label("<b>Last build was done:</b> " + Helper.FormatTime(timeNow.Subtract(buildReport.summary.buildEndedAt)), style);

                GUILayout.Label("<b>Errors last build:</b> " + buildReport.summary.totalErrors.ToString(), style);

                GUILayout.Label("<b>Warnings last build:</b> " + buildReport.summary.totalWarnings.ToString(), style);
            }
            else
            {
                GUILayout.Label("No build done yet");
            }

            GUILayout.EndVertical();

            masterList.DrawTabSelector();

            EditorGUILayout.BeginHorizontal();
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            masterList.DrawMessages();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}