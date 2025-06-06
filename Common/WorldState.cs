using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using Lidgren.Network;
using UnityEngine;
using SFS.IO;
using SFS.Parts;
using SFS.World;
using SFS.WorldBase;
using SFS.Parsers.Json;
using Random = System.Random;
using static SFS.World.WorldSave;

namespace MultiplayerSFS.Common
{
    public static class IDExtensions
    {
        static readonly Random generator = new Random();
        public static int InsertNew<T>(this Dictionary<int, T> dict, T item)
        {
            int id; do
            {
                id = generator.Next();
            }
            while (dict.ContainsKey(id));
            dict.Add(id, item);
            return id;
        }

        public static int InsertNew(this HashSet<int> set)
        {
            int id; do
            {
                id = generator.Next();
            }
            while (set.Contains(id));
            set.Add(id);
            return id;
        }
    }

    public static class Difficulty
    {
        public enum DifficultyType : byte
        {
            Normal = 0,
            Hard = 1,
            Realistic = 2
        }
    }

    public class WorldState
    {
        public double initWorldTime;
        public Stopwatch worldTimer = Stopwatch.StartNew();
        public double WorldTime
        {
            get
            {
                if (worldTimer.ElapsedTicks > 1000 * Stopwatch.Frequency)
                {
                    // * Safety measure to prevent floating-point precision errors server-side.
                    initWorldTime += worldTimer.Elapsed.TotalSeconds;
                    worldTimer.Restart();
                }
                return initWorldTime + worldTimer.Elapsed.TotalSeconds;
            }
            set
            {
                initWorldTime = value;
                worldTimer.Restart();
            }
        }

        public Difficulty.DifficultyType difficulty;
        public Dictionary<int, RocketState> rockets;

        public WorldState()
        {
            initWorldTime = 1000000.0;
            difficulty = Difficulty.DifficultyType.Normal;
            rockets = new Dictionary<int, RocketState>();
        }

        public WorldState(string path)
        {
            FolderPath folder = new FolderPath(path);
            FolderPath persistent = folder.CloneAndExtend("Persistent");
            if (!folder.FolderExists())
                throw new Exception("Save folder cannot be found or does not exist.");
            if (!persistent.FolderExists())
                throw new Exception("'Persistent' folder cannot be found or does not exist.");

            string worldSettingsPath = folder.ExtendToFile("WorldSettings.txt");
            string worldStatePath = persistent.ExtendToFile("WorldState.txt");
            string rocketsPath = persistent.ExtendToFile("Rockets.txt");

            if (!File.Exists(worldSettingsPath))
                throw new Exception("'WorldSettings.txt' file cannot be found or could not be loaded.");
            if (!File.Exists(worldStatePath))
                throw new Exception("'WorldState.txt' file cannot be found or could not be loaded.");
            if (!File.Exists(rocketsPath))
                throw new Exception("'Rockets.txt' file cannot be found or could not be loaded.");

            var settings = JsonConvert.DeserializeObject<WorldSettings>(File.ReadAllText(worldSettingsPath));
            var state = JsonConvert.DeserializeObject<WorldSave.WorldState>(File.ReadAllText(worldStatePath));
            var rocketSaves = JsonConvert.DeserializeObject<List<RocketSave>>(File.ReadAllText(rocketsPath));

            if (settings == null || state == null || rocketSaves == null)
                throw new Exception("Failed to deserialize save files.");

            initWorldTime = state.worldTime;
            difficulty = (Difficulty.DifficultyType)(byte)settings.difficulty.difficulty;
            rockets = new Dictionary<int, RocketState>();
            foreach (RocketSave save in rocketSaves)
            {
                rockets.InsertNew(new RocketState(save));
            }
        }

        public void Save(string path = null)
        {
            path ??= "Sav";
            var folder = new SFS.IO.FolderPath(path);
            var persistent = folder.CloneAndExtend("Persistent");
            if (!folder.FolderExists()) folder.CreateFolder();
            if (!persistent.FolderExists()) persistent.CreateFolder();

            // 保存难度
            var settings = new {
                difficulty = new { difficulty = difficulty }
            };
            File.WriteAllText(folder.ExtendToFile("WorldSettings.txt"), 
                JsonConvert.SerializeObject(settings, Formatting.Indented));

            // 保存世界时间
            var state = new { worldTime = WorldTime };
            File.WriteAllText(persistent.ExtendToFile("WorldState.txt"), 
                JsonConvert.SerializeObject(state, Formatting.Indented));

            // 保存火箭
            var rocketSaves = rockets.Values.Select(r => new {
                rocketName = r.rocketName,
                location = r.location,
                rotation = r.rotation,
                angularVelocity = r.angularVelocity,
                throttleOn = r.throttleOn,
                throttlePercent = r.throttlePercent,
                RCS = r.RCS,
                parts = r.parts.Values.ToArray(),
                joints = r.joints.ToArray(),
                stages = r.stages.ToArray()
            }).ToList();
            File.WriteAllText(persistent.ExtendToFile("Rockets.txt"), 
                JsonConvert.SerializeObject(rocketSaves, Formatting.Indented));
        }
    }

    public class RocketState : INetData
    {
        public string rocketName;
        public LocationData location;
        public float rotation;
        public float angularVelocity;
        public bool throttleOn;
        public float throttlePercent;
        public bool RCS;

        public float input_Turn;
        public Vector2 input_Raw;
        public Vector2 input_Horizontal;
        public Vector2 input_Vertical;

        public Dictionary<int, PartState> parts;
        public List<JointState> joints;
        public List<StageState> stages;

        public RocketState() {}

        public RocketState(RocketSave save)
        {
            rocketName = save.rocketName;
            location = save.location;
            rotation = save.rotation;
            angularVelocity = save.angularVelocity;
            throttleOn = save.throttleOn;
            throttlePercent = save.throttlePercent;
            RCS = save.RCS;

            input_Turn = 0;
            input_Raw = Vector2.zero;
            input_Horizontal = Vector2.zero;
            input_Vertical = Vector2.zero;

            Dictionary<int, int> partIndexToID = new Dictionary<int, int>(save.parts.Length);
            parts = new Dictionary<int, PartState>(save.parts.Length);

            for (int i = 0; i < save.parts.Length; i++)
            {
                PartState part = new PartState(save.parts[i]);
                int id = parts.InsertNew(part);
                partIndexToID.Add(i, id);
            }

            joints = save.joints.Select(joint => new JointState(joint, partIndexToID)).ToList();
            stages = save.stages.Select(stage => new StageState(stage, partIndexToID)).ToList();
        }

        public void UpdateRocket(Packet_UpdateRocket packet)
        {
            input_Turn = packet.Input_Turn;
            input_Raw = packet.Input_Raw;
            input_Horizontal = packet.Input_Horizontal;
            input_Vertical = packet.Input_Vertical;
            rotation = packet.Rotation;
            angularVelocity = packet.AngularVelocity;
            throttlePercent = packet.ThrottlePercent;
            throttleOn = packet.ThrottleOn;
            RCS = packet.RCS;
            location = packet.Location;
        }

        /// <summary>
        /// Returns true if the part was found and removed, otherwise returns false.
        /// </summary>
        public bool RemovePart(int id)
        {
            joints.RemoveAll(j => j.id_A == id || j.id_B == id);
            foreach (StageState stage in stages)
            {
                stage.partIDs.RemoveAll(p => p == id);
            }
            return parts.Remove(id);
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(rocketName);
            msg.Write(location);
            msg.Write(rotation);
            msg.Write(angularVelocity);
            msg.Write(throttleOn);
            msg.Write(throttlePercent);
            msg.Write(RCS);
            msg.WriteCollection
            (
                parts,
                kvp =>
                {
                    msg.Write(kvp.Key);
                    msg.Write(kvp.Value);
                }
            );
            msg.WriteCollection(joints, msg.Write);
            msg.WriteCollection(stages, msg.Write);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            rocketName = msg.ReadString();
            location = msg.ReadLocation();
            rotation = msg.ReadFloat();
            angularVelocity = msg.ReadFloat();
            throttleOn = msg.ReadBoolean();
            throttlePercent = msg.ReadFloat();
            RCS = msg.ReadBoolean();

            parts = msg.ReadCollection
            (
                count => new Dictionary<int, PartState>(),
                () => new KeyValuePair<int, PartState>(msg.ReadInt32(), msg.Read<PartState>())
            );
            joints = msg.ReadCollection(count => new List<JointState>(count), () => msg.Read<JointState>());
            stages = msg.ReadCollection(count => new List<StageState>(count), () => msg.Read<StageState>());
        }
    }

    public class PartState : INetData
    {
        public PartSave part;

        public PartState() {}
        public PartState(PartSave save)
        {
            part = save;
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(part.name);
            msg.Write(part.position);
            msg.Write(part.orientation);
            msg.Write(part.temperature);
            msg.WriteCollection
            (
                part.NUMBER_VARIABLES,
                kvp =>
                {
                    msg.Write(kvp.Key);
                    msg.Write(kvp.Value);
                }
            );
            msg.WriteCollection
            (
                part.TOGGLE_VARIABLES,
                kvp =>
                {
                    msg.Write(kvp.Key);
                    msg.Write(kvp.Value);
                }
            );
            msg.WriteCollection
            (
                part.TEXT_VARIABLES,
                kvp =>
                {
                    msg.Write(kvp.Key);
                    msg.Write(kvp.Value);
                }
            );
            msg.Write(part.burns);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            part = new PartSave
            {
                name = msg.ReadString(),
                position = msg.ReadVector2(),
                orientation = msg.ReadOrientation(),
                temperature = msg.ReadFloat(),
                NUMBER_VARIABLES = msg.ReadCollection
                (
                    count => new Dictionary<string, double>(count),
                    () => new KeyValuePair<string, double>(msg.ReadString(), msg.ReadDouble())
                ),
                TOGGLE_VARIABLES = msg.ReadCollection
                (
                    count => new Dictionary<string, bool>(count),
                    () => new KeyValuePair<string, bool>(msg.ReadString(), msg.ReadBoolean())
                ),
                TEXT_VARIABLES = msg.ReadCollection
                (
                    count => new Dictionary<string, string>(count),
                    () => new KeyValuePair<string, string>(msg.ReadString(), msg.ReadString())
                ),
                burns = msg.ReadBurnSave()
            };
        }
    }

    public class JointState : INetData
    {
        public int id_A;
        public int id_B;

        public JointState() {}
        public JointState(int id_A, int id_B)
        {
            this.id_A = id_A;
            this.id_B = id_B;
        }
        public JointState(JointSave save, Dictionary<int, int> partIndexToID)
        {
            id_A = partIndexToID[save.partIndex_A];
            id_B = partIndexToID[save.partIndex_B];
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(id_A);
            msg.Write(id_B);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            id_A = msg.ReadInt32();
            id_B = msg.ReadInt32();
        }
    }

    public class StageState : INetData
    {
        public int stageID;
        public List<int> partIDs;

        public StageState() {}

        public StageState(int stageID, List<int> partIDs)
        {
            this.stageID = stageID;
            this.partIDs = partIDs;
        }

        public StageState(StageSave save, Dictionary<int, int> partIndexToID)
        {
            stageID = save.stageId;
            IEnumerable<int> unfiltered = save.partIndexes.Select(idx => partIndexToID[idx]);
            partIDs = unfiltered.ToList();
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(stageID);
            msg.WriteCollection(partIDs, msg.Write);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            stageID = msg.ReadInt32();
            partIDs = msg.ReadCollection(count => new List<int>(), msg.ReadInt32);
        }
    }
}