using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace performance_metrics
{
    class BlockWatcher
    {
        internal readonly Dictionary<long, IMyProgrammableBlock> ProgrammableBlocks = new Dictionary<long, IMyProgrammableBlock>();

        public BlockWatcher()
        {
            MyEntities.OnEntityCreate += OnEntityCreate;
            MyEntities.OnEntityDelete += OnEntityDelete;
            MyEntities.OnEntityRemove += OnEntityDelete;

            foreach (var entity in MyEntities.GetEntities().Where((x) => (x as IMyCubeGrid) != null))
                OnEntityCreate(entity);
        }

        private void OnEntityCreate(MyEntity obj)
        {
            IMyCubeGrid cubeGrid = obj as IMyCubeGrid;
            if (cubeGrid == null)
                return;

            cubeGrid.OnBlockAdded += OnBlockAdded;
            cubeGrid.OnBlockRemoved += OnBlockRemoved;

            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            cubeGrid.GetBlocks(blocks);
            foreach (var block in blocks.Where((x) => (x as IMyTerminalBlock) != null))
                OnBlockAdded(block);
        }

        private void OnEntityDelete(MyEntity obj)
        {
            IMyCubeGrid cubeGrid = obj as IMyCubeGrid;
            if (cubeGrid == null)
                return;

            cubeGrid.OnBlockAdded -= OnBlockAdded;
            cubeGrid.OnBlockRemoved -= OnBlockRemoved;

            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            cubeGrid.GetBlocks(blocks);
            foreach (var block in blocks.Where((x) => (x as IMyTerminalBlock) != null))
                OnBlockRemoved(block);
        }

        private void OnBlockAdded(IMySlimBlock obj)
        {
            IMyTerminalBlock terminalBlock = obj.FatBlock as IMyTerminalBlock;
            if (terminalBlock == null)
                return;

            MyLog.Default.WriteLineAndConsole(terminalBlock.BlockDefinition.SubtypeName);

            switch (terminalBlock.BlockDefinition.SubtypeName)
            {
                case "LargeProgrammableBlock":
                    ProgrammableBlocks.Add(terminalBlock.EntityId, terminalBlock as IMyProgrammableBlock);
                    break;
            }
        }

        private void OnBlockRemoved(IMySlimBlock obj)
        {
            IMyTerminalBlock terminalBlock = obj.FatBlock as IMyTerminalBlock;
            if (terminalBlock == null)
                return;

            MyLog.Default.WriteLineAndConsole(terminalBlock.BlockDefinition.SubtypeName);

            switch (terminalBlock.BlockDefinition.SubtypeName)
            {
                case "LargeProgrammableBlock":
                    ProgrammableBlocks.Remove(terminalBlock.EntityId);
                    break;
            }
        }
    }
}
