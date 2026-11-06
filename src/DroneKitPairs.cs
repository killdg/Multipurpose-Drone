using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT.InventoryLogic;

namespace Killdg.MultipurposeDrone
{
    internal sealed class DroneKitPair
    {
        internal DroneKitPair(string aircraft, string remote, string glasses, int serial)
        {
            Aircraft = aircraft;
            Remote = remote;
            Glasses = glasses;
            Serial = serial;
        }

        internal string Aircraft { get; }
        internal string Remote { get; }
        internal string Glasses { get; }
        internal int Serial { get; }
    }

    internal static class DroneKitPairs
    {
        // Native item instance IDs survive moving gear between stash and raid.
        // This local file stores only the three IDs from an unpacked kit; it
        // never edits a profile or guesses a pairing from a template ID.
        internal static string RegistryPath { get; set; } = null!;

        private static Dictionary<string, DroneKitPair>? savedPairs;
        private static readonly Dictionary<string, int> serialByItem = new(StringComparer.Ordinal);

        internal static int SerialFor(Item item)
        {
            string template = item.TemplateId.ToString();
            if (template != DroneItems.Drone && template != DroneItems.Remote && template != DroneItems.Glasses)
            {
                return 0;
            }

            Read();
            return serialByItem.TryGetValue(item.Id, out int serial) ? serial : 0;
        }

        internal static Dictionary<string, DroneKitPair> Read()
        {
            if (savedPairs != null)
            {
                return savedPairs;
            }

            var pairs = new Dictionary<string, DroneKitPair>(StringComparer.Ordinal);
            int next = 1;
            if (File.Exists(RegistryPath))
            {
                foreach (string line in File.ReadLines(RegistryPath))
                {
                    string[] ids = line.Split(' ');
                    if ((ids.Length != 3 && ids.Length != 4)
                        || !ids.Take(3).All(id => id.Length == 24 && id.All(Uri.IsHexDigit)))
                    {
                        continue;
                    }

                    // Older registries already carry exact item IDs. Number
                    // those records in their saved order before issuing new ones.
                    int serial = ids.Length == 4 && int.TryParse(ids[3], out int stored) && stored > 0
                        ? stored : next;
                    pairs[ids[0]] = new DroneKitPair(ids[0], ids[1], ids[2], serial);
                    next = Math.Max(next, serial + 1);
                }
            }

            foreach (DroneKitPair pair in pairs.Values)
            {
                Index(pair);
            }

            savedPairs = pairs;
            return pairs;
        }

        private static void Index(DroneKitPair pair)
        {
            serialByItem[pair.Aircraft] = pair.Serial;
            serialByItem[pair.Remote] = pair.Serial;
            serialByItem[pair.Glasses] = pair.Serial;
        }

        internal static void Register(Item[] contents)
        {
            Item? aircraft = contents.SingleOrDefault(item => item.TemplateId == DroneItems.Drone);
            Item? remote = contents.SingleOrDefault(item => item.TemplateId == DroneItems.Remote);
            Item? glasses = contents.SingleOrDefault(item => item.TemplateId == DroneItems.Glasses);
            if (aircraft == null || remote == null || glasses == null)
            {
                // A partial retry already has its pair recorded before any move.
                return;
            }

            var pairs = Read();
            if (pairs.ContainsKey(aircraft.Id))
            {
                return;
            }

            int serial = pairs.Values.Select(pair => pair.Serial).DefaultIfEmpty(0).Max() + 1;
            var added = new DroneKitPair(aircraft.Id, remote.Id, glasses.Id, serial);
            string temporaryPath = RegistryPath + ".tmp";
            File.WriteAllLines(temporaryPath, pairs.Values.Append(added).Select(
                pair => $"{pair.Aircraft} {pair.Remote} {pair.Glasses} {pair.Serial}"));
            if (File.Exists(RegistryPath))
            {
                File.Replace(temporaryPath, RegistryPath, null);
            }
            else
            {
                File.Move(temporaryPath, RegistryPath);
            }

            pairs[added.Aircraft] = added;
            Index(added);
        }
    }
}
