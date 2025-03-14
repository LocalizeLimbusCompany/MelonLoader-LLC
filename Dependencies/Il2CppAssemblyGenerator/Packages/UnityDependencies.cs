using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;

namespace MelonLoader.Il2CppAssemblyGenerator.Packages
{
    internal class UnityDependencies : Models.PackageBase
    {
        // 获取最快节点
        private string fastestNode;

        internal UnityDependencies()
        {
            fastestNode = GetFastnetNode();
            Name = nameof(UnityDependencies);
            Version = InternalUtils.UnityInformationHandler.EngineVersion.ToStringWithoutType();
            URL = "https://" + fastestNode + $"/files/{Version}.zip";
            Destination = Path.Combine(Core.BasePath, Name);
            FilePath = Path.Combine(Core.BasePath, $"{Name}_{Version}.zip");
        }

        internal override bool ShouldSetup()
            => string.IsNullOrEmpty(Config.Values.UnityVersion)
            || !Config.Values.UnityVersion.Equals(Version);

        internal override void Save()
            => Save(ref Config.Values.UnityVersion);

        private string GetFastnetNode()
        {
            string[] urls = { "limbus.determination.top", "llc.determination.top", "dl.determination.top" };

            Dictionary<string, long> pingTimes = new();

            foreach (string url in urls)
            {
                Ping ping = new();
                try
                {
                    PingReply reply = ping.Send(url);
                    if (reply.Status == IPStatus.Success)
                    {
                        pingTimes.Add(url, reply.RoundtripTime);
                    }
                }
                catch
                {
                }
            }

            List<KeyValuePair<string, long>> pingTimesList = new(pingTimes);
            pingTimesList.Sort(delegate (KeyValuePair<string, long> pair1, KeyValuePair<string, long> pair2)
            {
                return pair1.Value.CompareTo(pair2.Value);
            });
            return pingTimesList[0].Key;
        }
    }
}
