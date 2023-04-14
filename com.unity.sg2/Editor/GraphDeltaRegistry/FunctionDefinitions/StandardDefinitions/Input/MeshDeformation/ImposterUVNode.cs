using UnityEditor.ShaderGraph.GraphDelta;
using Usage = UnityEditor.ShaderGraph.GraphDelta.GraphType.Usage;

namespace UnityEditor.ShaderGraph.Defs
{
    internal class ImposterUVNode : IStandardNode
    {
        static string Name => "ImposterUVNode";
        static int Version => 1;
        public static NodeDescriptor NodeDescriptor => new(
            Version,
            Name,
            "ImposteUV",
            functions: new FunctionDescriptor[] {
                     new(
                         "ThreeFrames",
"  ImposterUV(Pos, inUV, Frames, Offset, Size, HemiSphere, OutPos, Grid, UV0, UV1, UV2);",
            new ParameterDescriptor[]
            {
                new ParameterDescriptor("Pos", TYPE.Vec3, Usage.In, REF.ObjectSpace_Position),
                new ParameterDescriptor("inUV", TYPE.Vec4, Usage.In, REF.UV0),
                new ParameterDescriptor("Frames", TYPE.Float, Usage.In),
                new ParameterDescriptor("Offset", TYPE.Float, Usage.In),
                new ParameterDescriptor("Size", TYPE.Float, Usage.In),
                new ParameterDescriptor("HemiSphere", TYPE.Bool, Usage.In),
                new ParameterDescriptor("OutPos", TYPE.Vec3, Usage.Out),
                new ParameterDescriptor("UV0", TYPE.Vec4, Usage.Out),
                new ParameterDescriptor("UV1", TYPE.Vec4, Usage.Out),
                new ParameterDescriptor("UV2", TYPE.Vec4, Usage.Out),
                new ParameterDescriptor("Grid", TYPE.Vec4, Usage.Out)
            },
            new string[]
                {
                    "\"Packages/com.unity.shadergraph/ShaderGraphLibrary/Imposter_2Nodes.hlsl\""
                }
                ),new(
                         "OneFrames",
"  ImposterUV_oneFrame(Pos, inUV, Frames, Offset, Size, HemiSphere, OutPos, Grid, UV0);",
            new ParameterDescriptor[]
            {
                new ParameterDescriptor("Pos", TYPE.Vec3, Usage.In, REF.ObjectSpace_Position),
                new ParameterDescriptor("inUV", TYPE.Vec4, Usage.In, REF.UV0),
                new ParameterDescriptor("Frames", TYPE.Float, Usage.In),
                new ParameterDescriptor("Offset", TYPE.Float, Usage.In),
                new ParameterDescriptor("Size", TYPE.Float, Usage.In),
                new ParameterDescriptor("HemiSphere", TYPE.Bool, Usage.In),
                new ParameterDescriptor("OutPos", TYPE.Vec3, Usage.Out),
                new ParameterDescriptor("UV0", TYPE.Vec4, Usage.Out),
                new ParameterDescriptor("Grid", TYPE.Vec4, Usage.Out)
            },
            new string[]
                {
                    "\"Packages/com.unity.shadergraph/ShaderGraphLibrary/Imposter_2Nodes.hlsl\""
                }
                )
            }
        );

        public static NodeUIDescriptor NodeUIDescriptor => new(
            Version,
            Name,
            displayName: "Imposter UV",
            tooltip: "Calculates the billboard positon and the virtual UVs for sampling.",
            category: "Input/Mesh Deformation",
            hasPreview: false,
            description: "pkg://Documentation~/previews/ImposterUV.md",
            synonyms: new string[] { "billboard" },
            selectableFunctions: new()
            {
                { "ThreeFrames", "Three Frames" },
                { "OneFrame", "One Frame" }
            },
            functionSelectorLabel: "Sample Type",
            parameters: new ParameterUIDescriptor[] {
                new ParameterUIDescriptor(
                    name: "Pos",
                    displayName:"In Position",
                    tooltip: "The postiont in Object space"
                ),
                new ParameterUIDescriptor(
                    name: "UV",
                    tooltip: "The UV coordinates of the mesh"
                ),
                new ParameterUIDescriptor(
                    name: "Frames",
                    tooltip: "The amount of the imposter frames"
                ),
                new ParameterUIDescriptor(
                    name: "Offest",
                    tooltip: "The offset value from the origin"
                ),
                new ParameterUIDescriptor(
                    name: "Size",
                    tooltip: "The size of the imposter"
                ),
                new ParameterUIDescriptor(
                    name: "HemiSphere",
                    tooltip: "If it's true, calculate imposter grid and UVs base on hemisphere type."
                ),
                new ParameterUIDescriptor(
                    name: "OutPos",
                    displayName:"Out Position",
                    tooltip: "The output billboard position."
                ),
                new ParameterUIDescriptor(
                    name: "UV0",
                    tooltip: "The virtual UV for the base frame"
                ),
                new ParameterUIDescriptor(
                    name: "UV1",
                    tooltip: "The virtual UV for the second frame"
                ),
                new ParameterUIDescriptor(
                    name: "UV2",
                    tooltip: "The virtual UV for the third frame"
                ),
                new ParameterUIDescriptor(
                    name: "Grid",
                    tooltip: "The current UV grid"
                )
            }
        );
    }
}
