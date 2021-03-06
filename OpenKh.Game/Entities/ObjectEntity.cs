using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKh.Engine.MonoGame;
using OpenKh.Game.Debugging;
using OpenKh.Game.Infrastructure;
using OpenKh.Kh2;
using OpenKh.Kh2.Ard;
using OpenKh.Kh2.Extensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenKh.Game.Entities
{
    public class ObjectEntity : IEntity
    {
        public ObjectEntity(Kernel kernel, int objectId)
        {
            Kernel = kernel;
            ObjectId = objectId;
            Scaling = new Vector3(1, 1, 1);
        }

        public Kernel Kernel { get; }

        public int ObjectId { get; }

        public string ObjectName => Kernel.ObjEntries
            .FirstOrDefault(x => x.ObjectId == ObjectId)?.ModelName;

        public MeshGroup Mesh { get; private set; }

        public Vector3 Position { get; set; }

        public Vector3 Rotation { get; set; }

        public Vector3 Scaling { get; set; }

        public void LoadMesh(GraphicsDevice graphics)
        {
            var objEntry = Kernel.ObjEntries.FirstOrDefault(x => x.ObjectId == ObjectId);
            if (objEntry == null)
            {
                Log.Warn($"Object ID {ObjectId} not found.");
                return;
            }

            var fileName = $"obj/{objEntry.ModelName}.mdlx";

            using var stream = Kernel.DataContent.FileOpen(fileName);
            var entries = Bar.Read(stream);
            var model = entries.ForEntry(x => x.Type == Bar.EntryType.Model, Mdlx.Read);
            var texture = entries.ForEntry("tim_", Bar.EntryType.ModelTexture, ModelTexture.Read);
            Mesh = MeshLoader.FromKH2(graphics, model, texture);
        }

        public static MeshGroup FromFbx(GraphicsDevice graphics, string filePath)
        {
            const float Scale = 96.0f;
            var assimp = new Assimp.AssimpContext();
            var scene = assimp.ImportFile(filePath, Assimp.PostProcessSteps.PreTransformVertices);
            var baseFilePath = Path.GetDirectoryName(filePath);

            return new MeshGroup()
            {
                MeshDescriptors = scene.Meshes
                    .Select(x =>
                    {
                        var vertices = new VertexPositionColorTexture[x.Vertices.Count];
                        for (var i = 0; i < vertices.Length; i++)
                        {
                            vertices[i].Position.X = x.Vertices[i].X * Scale;
                            vertices[i].Position.Y = x.Vertices[i].Y * Scale;
                            vertices[i].Position.Z = x.Vertices[i].Z * Scale;
                            vertices[i].TextureCoordinate.X = x.TextureCoordinateChannels[0][i].X;
                            vertices[i].TextureCoordinate.Y = 1.0f - x.TextureCoordinateChannels[0][i].Y;
                            vertices[i].Color = Color.White;
                        }

                        return new MeshDesc
                        {
                            Vertices = vertices,
                            Indices = x.Faces.SelectMany(f => f.Indices).ToArray(),
                            IsOpaque = true,
                            TextureIndex = x.MaterialIndex
                        };
                    }).ToList(),
                Textures = scene.Materials.Select(x =>
                {
                    var path = Path.Join(baseFilePath, $"{x.Name}.png");
                    return new PngKingdomTexture(path, graphics);
                }).ToArray(),
            };
        }

        public static ObjectEntity FromSpawnPoint(Kernel kernel, SpawnPoint.Entity spawnPoint) =>
            new ObjectEntity(kernel, spawnPoint.ObjectId)
            {
                Position = new Vector3(spawnPoint.PositionX, -spawnPoint.PositionY, -spawnPoint.PositionZ),
                Rotation = new Vector3(spawnPoint.RotationX, spawnPoint.RotationY, spawnPoint.RotationZ),
            };
    }
}
