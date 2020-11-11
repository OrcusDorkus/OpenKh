using OpenKh.Engine.Maths;
using OpenKh.Kh2;
using OpenKh.Ps2;
using System;
using System.Collections.Generic;
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

        private class RingBuffer
        {
            public VertexRef[] ringBuffer = new VertexRef[4];
            public int ringIndex = 0;
        }

        private class VertexAssignment
        {
            public int matrixIndex;
            public float weight = 1f;
            public Vector4 rawPos;
        }

        private class VertexRef
        {
            public int vertexIndex, uvIndex;

            public VertexRef(int vertexIndex, int uvIndex)
            {
                this.vertexIndex = vertexIndex;
                this.uvIndex = uvIndex;
            }
        }

        private class TriangleRef
        {
            public TriangleRef(int textureIndex, bool isOpaque, VertexRef one, VertexRef two, VertexRef three)
            {
                this.textureIndex = textureIndex;
                this.isOpaque = isOpaque;
                this.list = new VertexRef[] { one, two, three };
            }

            public VertexRef[] list;
            public int textureIndex;
            public bool isOpaque;
        }

        private class ExportedMesh
        {
            public List<Vector3> positionList = new List<Vector3>();
            public List<Vector2> uvList = new List<Vector2>();
            public List<TriangleRef> triangleRefList = new List<TriangleRef>();
        }

        public List<CI> MeshDescriptors { get; } = new List<CI>();

        private readonly List<ImmutableMesh> immultableMeshList;

        /// <summary>
        /// Build immutable parts from a submodel.
        /// </summary>
        /// <param name="submodel"></param>
        public Kkdf2MdlxParser(Mdlx.SubModel submodel)
        {
            immultableMeshList = submodel.DmaChains
                .Select(x => new ImmutableMesh(x))
                .ToList();
        }

        /// <summary>
        /// Build final model using immutable parts and given matrices.
        /// </summary>
        /// <returns></returns>
        public Kkdf2MdlxBuiltModel ProcessVerticesAndBuildModel(Matrix[] matrices)
        {
            var models = new SortedDictionary<Tuple<int, bool>, Model>();

            var exportedMesh = ExportMeshNew(immultableMeshList, matrices);

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

            return new Kkdf2MdlxBuiltModel
            {
                textureIndexBasedModelDict = models,
                parser = this,
            };
        }

        private static ExportedMesh ExportMeshNew(List<ImmutableMesh> immultableMeshList, Matrix[] matrices)
        {
            var ringBuffer = new RingBuffer();
            int vertexBaseIndex = 0;
            int uvBaseIndex = 0;
            var exportedMesh = new ExportedMesh();
            foreach (var meshRoot in immultableMeshList)
            {
                for (int i = 0; i < meshRoot.VpuPackets.Count; i++)
                {
                    var mesh = meshRoot.VpuPackets[i];
                    var matrixIndexList = meshRoot.DmaChain.DmaVifs[i].Alaxi;
                    ProcessMeshNew(exportedMesh.triangleRefList, mesh, ringBuffer, vertexBaseIndex, uvBaseIndex,
                        meshRoot.TextureIndex, meshRoot.IsOpaque);

                    var positionList = exportedMesh.positionList;

                    var vertexIndex = 0;
                    var vertexAssignmentList = new VertexAssignment[mesh.Vertices.Length];
                    for (var indexToMatrixIndex = 0; indexToMatrixIndex < mesh.VertexRange.Length; indexToMatrixIndex++)
                    {
                        var verticesCount = mesh.VertexRange[indexToMatrixIndex];
                        for (var t = 0; t < verticesCount; t++)
                        {
                            var vertex = mesh.Vertices[vertexIndex];
                            vertexAssignmentList[vertexIndex++] = new VertexAssignment
                            {
                                matrixIndex = matrixIndexList[indexToMatrixIndex],
                                weight = vertex.W,
                                rawPos = new Vector4(vertex.X, vertex.Y, vertex.Z, vertex.W)
                            };
                        };
                    }

                    var vertexAssignmentsList = vertexAssignmentList
                        .Select(x => new VertexAssignment[] { x })
                        .ToArray();

                    // TODO:
                    //if (vpu.Type == 1 && vpu.VertexMixerCount > 0)
                    //{
                    //    si.Position = (vpu.VertexMixerOffset + tops) * 0x10;
                    //    var mixerCounts = Enumerable.Range(0, vpu.VertexMixerCount)
                    //        .Select(x => br.ReadInt32()).ToList();

                    //    var newVertexAssignList = new VertexAssignment[mixerCounts.Sum()][];
                    //    var inputVertexIndex = 0;

                    //    for (var i = 0; i < mixerCounts.Count; i++)
                    //    {
                    //        si.Position = (si.Position + 15) & (~15);
                    //        for (var j = 0; j < mixerCounts[i]; j++)
                    //        {
                    //            newVertexAssignList[inputVertexIndex++] = Enumerable
                    //                .Range(0, i + 1)
                    //                .Select(x => vertexAssignmentList[br.ReadInt32()])
                    //                .ToArray();
                    //        }
                    //    }

                    //    mesh.vertexAssignmentsList = newVertexAssignList;
                    //}

                    positionList.AddRange(
                        vertexAssignmentsList.Select(
                            vertexAssigns =>
                            {
                                Vector3 finalPos = Vector3.Zero;
                                if (vertexAssigns.Length == 1)
                                {
                                    // single joint
                                    finalPos = TransformCoordinate(
                                                    V4To3(
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
                                        finalPos += V4To3(
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
                        mesh.Indices.Select(x =>
                            new Vector2(x.U / 16 / 256.0f, x.V / 16 / 256.0f))
                    );

                    vertexBaseIndex += vertexAssignmentsList.Length;
                    uvBaseIndex += mesh.Indices.Length;
                }
            }

            return exportedMesh;
        }

        private static void ProcessMeshNew(
            List<TriangleRef> triangleRefList, VpuPacket packet,
            RingBuffer ringBuffer, int vertexBaseIndex, int uvBaseIndex,
            int textureIndex, bool isOpaque)
        {
            int[] triangleOrder = new int[] { 1, 3, 2 };
            for (var x = 0; x < packet.Indices.Length; x++)
            {
                var indexAssign = packet.Indices[x];
                VertexRef vertexRef = new VertexRef(
                    vertexBaseIndex + indexAssign.Index,
                    uvBaseIndex + x
                );

                ringBuffer.ringBuffer[ringBuffer.ringIndex] = vertexRef;
                ringBuffer.ringIndex = (ringBuffer.ringIndex + 1) & 3;
                var flag = indexAssign.Function;
                if (flag == VpuPacket.VertexFunction.DrawTriangle ||
                    flag == VpuPacket.VertexFunction.DrawTriangleDoubleSided)
                {
                    var triRef = new TriangleRef(textureIndex, isOpaque,
                        ringBuffer.ringBuffer[(ringBuffer.ringIndex - triangleOrder[0]) & 3],
                        ringBuffer.ringBuffer[(ringBuffer.ringIndex - triangleOrder[1]) & 3],
                        ringBuffer.ringBuffer[(ringBuffer.ringIndex - triangleOrder[2]) & 3]
                        );
                    triangleRefList.Add(triRef);
                }
                if (flag == VpuPacket.VertexFunction.DrawTriangleInverse ||
                    flag == VpuPacket.VertexFunction.DrawTriangleDoubleSided)
                {
                    var triRef = new TriangleRef(textureIndex, isOpaque,
                        ringBuffer.ringBuffer[(ringBuffer.ringIndex - triangleOrder[0]) & 3],
                        ringBuffer.ringBuffer[(ringBuffer.ringIndex - triangleOrder[2]) & 3],
                        ringBuffer.ringBuffer[(ringBuffer.ringIndex - triangleOrder[1]) & 3]
                        );
                    triangleRefList.Add(triRef);
                }
            }
        }

        private static Vector3 TransformCoordinate(Vector3 coordinate, Matrix transformation)
        {
            var vector4 = new Vector4
            {
                X = (float)((double)transformation.M21 * coordinate.Y + (double)transformation.M11 * coordinate.X + (double)transformation.M31 * coordinate.Z) + transformation.M41,
                Y = (float)((double)transformation.M22 * coordinate.Y + (double)transformation.M12 * coordinate.X + (double)transformation.M32 * coordinate.Z) + transformation.M42,
                Z = (float)((double)transformation.M23 * coordinate.Y + (double)transformation.M13 * coordinate.X + (double)transformation.M33 * coordinate.Z) + transformation.M43,
                W = (float)(1.0 / ((double)transformation.M24 * coordinate.Y + (double)transformation.M14 * coordinate.X + (double)transformation.M34 * coordinate.Z + transformation.M44))
            };

            return new Vector3(vector4.X * vector4.W, vector4.Y * vector4.W, vector4.Z * vector4.W);
        }

        public static Vector4 Transform(Vector4 vector, Matrix transformation) => new Vector4()
        {
            X = (float)((double)transformation.M21 * vector.Y + transformation.M11 * vector.X + transformation.M31 * vector.Z + transformation.M41 * vector.W),
            Y = (float)((double)transformation.M22 * vector.Y + transformation.M12 * vector.X + transformation.M32 * vector.Z + transformation.M42 * vector.W),
            Z = (float)((double)transformation.M23 * vector.Y + transformation.M13 * vector.X + transformation.M33 * vector.Z + transformation.M43 * vector.W),
            W = (float)((double)transformation.M24 * vector.Y + transformation.M14 * vector.X + transformation.M34 * vector.Z + transformation.M44 * vector.W)
        };

        private static Vector3 V4To3(Vector4 pos) => new Vector3(pos.X, pos.Y, pos.Z);
    }
}
