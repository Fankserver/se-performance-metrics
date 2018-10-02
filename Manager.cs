using LitJson;
using NLog;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Torch.API;
using Torch.Managers;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
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

            Torch.GameStateChanged += Torch_GameStateChanged;

            LogManager.GetCurrentClassLogger().Info("Attached");
        }

        private void Torch_GameStateChanged(MySandboxGame game, TorchGameState newState)
        {
            if (MySandboxGame.ConfigDedicated.RemoteApiEnabled)
            {
                LogManager.GetCurrentClassLogger().Error($"Remote API is enabled, can not start metric system");
                return;
            }

            if (newState == TorchGameState.Creating)
            {
                LogManager.GetCurrentClassLogger().Info($"WebServer started on port {MySandboxGame.ConfigDedicated.RemoteApiPort}");
                ws = new WebServer(SendHttpResponseResponse, $"http://*:{MySandboxGame.ConfigDedicated.RemoteApiPort}/");
                ws.Run();
            }
            else if (newState == TorchGameState.Unloaded)
            {
                ws.Stop();
                ws = null;
            }
        }

        /// <inheritdoc cref="Manager.Detach"/>
        public override void Detach()
        {
            base.Detach();

            Torch.GameStateChanged -= Torch_GameStateChanged;
            if (ws != null)
            {
                ws.Stop();
                ws = null;
            }

            LogManager.GetCurrentClassLogger().Info("Detached");
        }

        public string SendHttpResponseResponse(HttpListenerRequest request)
        {
            StringBuilder sb = new StringBuilder();
            JsonWriter writer = new JsonWriter(sb);

            switch (request.Url.AbsolutePath)
            {
                case "/metrics/v1/server":
                    int usedPCU = 0;
                    int maxPlayers = 0;
                    int maxFactionCount = 0;
                    int maxFloatingObjects = 0;
                    int maxGridSize = 0;
                    int maxBlocksPerPlayer = 0;
                    string blockLimit = "";
                    int totalPCU = 0;
                    if (MySession.Static != null) {
                        if (MySession.Static.GlobalBlockLimits != null)
                        {
                            usedPCU = MySession.Static.GlobalBlockLimits.PCUBuilt;
                        }
                        maxPlayers = MySession.Static.MaxPlayers;
                        maxFactionCount = MySession.Static.MaxFactionsCount;
                        maxFloatingObjects = MySession.Static.MaxFloatingObjects;
                        maxGridSize = MySession.Static.MaxGridSize;
                        maxBlocksPerPlayer = MySession.Static.MaxBlocksPerPlayer;
                        totalPCU = MySession.Static.TotalPCU;
                        switch (MySession.Static.BlockLimitsEnabled)
                        {
                            case MyBlockLimitsEnabledEnum.GLOBALLY:
                                blockLimit = "globally";
                                break;
                            case MyBlockLimitsEnabledEnum.NONE:
                                blockLimit = "none";
                                break;
                            case MyBlockLimitsEnabledEnum.PER_FACTION:
                                blockLimit = "faction";
                                break;
                            case MyBlockLimitsEnabledEnum.PER_PLAYER:
                                blockLimit = "player";
                                break;
                        }
                    }
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
                    writer.WritePropertyName("MaxPlayers");
                    writer.Write(maxPlayers);
                    writer.WritePropertyName("MaxFactionsCount");
                    writer.Write(maxFactionCount);
                    writer.WritePropertyName("MaxFloatingObjects");
                    writer.Write(maxFloatingObjects);
                    writer.WritePropertyName("MaxGridSize");
                    writer.Write(maxGridSize);
                    writer.WritePropertyName("MaxBlocksPerPlayer");
                    writer.Write(maxBlocksPerPlayer);
                    writer.WritePropertyName("BlockLimitsEnabled");
                    writer.Write(blockLimit);
                    writer.WritePropertyName("TotalPCU");
                    writer.Write(totalPCU);
                    writer.WriteObjectEnd();
                    break;
                case "/metrics/v1/session/grids":
                    writer.WriteArrayStart();
                    if (MySession.Static != null && MySession.Static.Ready)
                    {
                        ICollection<MyPlayer> onlinePlayers = MySession.Static.Players.GetOnlinePlayers();
                        MyConcurrentHashSet<MyEntity> entities = MyEntities.GetEntities();

                        Type type = typeof(MyEntities);
                        FieldInfo info = type.GetField("m_entitiesForUpdateOnce", BindingFlags.NonPublic | BindingFlags.Static);
                        object value = info.GetValue(null);
                        CachingList<MyEntity> m_entitiesForUpdateOnce = value as CachingList<MyEntity>;
                        List<long> x_entitiesForUpdateOnce = new List<long>();

                        type = typeof(MyEntities);
                        info = type.GetField("m_entitiesForUpdate", BindingFlags.NonPublic | BindingFlags.Static);
                        value = info.GetValue(null);
                        MyDistributedUpdater<ConcurrentCachingList<MyEntity>, MyEntity> m_entitiesForUpdate = value as MyDistributedUpdater<ConcurrentCachingList<MyEntity>, MyEntity>;
                        List<long> x_entitiesForUpdate = new List<long>();

                        type = typeof(MyEntities);
                        info = type.GetField("m_entitiesForUpdate10", BindingFlags.NonPublic | BindingFlags.Static);
                        value = info.GetValue(null);
                        MyDistributedUpdater<CachingList<MyEntity>, MyEntity> m_entitiesForUpdate10 = value as MyDistributedUpdater<CachingList<MyEntity>, MyEntity>;
                        List<long> x_entitiesForUpdate10 = new List<long>();

                        type = typeof(MyEntities);
                        info = type.GetField("m_entitiesForUpdate100", BindingFlags.NonPublic | BindingFlags.Static);
                        value = info.GetValue(null);
                        MyDistributedUpdater<CachingList<MyEntity>, MyEntity> m_entitiesForUpdate100 = value as MyDistributedUpdater<CachingList<MyEntity>, MyEntity>;
                        List<long> x_entitiesForUpdate100 = new List<long>();

                        type = typeof(MyEntities);
                        info = type.GetField("m_entitiesForSimulate", BindingFlags.NonPublic | BindingFlags.Static);
                        value = info.GetValue(null);
                        MyDistributedUpdater<CachingList<MyEntity>, MyEntity> m_entitiesForSimulate = value as MyDistributedUpdater<CachingList<MyEntity>, MyEntity>;
                        List<long> x_entitiesForSimulate = new List<long>();

                        Torch.InvokeBlocking(() =>
                        {
                            x_entitiesForUpdateOnce = m_entitiesForUpdateOnce.Select((x) => x.EntityId).ToList();
                            x_entitiesForUpdate = m_entitiesForUpdate.List.Select((x) => x.EntityId).ToList();
                            x_entitiesForUpdate10 = m_entitiesForUpdate10.List.Select((x) => x.EntityId).ToList();
                            x_entitiesForUpdate100 = m_entitiesForUpdate100.List.Select((x) => x.EntityId).ToList();
                            x_entitiesForSimulate = m_entitiesForSimulate.List.Select((x) => x.EntityId).ToList();
                        });

                        bool IsConcealed(MyCubeGrid grid)
                        {
                            int NeedsUpdateMatches = 0;
                            int RegistedMatches = 0;

                            if ((grid.NeedsUpdate & MyEntityUpdateEnum.BEFORE_NEXT_FRAME) > MyEntityUpdateEnum.NONE) {
                                NeedsUpdateMatches++;
                                if (x_entitiesForUpdateOnce.Any((x) => x == grid.EntityId))
                                {
                                    RegistedMatches++;
                                }
                            }
                            if ((grid.NeedsUpdate & MyEntityUpdateEnum.EACH_FRAME) > MyEntityUpdateEnum.NONE)
                            {
                                NeedsUpdateMatches++;
                                if (x_entitiesForUpdate.Any((x) => x == grid.EntityId))
                                {
                                    RegistedMatches++;
                                }
                            }
                            if ((grid.NeedsUpdate & MyEntityUpdateEnum.EACH_10TH_FRAME) > MyEntityUpdateEnum.NONE)
                            {
                                NeedsUpdateMatches++;
                                if (x_entitiesForUpdate10.Any((x) => x == grid.EntityId))
                                {
                                    RegistedMatches++;
                                }
                            }
                            if ((grid.NeedsUpdate & MyEntityUpdateEnum.EACH_100TH_FRAME) > MyEntityUpdateEnum.NONE)
                            {
                                NeedsUpdateMatches++;
                                if (x_entitiesForUpdate100.Any((x) => x == grid.EntityId))
                                {
                                    RegistedMatches++;
                                }
                            }
                            if ((grid.NeedsUpdate & MyEntityUpdateEnum.SIMULATE) > MyEntityUpdateEnum.NONE)
                            {
                                NeedsUpdateMatches++;
                                if (x_entitiesForSimulate.Any((x) => x == grid.EntityId))
                                {
                                    RegistedMatches++;
                                }
                            }

                            return NeedsUpdateMatches > 0 && RegistedMatches == 0;
                        }

                        foreach (MyEntity item in entities)
                        {
                            MyCubeGrid myCubeGrid = item as MyCubeGrid;
                            if (myCubeGrid != null && !myCubeGrid.Closed && myCubeGrid.Physics != null)
                            {
                                long steamId = 0L;
                                string displayName = string.Empty;
                                string factionTag = string.Empty;
                                string factionName = string.Empty;
                                if (myCubeGrid.BigOwners.Count > 0)
                                {
                                    steamId = myCubeGrid.BigOwners[0];

                                    MyIdentity myIdentity = MySession.Static.Players.TryGetIdentity(steamId);
                                    if (myIdentity != null)
                                    {
                                        displayName = myIdentity.DisplayName;
                                    }

                                    IMyFaction myFaction = MySession.Static.Factions.TryGetPlayerFaction(steamId);
                                    if (myFaction != null)
                                    {
                                        factionTag = myFaction.Tag;
                                        factionName = myFaction.Name;
                                    }
                                }

                                writer.WriteObjectStart();
                                writer.WritePropertyName("DisplayName");
                                writer.Write(myCubeGrid.DisplayName);
                                writer.WritePropertyName("EntityId");
                                writer.Write(myCubeGrid.EntityId);
                                writer.WritePropertyName("GridSize");
                                writer.Write(myCubeGrid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small");
                                writer.WritePropertyName("BlocksCount");
                                writer.Write(myCubeGrid.BlocksCount);
                                writer.WritePropertyName("Mass");
                                writer.Write(myCubeGrid.Physics.Mass);
                                writer.WritePropertyName("Position");
                                Vector3D position = myCubeGrid.PositionComp.GetPosition();
                                writer.WriteObjectStart();
                                writer.WritePropertyName("X");
                                writer.Write(position.X);
                                writer.WritePropertyName("Y");
                                writer.Write(position.Y);
                                writer.WritePropertyName("Z");
                                writer.Write(position.Z);
                                writer.WriteObjectEnd();
                                writer.WritePropertyName("LinearSpeed");
                                writer.Write(myCubeGrid.Physics.LinearVelocity.Length());
                                writer.WritePropertyName("DistanceToPlayer");
                                writer.Write(MySession.GetPlayerDistance(myCubeGrid, onlinePlayers));
                                writer.WritePropertyName("OwnerSteamId");
                                writer.Write(steamId);
                                writer.WritePropertyName("OwnerDisplayName");
                                writer.Write(displayName);
                                writer.WritePropertyName("OwnerFactionTag");
                                writer.Write(factionTag);
                                writer.WritePropertyName("OwnerFactionName");
                                writer.Write(factionName);
                                writer.WritePropertyName("IsPowered");
                                writer.Write(myCubeGrid.GridSystems.ResourceDistributor.ResourceStateByType(MyResourceDistributorComponent.ElectricityId, withRecompute: false) != MyResourceStateEnum.NoPower);
                                writer.WritePropertyName("PCU");
                                writer.Write(myCubeGrid.BlocksPCU);
                                writer.WritePropertyName("IsConcealed");
                                writer.Write(IsConcealed(myCubeGrid));
                                writer.WritePropertyName("DampenersEnabled");
                                writer.Write(myCubeGrid.DampenersEnabled);
                                writer.WritePropertyName("IsStatic");
                                writer.Write(myCubeGrid.Physics.IsStatic);
                                writer.WriteObjectEnd();
                            }
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
            }

            return sb.ToString();
        }
    }
}
