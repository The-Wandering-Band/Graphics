using System;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    using _ = CoreEditorUtils;
    using CED = CoreEditorDrawer<PlanarReflectionProbeUI, SerializedPlanarReflectionProbe>;

    partial class PlanarReflectionProbeUI
    {
        public static readonly CED.IDrawer Inspector;

        public static readonly CED.IDrawer SectionCaptureSettings = CED.Group(
            CED.Action(Drawer_SectionCaptureSettings)
        );

        public static readonly CED.IDrawer SectionFoldoutAdvancedSettings = CED.FoldoutGroup(
            "Advanced Settings",
            (s, d, o) => s.isSectionExpandedAdvancedSettings,
            true,
            CED.Action(Drawer_SectionAdvancedSettings)
        );

        static PlanarReflectionProbeUI()
        {
            Inspector = CED.Group(
                SectionCaptureSettings,
                CED.space,
                CED.Action((s, d, o) => EditorGUILayout.LabelField(_.GetContent("Proxy Volume"), EditorStyles.boldLabel)),
                CED.Action(Drawer_FieldProxyVolumeReference),
                CED.space,
                CED.Action(Drawer_Toolbar),
                CED.space,
                CED.Select(
                    (s, d, o) => s.influenceVolume,
                    (s, d, o) => d.influenceVolume,
                    InfluenceVolumeUI.SectionFoldoutShape
                ),
                SectionFoldoutAdvancedSettings
            );
        }

        const EditMode.SceneViewEditMode EditBaseShape = EditMode.SceneViewEditMode.ReflectionProbeBox;
        const EditMode.SceneViewEditMode EditInfluenceShape = EditMode.SceneViewEditMode.GridBox;
        const EditMode.SceneViewEditMode EditInfluenceNormalShape = EditMode.SceneViewEditMode.Collider;
        const EditMode.SceneViewEditMode EditCenter = EditMode.SceneViewEditMode.ReflectionProbeOrigin;

        static void Drawer_SectionAdvancedSettings(PlanarReflectionProbeUI s, SerializedPlanarReflectionProbe d, Editor o)
        {
            EditorGUILayout.PropertyField(d.dimmer, _.GetContent("Dimmer"));
        }

        static void Drawer_SectionCaptureSettings(PlanarReflectionProbeUI s, SerializedPlanarReflectionProbe d, Editor o)
        {
            EditorGUILayout.LabelField(_.GetContent("Capture Settings"), EditorStyles.boldLabel);
            ++EditorGUI.indentLevel;
            EditorGUILayout.PropertyField(d.mode, _.GetContent("Type"));
            EditorGUILayout.PropertyField(d.captureOffset, _.GetContent("Capture Position"));
            --EditorGUI.indentLevel;
        }
        static void Drawer_FieldProxyVolumeReference(PlanarReflectionProbeUI s, SerializedPlanarReflectionProbe d, Editor o)
        {
            EditorGUILayout.PropertyField(d.proxyVolumeReference, _.GetContent("Reference"));
        }

        static readonly EditMode.SceneViewEditMode[] k_Toolbar_SceneViewEditModes =
        {
            EditBaseShape,
            EditInfluenceShape,
            EditInfluenceNormalShape,
            EditCenter
        };
        static GUIContent[] s_Toolbar_Contents = null;
        static GUIContent[] toolbar_Contents
        {
            get
            {
                return s_Toolbar_Contents ?? (s_Toolbar_Contents = new[]
                {
                    EditorGUIUtility.IconContent("EditCollider", "|Modify the base shape. (SHIFT+1)"),
                    EditorGUIUtility.IconContent("PreMatCube", "|Modify the influence volume. (SHIFT+2)"),
                    EditorGUIUtility.IconContent("SceneViewOrtho", "|Modify the influence normal volume. (SHIFT+3)"),
                    EditorGUIUtility.IconContent("MoveTool", "|Move the center.")
                });
            }
        }
        static void Drawer_Toolbar(PlanarReflectionProbeUI s, SerializedPlanarReflectionProbe d, Editor o)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.changed = false;

            EditMode.DoInspectorToolbar(k_Toolbar_SceneViewEditModes, toolbar_Contents, GetBoundsGetter(o), o);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        static Func<Bounds> GetBoundsGetter(Editor o)
        {
            return () =>
            {
                var bounds = new Bounds();
                foreach (Component targetObject in o.targets)
                {
                    var rp = targetObject.transform;
                    var b = rp.position;
                    bounds.Encapsulate(b);
                }
                return bounds;
            };
        }
    }
}
