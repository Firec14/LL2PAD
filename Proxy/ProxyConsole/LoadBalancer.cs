using System;
using System.Collections.Generic;

namespace Proxy
{
    public class LoadBalancer
    {
        private readonly List<string> masters;
        private readonly List<string> slaves;
        private int slaveIndex = 0;

        public LoadBalancer(string[] masters, string[] slaves)
        {
            this.masters = new List<string>(masters);
            this.slaves = new List<string>(slaves);
        }

        public string GetMaster()
        {
            return masters[0]; // avem doar unul, dar se poate extinde
        }

        public string GetNextSlave()
        {
            if (slaves.Count == 0)
                return GetMaster(); // fallback
            var slave = slaves[slaveIndex % slaves.Count];
            slaveIndex++;
            return slave;
        }
    }
}
