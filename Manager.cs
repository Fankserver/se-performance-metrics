using LitJson;
using NLog;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Session;
using Torch.Managers;
using Torch.Session;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
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

    class PerformanceMetricsManager : Manager
    {
        private WebServer ws;
        private TorchSessionManager _sessionManager;
        private static MyConcurrentDeque<Event> events = new MyConcurrentDeque<Event>();

        public PerformanceMetricsManager(ITorchBase torchInstance) : base(torchInstance)
        {
            GC.RegisterForFullGCNotification(10, 10);
            Thread thWaitForFullGC = new Thread(new ThreadStart(WaitForFullGCProc));
            thWaitForFullGC.Start();
        }

        /// <inheritdoc cref="Manager.Attach"/>
        public override void Attach()
        {
            base.Attach();

            Torch.GameStateChanged += Torch_GameStateChanged;
            _sessionManager = Torch.Managers.GetManager<TorchSessionManager>();
            if (_sessionManager != null)
                _sessionManager.SessionStateChanged += SessionChanged;
            else
                LogManager.GetCurrentClassLogger().Warn("No session manager. Player metrics won't work");

            LogManager.GetCurrentClassLogger().Info("Attached");
        }

        /// <inheritdoc cref="Manager.Detach"/>
        public override void Detach()
        {
            base.Detach();

            Torch.GameStateChanged -= Torch_GameStateChanged;
            if (_sessionManager != null)
                _sessionManager.SessionStateChanged -= SessionChanged;
            _sessionManager = null;
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
                    writer.WriteObjectEnd();
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
                            writer.WritePropertyName("Occurred");
                            writer.Write(ev.Occurred.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", DateTimeFormatInfo.InvariantInfo));
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
                                Vector3D position = myVoxelBase.PositionComp.GetPosition();
                                writer.WritePropertyName("Position");
                                writer.WriteObjectStart();
                                writer.WritePropertyName("X");
                                writer.Write(position.X);
                                writer.WritePropertyName("Y");
                                writer.Write(position.Y);
                                writer.WritePropertyName("Z");
                                writer.Write(position.Z);
                                writer.WriteObjectEnd();
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
                                Vector3D position = myPlanet.PositionComp.GetPosition();
                                writer.WritePropertyName("Position");
                                writer.WriteObjectStart();
                                writer.WritePropertyName("X");
                                writer.Write(position.X);
                                writer.WritePropertyName("Y");
                                writer.Write(position.Y);
                                writer.WritePropertyName("Z");
                                writer.Write(position.Z);
                                writer.WriteObjectEnd();
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
                                Vector3D vector3D = Vector3D.Zero;
                                float value5 = 0f;
                                float value6 = 0f;
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
                                    vector3D = myFloatingObject.PositionComp.GetPosition();
                                    value5 = myFloatingObject.Physics.LinearVelocity.Length();
                                    value6 = MySession.GetPlayerDistance(myFloatingObject, onlinePlayers);
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
                                    vector3D = myInventoryBagEntity.PositionComp.GetPosition();
                                    value5 = myInventoryBagEntity.Physics.LinearVelocity.Length();
                                    value6 = MySession.GetPlayerDistance(myInventoryBagEntity, onlinePlayers);
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
                                writer.WritePropertyName("Position");
                                writer.WriteObjectStart();
                                writer.WritePropertyName("X");
                                writer.Write(vector3D.X);
                                writer.WritePropertyName("Y");
                                writer.Write(vector3D.Y);
                                writer.WritePropertyName("Z");
                                writer.Write(vector3D.Z);
                                writer.WriteObjectEnd();
                                writer.WritePropertyName("LinearSpeed");
                                writer.Write(value5);
                                writer.WritePropertyName("DistanceToPlayer");
                                writer.Write(value6);
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
                Occurred = DateTime.Now.ToUniversalTime(),
            });
        }

        private void PlayerLeft(IPlayer obj)
        {
            events.EnqueueFront(new Event
            {
                Type = "session",
                Text = $"Player {obj.Name} left",
                Tags = new string[] { "player", "left" },
                Occurred = DateTime.Now.ToUniversalTime(),
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
                        Occurred = DateTime.Now.ToUniversalTime(),
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
                        Occurred = DateTime.Now.ToUniversalTime(),
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
    }
}
