﻿using LitJson;
using NLog;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Conveyors;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using SpaceEngineers.Game.World;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Session;
using Torch.Managers;
using Torch.Managers.PatchManager;
using Torch.Session;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.Stats;
using VRageMath;

namespace performance_metrics
{
    class Event
    {
        public string Type;
        public string Text;
        public string[] Tags;
        public DateTime Occurred;
    }

    class LoadEvent
    {
        public float ServerThreadLoadSmooth;
        public float ServerThreadLoad;
        public float ServerCPULoadSmooth;
        public float ServerCPULoad;
        public float ServerSimulationRatio;
        public DateTime Occurred;
    }

    class PlayerEvent
    {
        public string Type;
        public ulong SteamId;
        public DateTime Occurred;
    }

    class PerformanceMetricsManager : Manager
    {
        private WebServer ws;
        private TorchSessionManager _sessionManager;
        private Persistent<Config> _config;
        private System.Timers.Timer loadTimer;
        [Dependency] private readonly PatchManager _patchManager;
        private PatchContext _ctx;
        private static MyConcurrentDeque<Event> events = new MyConcurrentDeque<Event>();
        private static MyConcurrentDeque<LoadEvent> loadEvents = new MyConcurrentDeque<LoadEvent>();
        private static MyConcurrentDeque<PlayerEvent> playerEvents = new MyConcurrentDeque<PlayerEvent>();
        private static Stopwatch saveStopwatch = new Stopwatch();
        private static long saveDuration = 0;

        private static readonly MethodInfo _asyncSavingStart =
            typeof(MyAsyncSaving).GetMethod(nameof(MyAsyncSaving.Start), BindingFlags.Static | BindingFlags.Public) ??
            throw new Exception("Failed to find patch method");

        private static readonly MethodInfo _serverBanClient =
            typeof(MyDedicatedServerBase).GetMethod(nameof(MyDedicatedServerBase.BanClient), BindingFlags.Instance | BindingFlags.Public) ??
            throw new Exception("Failed to find patch method");

        private static readonly MethodInfo _spaceRespawnComponentCreateNewIdentity =
            typeof(MySpaceRespawnComponent).GetMethod(nameof(MySpaceRespawnComponent.CreateNewIdentity), BindingFlags.Instance | BindingFlags.Public) ??
            throw new Exception("Failed to find patch method");

        public PerformanceMetricsManager(ITorchBase torchInstance, Persistent<Config> config) : base(torchInstance)
        {
            _config = config;
            GC.RegisterForFullGCNotification(99, 99);
            Thread thWaitForFullGC = new Thread(new ThreadStart(WaitForFullGCProc));
            thWaitForFullGC.Start();

            var pluginManager = Torch.Managers.GetManager<PluginManager>();
            pluginManager.PluginsLoaded += PluginsLoaded;
        }

        /// <inheritdoc cref="Manager.Attach"/>
        public override void Attach()
        {
            base.Attach();

            if (_ctx == null)
                _ctx = _patchManager.AcquireContext();

            Torch.GameStateChanged += Torch_GameStateChanged;
            _sessionManager = Torch.Managers.GetManager<TorchSessionManager>();
            if (_sessionManager != null)
                _sessionManager.SessionStateChanged += SessionChanged;
            else
                LogManager.GetCurrentClassLogger().Warn("No session manager. Player metrics won't work");

            loadTimer = new System.Timers.Timer(500);
            loadTimer.Elapsed += LoadTimerElapsed;
            loadTimer.AutoReset = true;
            loadTimer.Start();

            var perfMetricManager = typeof(PerformanceMetricsManager);
            _ctx.GetPattern(_asyncSavingStart).Prefixes.Add(perfMetricManager.GetMethod(nameof(PrefixAsyncSavingStart), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            _ctx.GetPattern(_asyncSavingStart).Suffixes.Add(perfMetricManager.GetMethod(nameof(SuffixAsyncSavingStart), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            _ctx.GetPattern(_serverBanClient).Suffixes.Add(perfMetricManager.GetMethod(nameof(SuffixBanClient), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            _ctx.GetPattern(_spaceRespawnComponentCreateNewIdentity).Suffixes.Add(perfMetricManager.GetMethod(nameof(SuffixCreateNewIdentity), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            _patchManager.Commit();

            LogManager.GetCurrentClassLogger().Info("Attached");
        }

        /// <inheritdoc cref="Manager.Detach"/>
        public override void Detach()
        {
            base.Detach();

            loadTimer.Stop();
            loadTimer.Dispose();

            Torch.GameStateChanged -= Torch_GameStateChanged;
            if (_sessionManager != null)
                _sessionManager.SessionStateChanged -= SessionChanged;
            _sessionManager = null;
            if (ws != null)
            {
                ws.Stop();
                ws = null;
            }

            _patchManager.FreeContext(_ctx);

            LogManager.GetCurrentClassLogger().Info("Detached");
        }

        public string SendHttpResponseResponse(HttpListenerRequest request)
        {
            StringBuilder sb = new StringBuilder();
            JsonWriter writer = new JsonWriter(sb);

            switch (request.Url.AbsolutePath)
            {
                //case "/gc/collect/0":
                //    GC.Collect(0);
                //    break;
                //case "/gc/collect/1":
                //    GC.Collect(1);
                //    break;
                //case "/gc/collect/2":
                //    GC.Collect(2);
                //    break;
                case "/metrics/v1/server":
                    int usedPCU = 0;
                    int maxPlayers = 0;
                    int maxFactionCount = 0;
                    int maxFloatingObjects = 0;
                    int maxGridSize = 0;
                    int maxBlocksPerPlayer = 0;
                    string blockLimit = "";
                    int totalPCU = 0;
                    int modCount = 0;
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
                        modCount = MySession.Static.Mods.Count;
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
                    writer.WritePropertyName("ModCount");
                    writer.Write(modCount);
                    writer.WritePropertyName("SaveDuration");
                    writer.Write(saveDuration);
                    writer.WriteObjectEnd();
                    break;
                case "/metrics/v1/load":
                    writer.WriteArrayStart();
                    LoadEvent loadEv;
                    while (!loadEvents.Empty)
                    {
                        if (loadEvents.TryDequeueBack(out loadEv))
                        {
                            writer.WriteObjectStart();
                            writer.WritePropertyName("ServerCPULoad");
                            writer.Write(loadEv.ServerCPULoad);
                            writer.WritePropertyName("ServerCPULoadSmooth");
                            writer.Write(loadEv.ServerCPULoadSmooth);
                            writer.WritePropertyName("ServerSimulationRatio");
                            writer.Write(loadEv.ServerSimulationRatio);
                            writer.WritePropertyName("ServerThreadLoad");
                            writer.Write(loadEv.ServerThreadLoad);
                            writer.WritePropertyName("ServerThreadLoadSmooth");
                            writer.Write(loadEv.ServerThreadLoadSmooth);
                            writer.WritePropertyName("MillisecondsInThePast");
                            writer.Write((DateTime.Now - loadEv.Occurred).TotalMilliseconds);
                            writer.WriteObjectEnd();
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
                case "/metrics/v1/process":
                    System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    writer.WriteObjectStart();
                    writer.WritePropertyName("PrivateMemorySize64");
                    writer.Write(currentProcess.PrivateMemorySize64);
                    writer.WritePropertyName("VirtualMemorySize64");
                    writer.Write(currentProcess.VirtualMemorySize64);
                    writer.WritePropertyName("WorkingSet64");
                    writer.Write(currentProcess.WorkingSet64);
                    writer.WritePropertyName("NonpagedSystemMemorySize64");
                    writer.Write(currentProcess.NonpagedSystemMemorySize64);
                    writer.WritePropertyName("PagedMemorySize64");
                    writer.Write(currentProcess.PagedMemorySize64);
                    writer.WritePropertyName("PagedSystemMemorySize64");
                    writer.Write(currentProcess.PagedSystemMemorySize64);
                    writer.WritePropertyName("PeakPagedMemorySize64");
                    writer.Write(currentProcess.PeakPagedMemorySize64);
                    writer.WritePropertyName("PeakVirtualMemorySize64");
                    writer.Write(currentProcess.PeakVirtualMemorySize64);
                    writer.WritePropertyName("PeakWorkingSet64");
                    writer.Write(currentProcess.PeakWorkingSet64);
                    writer.WritePropertyName("GCLatencyMode");
                    writer.Write((int)System.Runtime.GCSettings.LatencyMode);
                    writer.WritePropertyName("GCIsServerGC");
                    writer.Write(System.Runtime.GCSettings.IsServerGC);
                    writer.WritePropertyName("GCTotalMemory");
                    writer.Write(GC.GetTotalMemory(false));
                    writer.WritePropertyName("GCMaxGeneration");
                    writer.Write(GC.MaxGeneration);
                    writer.WritePropertyName("GCCollectionCount0");
                    writer.Write(GC.CollectionCount(0));
                    writer.WritePropertyName("GCCollectionCount1");
                    writer.Write(GC.CollectionCount(1));
                    writer.WritePropertyName("GCCollectionCount2");
                    writer.Write(GC.CollectionCount(2));
                    writer.WriteObjectEnd();
                    break;
                case "/metrics/v1/events":
                    writer.WriteArrayStart();
                    Event ev;
                    while (!events.Empty)
                    {
                        if (events.TryDequeueBack(out ev))
                        {
                            writer.WriteObjectStart();
                            writer.WritePropertyName("Type");
                            writer.Write(ev.Type);
                            writer.WritePropertyName("Text");
                            writer.Write(ev.Text);
                            writer.WritePropertyName("Tags");
                            writer.WriteArrayStart();
                            foreach (var tag in ev.Tags)
                            {
                                writer.Write(tag);
                            }
                            writer.WriteArrayEnd();
                            writer.WritePropertyName("SecondsInThePast");
                            writer.Write((DateTime.Now - ev.Occurred).TotalSeconds);
                            writer.WriteObjectEnd();
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
                case "/metrics/v1/players":
                    writer.WriteArrayStart();
                    PlayerEvent playerEv;
                    while (!playerEvents.Empty)
                    {
                        if (playerEvents.TryDequeueBack(out playerEv))
                        {
                            writer.WriteObjectStart();
                            writer.WritePropertyName("Type");
                            writer.Write(playerEv.Type);
                            writer.WritePropertyName("SteamId");
                            writer.Write(playerEv.SteamId);
                            writer.WritePropertyName("MillisecondsInThePast");
                            writer.Write((DateTime.Now - playerEv.Occurred).TotalMilliseconds);
                            writer.WriteObjectEnd();
                        }
                    }
                    writer.WriteArrayEnd();
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

                        info = type.GetField("m_entitiesForUpdate", BindingFlags.NonPublic | BindingFlags.Static);
                        value = info.GetValue(null);
                        MyDistributedUpdater<ConcurrentCachingList<MyEntity>, MyEntity> m_entitiesForUpdate = value as MyDistributedUpdater<ConcurrentCachingList<MyEntity>, MyEntity>;
                        List<long> x_entitiesForUpdate = new List<long>();

                        info = type.GetField("m_entitiesForUpdate10", BindingFlags.NonPublic | BindingFlags.Static);
                        value = info.GetValue(null);
                        MyDistributedUpdater<CachingList<MyEntity>, MyEntity> m_entitiesForUpdate10 = value as MyDistributedUpdater<CachingList<MyEntity>, MyEntity>;
                        List<long> x_entitiesForUpdate10 = new List<long>();

                        info = type.GetField("m_entitiesForUpdate100", BindingFlags.NonPublic | BindingFlags.Static);
                        value = info.GetValue(null);
                        MyDistributedUpdater<CachingList<MyEntity>, MyEntity> m_entitiesForUpdate100 = value as MyDistributedUpdater<CachingList<MyEntity>, MyEntity>;
                        List<long> x_entitiesForUpdate100 = new List<long>();

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
                                long groupEntityId = 0L;
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

                                foreach (var group in MyCubeGridGroups.Static.Physical.Groups)
                                {
                                    bool found = false;

                                    foreach (var node in group.Nodes)
                                    {
                                        if (node.NodeData != myCubeGrid)
                                            continue;

                                        groupEntityId = group.Nodes.OrderByDescending(x => x.NodeData.BlocksCount).First().NodeData.EntityId;
                                        found = true;
                                        break;
                                    }

                                    if (found)
                                        break;
                                }

                                int conveyorInventoryBlockCount = 0;
                                int conveyorEndpointBlockCount = 0;
                                int conveyorLineCount = 0;
                                int conveyorConnectorCount = 0;
                                if (myCubeGrid?.GridSystems?.ConveyorSystem != null)
                                {
                                    type = myCubeGrid.GridSystems.ConveyorSystem.GetType();
                                    conveyorInventoryBlockCount = (type.GetField("m_inventoryBlocks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(myCubeGrid.GridSystems.ConveyorSystem) as HashSet<MyCubeBlock>).Count;
                                    conveyorEndpointBlockCount = (type.GetField("m_conveyorEndpointBlocks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(myCubeGrid.GridSystems.ConveyorSystem) as HashSet<IMyConveyorEndpointBlock>).Count;
                                    conveyorLineCount = (type.GetField("m_lines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(myCubeGrid.GridSystems.ConveyorSystem) as HashSet<MyConveyorLine>).Count;
                                    conveyorConnectorCount = (type.GetField("m_connectors", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(myCubeGrid.GridSystems.ConveyorSystem) as HashSet<MyShipConnector>).Count;
                                }

                                writer.WriteObjectStart();
                                writer.WritePropertyName("DisplayName");
                                writer.Write(myCubeGrid.DisplayName);
                                writer.WritePropertyName("EntityId");
                                writer.Write(myCubeGrid.EntityId);
                                writer.WritePropertyName("PhysicsGroupEntityId");
                                writer.Write(groupEntityId);
                                writer.WritePropertyName("GridSize");
                                writer.Write(myCubeGrid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small");
                                writer.WritePropertyName("BlocksCount");
                                writer.Write(myCubeGrid.BlocksCount);
                                writer.WritePropertyName("Mass");
                                writer.Write(myCubeGrid.Physics.Mass);
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
                                writer.WritePropertyName("ConveyorSystemInventoryBlockCount");
                                writer.Write(conveyorInventoryBlockCount);
                                writer.WritePropertyName("ConveyorSystemEndpointBlockCount");
                                writer.Write(conveyorEndpointBlockCount);
                                writer.WritePropertyName("ConveyorSystemLineCount");
                                writer.Write(conveyorLineCount);
                                writer.WritePropertyName("ConveyorSystemConnectorCount");
                                writer.Write(conveyorConnectorCount);
                                writer.WriteObjectEnd();
                            }
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
                case "/metrics/v1/session/asteroids":
                    writer.WriteArrayStart();
                    if (MySession.Static != null && MySession.Static.Ready)
                    {
                        MyConcurrentHashSet<MyEntity> entities = MyEntities.GetEntities();
                        foreach (MyEntity item in entities)
                        {
                            MyVoxelBase myVoxelBase = item as MyVoxelBase;
                            if (myVoxelBase != null && !(myVoxelBase is MyPlanet) && !myVoxelBase.Closed)
                            {
                                writer.WriteObjectStart();
                                writer.WritePropertyName("DisplayName");
                                writer.Write(myVoxelBase.StorageName);
                                writer.WritePropertyName("EntityId");
                                writer.Write(myVoxelBase.EntityId);
                                writer.WriteObjectEnd();
                            }
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
                case "/metrics/v1/session/planets":
                    writer.WriteArrayStart();
                    if (MySession.Static != null && MySession.Static.Ready)
                    {
                        MyConcurrentHashSet<MyEntity> entities = MyEntities.GetEntities();
                        foreach (MyEntity item in entities)
                        {
                            MyPlanet myPlanet = item as MyPlanet;
                            if (myPlanet != null && !myPlanet.Closed)
                            {
                                string storageName = myPlanet.StorageName;
                                long entityId = myPlanet.EntityId;
                                writer.WriteObjectStart();
                                writer.WritePropertyName("DisplayName");
                                writer.Write(storageName);
                                writer.WritePropertyName("EntityId");
                                writer.Write(entityId);
                                writer.WriteObjectEnd();
                            }
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
                case "/metrics/v1/session/floatingObjects":
                    writer.WriteArrayStart();
                    if (MySession.Static != null && MySession.Static.Ready)
                    {
                        ICollection<MyPlayer> onlinePlayers = MySession.Static.Players.GetOnlinePlayers();
                        MyConcurrentHashSet<MyEntity> entities = MyEntities.GetEntities();
                        foreach (MyEntity item in entities)
                        {
                            MyFloatingObject myFloatingObject = item as MyFloatingObject;
                            MyInventoryBagEntity myInventoryBagEntity = item as MyInventoryBagEntity;
                            if (myFloatingObject != null || myInventoryBagEntity != null)
                            {
                                string value = string.Empty;
                                long value2 = 0L;
                                string value3 = string.Empty;
                                float value4 = 0f;
                                float value5 = 0f;
                                float value6 = 0f;
                                string value7 = string.Empty;
                                if (myFloatingObject != null)
                                {
                                    if (myFloatingObject.Closed || myFloatingObject.Physics == null)
                                    {
                                        continue;
                                    }
                                    value = myFloatingObject.DisplayName;
                                    value2 = myFloatingObject.EntityId;
                                    value3 = "FloatingObject";
                                    value4 = myFloatingObject.Physics.Mass;
                                    value5 = myFloatingObject.Physics.LinearVelocity.Length();
                                    value6 = MySession.GetPlayerDistance(myFloatingObject, onlinePlayers);
                                    var def = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(myFloatingObject.Item.Content.TypeId, myFloatingObject.Item.Content.SubtypeId));
                                    value7 = def.DisplayNameText;
                                }
                                else if (myInventoryBagEntity != null)
                                {
                                    if (myInventoryBagEntity.Closed || myInventoryBagEntity.Physics == null)
                                    {
                                        continue;
                                    }
                                    value = myInventoryBagEntity.DisplayName;
                                    value2 = myInventoryBagEntity.EntityId;
                                    value3 = "Bag";
                                    value4 = myInventoryBagEntity.Physics.Mass;
                                    value5 = myInventoryBagEntity.Physics.LinearVelocity.Length();
                                    value6 = MySession.GetPlayerDistance(myInventoryBagEntity, onlinePlayers);
                                    value7 = "Bag";
                                }
                                writer.WriteObjectStart();
                                writer.WritePropertyName("DisplayName");
                                writer.Write(value);
                                writer.WritePropertyName("EntityId");
                                writer.Write(value2);
                                writer.WritePropertyName("Kind");
                                writer.Write(value3);
                                writer.WritePropertyName("Mass");
                                writer.Write(value4);
                                writer.WritePropertyName("LinearSpeed");
                                writer.Write(value5);
                                writer.WritePropertyName("DistanceToPlayer");
                                writer.Write(value6);
                                writer.WritePropertyName("TypeDisplayName");
                                writer.Write(value7);
                                writer.WriteObjectEnd();
                            }
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
                case "/metrics/v1/session/factions":
                    writer.WriteArrayStart();
                    if (MySession.Static != null && MySession.Static.Ready)
                    {
                        List<MyFaction> factions = MySession.Static.Factions.Select((x) => x.Value).ToList();
                        foreach (MyFaction myfaction in factions)
                        {
                            writer.WriteObjectStart();
                            writer.WritePropertyName("AcceptHumans");
                            writer.Write(myfaction.AcceptHumans);
                            writer.WritePropertyName("AutoAcceptMember");
                            writer.Write(myfaction.AutoAcceptMember);
                            writer.WritePropertyName("AutoAcceptPeace");
                            writer.Write(myfaction.AutoAcceptPeace);
                            writer.WritePropertyName("EnableFriendlyFire");
                            writer.Write(myfaction.EnableFriendlyFire);
                            writer.WritePropertyName("FactionId");
                            writer.Write(myfaction.FactionId);
                            writer.WritePropertyName("FounderId");
                            writer.Write(myfaction.FounderId);
                            writer.WritePropertyName("MemberCount");
                            writer.Write(myfaction.Members.Count);
                            writer.WritePropertyName("Name");
                            writer.Write(myfaction.Name);
                            writer.WritePropertyName("Tag");
                            writer.Write(myfaction.Tag);
                            writer.WritePropertyName("NPCOnly");
                            writer.Write(myfaction.Members.All((x) => MySession.Static.Players.IdentityIsNpc(x.Value.PlayerId)));
                            writer.WriteObjectEnd();
                        }
                    }
                    writer.WriteArrayEnd();
                    break;
            }

            return sb.ToString();
        }

        private void Torch_GameStateChanged(MySandboxGame game, TorchGameState newState)
        {
            if (MySandboxGame.ConfigDedicated.RemoteApiEnabled)
            {
                LogManager.GetCurrentClassLogger().Error($"Remote API is enabled, disable it!");
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
            else if (newState == TorchGameState.Loaded)
            {
                List<ulong> modIds = MySession.Static.Mods.Select((x) => x.PublishedFileId).ToList();
                List<ulong> modAddedIds = modIds.Except(_config.Data.LastModIds).ToList();
                List<ulong> modRemovedIds = _config.Data.LastModIds.Except(modIds).ToList();

                if (modAddedIds.Count > 0 || modRemovedIds.Count > 0)
                {
                    List<string> tags = new List<string>();
                    tags.Add("mods");
                    if (modAddedIds.Count > 0)
                        tags.Add("added");
                    if (modRemovedIds.Count > 0)
                        tags.Add("removed");

                    List<string> text = new List<string>();
                    foreach (ulong modId in modAddedIds)
                    {
                        text.Add($"Added {modId} ({MySession.Static.Mods.Find((x) => x.PublishedFileId == modId).Name})");
                    }
                    foreach (ulong modId in modRemovedIds)
                    {
                        text.Add($"Removed {modId} ({MySession.Static.Mods.Find((x) => x.PublishedFileId == modId).Name})");
                    }

                    events.EnqueueFront(new Event
                    {
                        Type = "config",
                        Text = string.Join("\n", text),
                        Tags = tags.ToArray(),
                        Occurred = DateTime.Now,
                    });

                    _config.Data.LastModIds = modIds;
                    _config.Save();
                }

                GC.Collect();
            }
        }

        private void SessionChanged(ITorchSession session, TorchSessionState state)
        {
            var mpMan = Torch.CurrentSession.Managers.GetManager<IMultiplayerManagerServer>();
            switch (state)
            {
                case TorchSessionState.Loaded:
                    mpMan.PlayerJoined += PlayerJoined;
                    mpMan.PlayerLeft += PlayerLeft;
                    break;
                case TorchSessionState.Unloading:
                    mpMan.PlayerJoined -= PlayerJoined;
                    mpMan.PlayerLeft -= PlayerLeft;
                    break;
            }
        }

        private void PlayerJoined(IPlayer obj)
        {
            events.EnqueueFront(new Event
            {
                Type = "session",
                Text = $"Player {obj.Name} joined",
                Tags = new string[]{ "player", "joined" },
                Occurred = DateTime.Now,
            });
        }

        private void PlayerLeft(IPlayer obj)
        {
            if (obj.Name.StartsWith("ID:")) return;
            events.EnqueueFront(new Event
            {
                Type = "session",
                Text = $"Player {obj.Name} left",
                Tags = new string[] { "player", "left" },
                Occurred = DateTime.Now,
            });
        }

        private void PluginsLoaded(IReadOnlyCollection<Torch.API.Plugins.ITorchPlugin> obj)
        {
            var currentIds = obj.Select((x) => x.Id);
            var lastIds = _config.Data.LastPlugins.Select((x) => x.Key);

            List<string> text = new List<string>();
            List<string> tags = new List<string>();
            tags.Add("plugins");

            var added = currentIds.Except(lastIds).ToList();
            if (added.Count > 0)
                tags.Add("added");

            foreach (Guid id in added)
            {
                var plugin = obj.Where((x) => x.Id == id).First();
                text.Add($"Added {plugin.Name} ({plugin.Version})");
            }

            var removed = lastIds.Except(currentIds).ToList();
            if (removed.Count > 0)
                tags.Add("removed");

            foreach (Guid id in removed)
            {
                var plugin = _config.Data.LastPlugins.Find((x) => x.Key == id).Value;
                text.Add($"Removed {plugin.Name} ({plugin.Version})");
            }

            bool changes = false;
            foreach (Guid id in currentIds.Intersect(lastIds))
            {
                var currentPlugin = obj.Where((x) => x.Id == id).First();
                var oldPlugin = _config.Data.LastPlugins.Find((x) => x.Key == id).Value;
                if (currentPlugin.Version != oldPlugin.Version)
                {
                    changes = true;
                    text.Add($"Changed {currentPlugin.Name} ({oldPlugin.Version}) -> ({currentPlugin.Version})");
                }
            }
            if (changes)
                tags.Add("changes");

            if (tags.Count > 1)
            {
                events.EnqueueFront(new Event
                {
                    Type = "config",
                    Text = string.Join("\n", text),
                    Tags = tags.ToArray(),
                    Occurred = DateTime.Now,
                });

                _config.Data.LastPlugins = obj.Select((x) => new SerializeableKeyValue<Guid, ConfigPlugin>
                {
                    Key = x.Id,
                    Value = new ConfigPlugin
                    {
                        Name = x.Name,
                        Version = x.Version,
                    },
                }).ToList();
                _config.Save();
            }
        }

        private void LoadTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            loadEvents.EnqueueFront(new LoadEvent
            {
                ServerCPULoad = Sync.ServerCPULoad,
                ServerCPULoadSmooth = Sync.ServerCPULoadSmooth,
                ServerSimulationRatio = Sync.ServerSimulationRatio,
                ServerThreadLoad = Sync.ServerThreadLoad,
                ServerThreadLoadSmooth = Sync.ServerThreadLoadSmooth,
                Occurred = DateTime.Now,
            });
        }

        public static void WaitForFullGCProc()
        {
            while (true)
            {
                // Check for a notification of an approaching collection.
                GCNotificationStatus s = GC.WaitForFullGCApproach();
                if (s == GCNotificationStatus.Succeeded)
                {
                    //Console.WriteLine("GC Notification raised.");
                    events.EnqueueFront(new Event
                    {
                        Type = "process",
                        Text = $"Full GC Approach",
                        Tags = new string[] { "gc" },
                        Occurred = DateTime.Now,
                    });
                }
                else if (s == GCNotificationStatus.Canceled)
                {
                    //Console.WriteLine("GC Notification cancelled.");
                    break;
                }
                else
                {
                    // This can occur if a timeout period
                    // is specified for WaitForFullGCApproach(Timeout) 
                    // or WaitForFullGCComplete(Timeout)  
                    // and the time out period has elapsed. 
                    //Console.WriteLine("GC Notification not applicable.");
                    break;
                }

                // Check for a notification of a completed collection.
                GCNotificationStatus status = GC.WaitForFullGCComplete();
                if (status == GCNotificationStatus.Succeeded)
                {
                    //Console.WriteLine("GC Notification raised.");
                    events.EnqueueFront(new Event
                    {
                        Type = "process",
                        Text = $"Full GC Complete",
                        Tags = new string[] { "gc" },
                        Occurred = DateTime.Now,
                    });
                }
                else if (status == GCNotificationStatus.Canceled)
                {
                    //Console.WriteLine("GC Notification cancelled.");
                    break;
                }
                else
                {
                    // Could be a time out.
                    //Console.WriteLine("GC Notification not applicable.");
                    break;
                }

                Thread.Sleep(500);
            }
        }

        public static void PrefixAsyncSavingStart(Action callbackOnFinished = null, string customName = null, bool wait = false)
        {
            events.EnqueueFront(new Event
            {
                Type = "process",
                Text = $"Save started",
                Tags = new string[] { "save" },
                Occurred = DateTime.Now,
            });
            saveStopwatch.Start();
        }

        public static void SuffixAsyncSavingStart(Action callbackOnFinished = null, string customName = null, bool wait = false)
        {
            saveStopwatch.Stop();
            saveDuration += saveStopwatch.ElapsedMilliseconds;
            saveStopwatch.Reset();
        }

        public static void SuffixBanClient(ulong userId, bool banned)
        {
            playerEvents.EnqueueFront(new PlayerEvent
            {
                Type = banned ? "Ban" : "Unban",
                SteamId = userId,
                Occurred = DateTime.Now,
            });
        }

        public static void SuffixCreateNewIdentity(string identityName, MyPlayer.PlayerId playerId, string modelName, bool initialPlayer = false)
        {
            playerEvents.EnqueueFront(new PlayerEvent
            {
                Type = "New",
                SteamId = playerId.SteamId,
                Occurred = DateTime.Now,
            });
        }
    }
}
