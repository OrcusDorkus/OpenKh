using OpenKh.Common;
using OpenKh.Engine.Maths;
using OpenKh.Kh2;
using OpenKh.Ps2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace OpenKh.Engine.Parsers.Kddf2
{
    public class Kkdf2MdlxParser
    {
        public class CI
        {
            public int[] Indices;
            public int TextureIndex, SegmentIndex;
            public bool IsOpaque;
        }

        public List<CI> MeshDescriptors { get; } = new List<CI>();

        private readonly List<ImmutableMesh> immultableMeshList;

        /// <summary>
        /// Build immutable parts from a submodel.
        /// </summary>
        /// <param name="submodel"></param>
        public Kkdf2MdlxParser(Mdlx.SubModel submodel)
        {
            immultableMeshList = new List<ImmutableMesh>();
            foreach (Mdlx.DmaChain dmaChain in submodel.DmaChains)
            {
                foreach (Mdlx.DmaVif dmaVif in dmaChain.DmaVifs)
                {
                    const int tops = 0x00;

                    var unpacker = new VifUnpacker(dmaVif.VifPacket)
                    {
                        Vif1_Tops = tops
                    };
                    unpacker.Run();

                    var mesh = VU1Simulation.Run(unpacker.Memory, tops, dmaVif.TextureIndex, dmaVif.Alaxi);
                    mesh.isOpaque = (dmaChain.RenderFlags & 1) == 0;
                    immultableMeshList.Add(mesh);
                }
            }
        }

        /// <summary>
        /// Build final model using immutable parts and given matrices.
        /// </summary>
        /// <returns></returns>
        public Kkdf2MdlxBuiltModel ProcessVerticesAndBuildModel(Matrix[] matrices)
        {
            var models = new SortedDictionary<Tuple<int, bool>, Model>();

            var exportedMesh = new ExportedMesh();

            {
                int vertexBaseIndex = 0;
                int uvBaseIndex = 0;
                VertexRef[] ringBuffer = new VertexRef[4];
                int ringIndex = 0;
                int[] triangleOrder = new int[] { 1, 3, 2 };
                foreach (ImmutableMesh mesh in immultableMeshList)
                {
                    for (int x = 0; x < mesh.indexAssignmentList.Length; x++)
                    {
                        var indexAssign = mesh.indexAssignmentList[x];

                        VertexRef vertexRef = new VertexRef(
                            vertexBaseIndex + indexAssign.indexToVertexAssignment,
                            uvBaseIndex + x
                        );
                        ringBuffer[ringIndex] = vertexRef;
                        ringIndex = (ringIndex + 1) & 3;
                        int flag = indexAssign.vertexFlag;
                        if (flag == 0x20 || flag == 0x00)
                        {
                            var triRef = new TriangleRef(mesh.textureIndex, mesh.isOpaque,
                                ringBuffer[(ringIndex - triangleOrder[0]) & 3],
                                ringBuffer[(ringIndex - triangleOrder[1]) & 3],
                                ringBuffer[(ringIndex - triangleOrder[2]) & 3]
                                );
                            exportedMesh.triangleRefList.Add(triRef);
                        }
                        if (flag == 0x30 || flag == 0x00)
                        {
                            var triRef = new TriangleRef(mesh.textureIndex, mesh.isOpaque,
                                ringBuffer[(ringIndex - triangleOrder[0]) & 3],
                                ringBuffer[(ringIndex - triangleOrder[2]) & 3],
                                ringBuffer[(ringIndex - triangleOrder[1]) & 3]
                                );
                            exportedMesh.triangleRefList.Add(triRef);
                        }
                    }

                    exportedMesh.positionList.AddRange(
                        mesh.vertexAssignmentsList.Select(
                            vertexAssigns =>
                            {
                                Vector3 finalPos = Vector3.Zero;
                                if (vertexAssigns.Length == 1)
                                {
                                    // single joint
                                    finalPos = TransformCoordinate(
                                    VCUt.V4To3(
                                        vertexAssigns[0].rawPos
                                    ),
                                    matrices[vertexAssigns[0].matrixIndex]
                                );
                                }
                                else
                                {
                                    // multiple joints, using rawPos.W as blend weights
                                    foreach (VertexAssignment vertexAssign in vertexAssigns)
                                    {
                                        finalPos += VCUt.V4To3(
                                            Transform(
                                                vertexAssign.rawPos,
                                                matrices[vertexAssign.matrixIndex]
                                            )
                                        );
                                    }
                                }
                                return finalPos;
                            }
                        )
                    );

                    exportedMesh.uvList.AddRange(
                        mesh.indexAssignmentList
                            .Select(indexAssign => indexAssign.uv)
                    );

                    vertexBaseIndex += mesh.vertexAssignmentsList.Length;
                    uvBaseIndex += mesh.indexAssignmentList.Length;
                }
            }

            {
                int triangleRefCount = exportedMesh.triangleRefList.Count;
                for (int triIndex = 0; triIndex < triangleRefCount; triIndex++)
                {
                    TriangleRef triRef = exportedMesh.triangleRefList[triIndex];
                    Tuple<int, bool> modelKey = new Tuple<int, bool>(triRef.textureIndex, triRef.isOpaque);
                    Model model;
                    if (models.TryGetValue(modelKey, out model) == false)
                    {
                        models[modelKey] = model = new Model();
                    }
                    for (int i = 0; i < triRef.list.Length; i++)
                    {
                        VertexRef vertRef = triRef.list[i];
                        Vector3 pos = exportedMesh.positionList[vertRef.vertexIndex];
                        Vector2 uv = exportedMesh.uvList[vertRef.uvIndex];
                        model.Vertices.Add(new CustomVertex.PositionColoredTextured(pos, -1, uv.X, uv.Y));
                    }
                }
            }

            return new Kkdf2MdlxBuiltModel
            {
                textureIndexBasedModelDict = models,
                parser = this,
            };
        }

        private static Vector3 TransformCoordinate(Vector3 coordinate, Matrix transformation)
        {
            Vector4 vector4 = new Vector4();
            vector4.X = (float)((double)transformation.M21 * (double)coordinate.Y + (double)transformation.M11 * (double)coordinate.X + (double)transformation.M31 * (double)coordinate.Z) + transformation.M41;
            vector4.Y = (float)((double)transformation.M22 * (double)coordinate.Y + (double)transformation.M12 * (double)coordinate.X + (double)transformation.M32 * (double)coordinate.Z) + transformation.M42;
            vector4.Z = (float)((double)transformation.M23 * (double)coordinate.Y + (double)transformation.M13 * (double)coordinate.X + (double)transformation.M33 * (double)coordinate.Z) + transformation.M43;
            float num = (float)(1.0 / ((double)transformation.M24 * (double)coordinate.Y + (double)transformation.M14 * (double)coordinate.X + (double)transformation.M34 * (double)coordinate.Z + (double)transformation.M44));
            vector4.W = num;
            Vector3 vector3 = new Vector3(vector4.X * num, vector4.Y * num, vector4.Z * num);
            return vector3;
        }

        public static Vector4 Transform(Vector4 vector, Matrix transformation)
        {
            return new Vector4()
            {
                X = (float)((double)transformation.M21 * vector.Y + transformation.M11 * vector.X + transformation.M31 * vector.Z + transformation.M41 * vector.W),
                Y = (float)((double)transformation.M22 * vector.Y + transformation.M12 * vector.X + transformation.M32 * vector.Z + transformation.M42 * vector.W),
                Z = (float)((double)transformation.M23 * vector.Y + transformation.M13 * vector.X + transformation.M33 * vector.Z + transformation.M43 * vector.W),
                W = (float)((double)transformation.M24 * vector.Y + transformation.M14 * vector.X + transformation.M34 * vector.Z + transformation.M44 * vector.W)
            };
        }
    }
}
