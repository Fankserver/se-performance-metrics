using LitJson;
using NLog;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Torch.API;
using Torch.Managers;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;

namespace performance_metrics
{
    class PerformanceMetricsManager : Manager
    {
        private WebServer ws;

        public PerformanceMetricsManager(ITorchBase torchInstance) : base(torchInstance)
        {
        }

        /// <inheritdoc cref="Manager.Attach"/>
        public override void Attach()
        {
            base.Attach();

            ws = new WebServer(SendHttpResponseResponse, "http://*:8080/");
            ws.Run();

            LogManager.GetCurrentClassLogger().Info("Attached");
        }

        /// <inheritdoc cref="Manager.Detach"/>
        public override void Detach()
        {
            base.Detach();

            ws.Stop();

            LogManager.GetCurrentClassLogger().Info("Detached");
        }

        public string SendHttpResponseResponse(HttpListenerRequest request)
        {
            //LogManager.GetCurrentClassLogger().Debug($"Process request: {request.Url.AbsolutePath}");

            StringBuilder sb = new StringBuilder();
            JsonWriter writer = new JsonWriter(sb);

            switch (request.Url.AbsolutePath)
            {
                case "/vrageremote/v1/server":
                    int usedPCU = 0;
                    if (MySession.Static != null && MySession.Static.GlobalBlockLimits != null)
                    {
                        usedPCU = MySession.Static.GlobalBlockLimits.PCUBuilt;
                    }
                    writer.WriteObjectStart();
                    writer.WritePropertyName("data");
                    writer.WriteObjectStart();
                    writer.WritePropertyName("Version");
                    writer.Write(MyFinalBuildConstants.APP_VERSION_STRING_DOTS.ToString());
                    writer.WritePropertyName("ServerName");
                    writer.Write(MySandboxGame.ConfigDedicated.ServerName);
                    writer.WritePropertyName("WorldName");
                    writer.Write(MySandboxGame.ConfigDedicated.WorldName);
                    writer.WritePropertyName("IsReady");
                    writer.Write(MySession.Static != null && MySession.Static.Ready);
                    writer.WritePropertyName("SimSpeed");
                    writer.Write(Sync.ServerSimulationRatio);
                    writer.WritePropertyName("SimulationCpuLoad");
                    writer.Write((float)(int)Sync.ServerCPULoad);
                    writer.WritePropertyName("TotalTime");
                    writer.Write(MySandboxGame.TotalTimeInMilliseconds / 1000);
                    writer.WritePropertyName("Players");
                    writer.Write((Sync.Clients != null) ? (Sync.Clients.Count - 1) : 0);
                    writer.WritePropertyName("UsedPCU");
                    writer.Write(usedPCU);
                    writer.WriteObjectEnd();
                    writer.WriteObjectEnd();
                    break;
                case "/vrageremote/v1/session/grids":
                    writer.WriteObjectStart();
                    writer.WritePropertyName("data");
                    writer.WriteObjectStart();
                    writer.WritePropertyName("Grids");
                    writer.WriteArrayStart();
                    if (MySession.Static != null && MySession.Static.Ready)
                    {
                        ICollection<MyPlayer> onlinePlayers = MySession.Static.Players.GetOnlinePlayers();
                        MyConcurrentHashSet<MyEntity> entities = MyEntities.GetEntities();
                        foreach (MyEntity item in entities)
                        {
                            MyCubeGrid myCubeGrid = item as MyCubeGrid;
                            if (myCubeGrid != null && !myCubeGrid.Closed && myCubeGrid.Physics != null)
                            {
                                bool value = myCubeGrid.GridSystems.ResourceDistributor.ResourceStateByType(MyResourceDistributorComponent.ElectricityId, withRecompute: false) != MyResourceStateEnum.NoPower;
                                bool flag = myCubeGrid.GridSizeEnum == MyCubeSize.Large;
                                long entityId = myCubeGrid.EntityId;
                                string displayName = myCubeGrid.DisplayName;
                                int blocksCount = myCubeGrid.BlocksCount;
                                float mass = myCubeGrid.Physics.Mass;
                                Vector3D position = myCubeGrid.PositionComp.GetPosition();
                                float value2 = myCubeGrid.Physics.LinearVelocity.Length();
                                float playerDistance = MySession.GetPlayerDistance(myCubeGrid, onlinePlayers);
                                long value3 = 0L;
                                string value4 = string.Empty;
                                if (myCubeGrid.BigOwners.Count > 0)
                                {
                                    value3 = myCubeGrid.BigOwners[0];
                                    MyIdentity myIdentity = MySession.Static.Players.TryGetIdentity(myCubeGrid.BigOwners[0]);
                                    if (myIdentity != null)
                                    {
                                        value4 = myIdentity.DisplayName;
                                    }
                                }
                                int blocksPCU = myCubeGrid.BlocksPCU;
                                writer.WriteObjectStart();
                                writer.WritePropertyName("DisplayName");
                                writer.Write(displayName);
                                writer.WritePropertyName("EntityId");
                                writer.Write(entityId);
                                writer.WritePropertyName("GridSize");
                                writer.Write(flag ? "Large" : "Small");
                                writer.WritePropertyName("BlocksCount");
                                writer.Write(blocksCount);
                                writer.WritePropertyName("Mass");
                                writer.Write(mass);
                                writer.WritePropertyName("Position");
                                writer.WriteObjectStart();
                                writer.WritePropertyName("X");
                                writer.Write(position.X);
                                writer.WritePropertyName("Y");
                                writer.Write(position.Y);
                                writer.WritePropertyName("Z");
                                writer.Write(position.Z);
                                writer.WriteObjectEnd();
                                writer.WritePropertyName("LinearSpeed");
                                writer.Write(value2);
                                writer.WritePropertyName("DistanceToPlayer");
                                writer.Write(playerDistance);
                                writer.WritePropertyName("OwnerSteamId");
                                writer.Write(value3);
                                writer.WritePropertyName("OwnerDisplayName");
                                writer.Write(value4);
                                writer.WritePropertyName("IsPowered");
                                writer.Write(value);
                                writer.WritePropertyName("PCU");
                                writer.Write(blocksPCU);
                                writer.WriteObjectEnd();
                            }
                        }
                    }
                    writer.WriteArrayEnd();
                    writer.WriteObjectEnd();
                    writer.WriteObjectEnd();
                    break;
            }

            return sb.ToString();
        }
    }
}
