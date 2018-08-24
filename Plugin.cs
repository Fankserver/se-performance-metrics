﻿using Sandbox;
using Sandbox.Game.Entities.Blocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using VRage.Plugins;

namespace performance_metrics
{
    public class Plugin : IPlugin
    {
        private WebServer ws;
        private Metric metric;
        private BlockWatcher watcher;
        static System.Timers.Timer ticker;

        public void Dispose()
        {
            ws.Stop();

            ticker.Stop();
            ticker.Close();
        }

        public void Init(object gameInstance)
        {
            metric = new Metric();

            watcher = new BlockWatcher();

            ticker = new System.Timers.Timer();
            ticker.Interval = 10000;
            ticker.Elapsed += OnTicker10;
            ticker.AutoReset = true;
            ticker.Start();

            ws = new WebServer(SendHttpResponseResponse, "http://*:3000/");
            ws.Run();
        }

        public void Update()
        {
            //throw new NotImplementedException();
        }

        private void OnTicker10(object sender, System.Timers.ElapsedEventArgs e)
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            metric.Process.PagedMemorySize = process.PagedMemorySize64;
            metric.Process.PrivateMemorySize = process.PrivateMemorySize64;
            metric.Process.VirtualMemorySize = process.VirtualMemorySize64;

            metric.ProgrammableBlocks.Clear();
            watcher.ProgrammableBlocks.ForEach((x) =>
            {
                metric.ProgrammableBlocks.Add(new ProgrammableBlocks()
                {
                    Enabled = x.Value.Enabled,
                });
            });
        }

        public string SendHttpResponseResponse(HttpListenerRequest request)
        {
            return new JavaScriptSerializer().Serialize(metric);
        }
    }
}
