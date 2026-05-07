using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FakePopulationBases", "Alexi", "2.0.0")]
    [Description("Spawns fake honeypot bases via CopyPaste templates to simulate player population")]
    public class FakePopulationBases : RustPlugin
    {
        [PluginReference] private Plugin CopyPaste;

        #region Constants

        private const string PermAdmin = "fakepopulationbases.admin";

        private static readonly int RaycastMask =
            LayerMask.GetMask("Construction", "Terrain", "World", "Default");

        #endregion

        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Cleanup all fake bases on map wipe")]
            public bool CleanupOnWipe = true;

            [JsonProperty("Automatically spawn random bases at the start of each wipe")]
            public bool AutoSpawnOnWipe = true;

            [JsonProperty("Number of bases to auto-spawn on wipe")]
            public int AutoSpawnOnWipeCount = 10;

            [JsonProperty("Delay after server start before the FIRST auto-spawn (seconds)")]
            public float AutoSpawnOnWipeDelay = 60f;

            [JsonProperty("Stagger window for wipe spawns (minutes). 0 = all at once. Default 90 min spreads spawns realistically over wipe day.")]
            public float AutoSpawnOnWipeStaggerMinutes = 90f;

            [JsonProperty("Apply fake usernames to sleeping bags and TC owner so they look like real player bases")]
            public bool FakeOwnership = true;

            [JsonProperty("Pool of fake usernames used for sleeping bags / TC owner")]
            public List<string> FakeUsernames = new List<string>
            {
                "Bob", "Mike", "Dave", "Steve", "Tom", "Jack", "Tim", "Ben",
                "Alex", "Chris", "Mark", "Paul", "John", "Sam", "Will", "Ryan",
                "ProGamer42", "RustLord", "BeachBoy", "NoobMaster69", "xX_Sniper_Xx",
                "BobTheBuilder", "RustyNail", "DesertFox", "MoonLight", "ShadowHunter",
                "Aragorn", "Frodo", "Gandalf", "Legolas", "Boromir",
                "Sarah", "Emma", "Lisa", "Anna", "Kate", "Mia", "Zoe",
                "TacoMaster", "PvPGod", "FreshSpawn", "RaidLord", "HoboKing",
                "Stoneage", "Wolverine", "Banjo", "Nomad", "Phoenix",
            };

            [JsonProperty("Enable night lighting (lanterns turn on at night)")]
            public bool NightLighting = true;

            [JsonProperty("Lighting check interval (seconds)")]
            public float LightingCheckInterval = 60f;

            [JsonProperty("Templates available for /fakebase. Add CopyPaste filenames here.")]
            public List<TemplateEntry> Templates = new List<TemplateEntry>
            {
                new TemplateEntry { Name = "s1x2", Weight = 1.0f },
            };

            [JsonProperty("Maximum raycast distance for /fakebase (meters)")]
            public float MaxLookDistance = 30f;

            [JsonProperty("Default count for /fakebase random")]
            public int DefaultRandomCount = 5;

            [JsonProperty("Hard cap on /fakebase random count")]
            public int MaxRandomCount = 50;

            [JsonProperty("Map-edge buffer for random spawns (meters)")]
            public float MapEdgeBuffer = 80f;

            [JsonProperty("Delay between random paste attempts (seconds)")]
            public float RandomPasteDelay = 0.6f;

            [JsonProperty("Reject spawn if water depth at footprint exceeds this (meters, 0 = disabled)")]
            public float MaxWaterDepth = 0.5f;

            [JsonProperty("Reject spawn on Road / Rail / Cliff / Building / River / Lake / Ocean / Monument topology")]
            public bool BlockBadTopology = true;

            [JsonProperty("Minimum distance from monument bounds (meters, 0 = disabled)")]
            public float MonumentBufferMeters = 30f;

            [JsonProperty("Auto-stock the tool cupboard found in pasted bases")]
            public bool StockCupboard = true;

            [JsonProperty("Auto-stock wooden boxes found in pasted bases")]
            public bool StockBoxes = true;

            [JsonProperty("Loot pool for tool cupboard")]
            public List<LootEntry> CupboardLoot = new List<LootEntry>
            {
                new LootEntry { Shortname = "wood",            Min = 50, Max = 300, Chance = 1.0f },
                new LootEntry { Shortname = "stones",          Min = 20, Max = 150, Chance = 1.0f },
                new LootEntry { Shortname = "metal.fragments", Min = 0,  Max = 60,  Chance = 0.6f },
                new LootEntry { Shortname = "scrap",           Min = 1,  Max = 5,   Chance = 0.4f },
                new LootEntry { Shortname = "cloth",           Min = 5,  Max = 30,  Chance = 0.5f },
            };

            [JsonProperty("Loot pool for wooden boxes")]
            public List<LootEntry> BoxLoot = new List<LootEntry>
            {
                new LootEntry { Shortname = "wood",           Min = 20, Max = 100, Chance = 0.7f },
                new LootEntry { Shortname = "stones",         Min = 10, Max = 60,  Chance = 0.5f },
                new LootEntry { Shortname = "lowgradefuel",   Min = 5,  Max = 25,  Chance = 0.4f },
                new LootEntry { Shortname = "chicken.cooked", Min = 1,  Max = 3,   Chance = 0.3f },
            };
        }

        public class TemplateEntry
        {
            public string Name;
            public float Weight = 1.0f;
        }

        public class LootEntry
        {
            public string Shortname;
            public int Min;
            public int Max;
            public float Chance = 1.0f;
        }

        protected override void LoadDefaultConfig() { config = new Configuration(); }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch (Exception e)
            {
                PrintError($"Configuration corrupted: {e.Message}. Regenerating defaults.");
                config = new Configuration();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion

        #region Data

        private StoredData storedData;

        private class StoredData
        {
            [JsonProperty("entityIds")]
            public List<ulong> EntityIds = new List<ulong>();
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
            }
            catch
            {
                storedData = new StoredData();
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        #endregion

        #region Lifecycle

        private Timer lightingTimer;
        private bool _pendingWipeSpawn;

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (CopyPaste == null)
                PrintWarning("CopyPaste plugin not loaded. Install it from https://umod.org/plugins/copy-paste");

            RebuildTemplateIndex();

            if (config.NightLighting)
                lightingTimer = timer.Every(config.LightingCheckInterval, UpdateNightLighting);

            if (_pendingWipeSpawn)
            {
                _pendingWipeSpawn = false;
                ScheduleStaggeredWipeSpawn();
            }
        }

        private void OnNewSave(string filename)
        {
            if (config.CleanupOnWipe)
            {
                int removed = CleanupAllBases(silent: true);
                Puts($"Wipe detected — {removed} stale fake base entities purged.");
            }

            if (config.AutoSpawnOnWipe)
                _pendingWipeSpawn = true;
        }

        private void Unload()
        {
            lightingTimer?.Destroy();
            SaveData();
        }

        #endregion

        #region Commands

        [ChatCommand("fakebase")]
        private void CmdFakeBase(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPerm(player))
            {
                SendReply(player, "<color=#ff5555>Permission refusée.</color>");
                return;
            }

            if (args.Length > 0)
            {
                string sub = args[0].ToLowerInvariant();
                if (sub == "cleanup")
                {
                    int removed = CleanupAllBases();
                    SendReply(player, $"<color=#55ff55>{removed} entités supprimées.</color>");
                    return;
                }
                if (sub == "random")
                {
                    if (!CheckCopyPaste(player)) return;

                    int count = config.DefaultRandomCount;
                    if (args.Length > 1 && int.TryParse(args[1], out int parsed) && parsed > 0)
                        count = Math.Min(parsed, config.MaxRandomCount);

                    SendReply(player, $"<color=#55ff55>Génération de {count} bases aléatoires (~{count * config.RandomPasteDelay:F1}s)...</color>");
                    SpawnRandomBases(count, player);
                    return;
                }
                if (sub == "list")
                {
                    string list = string.Join(", ", config.Templates.Select(t => $"{t.Name}(x{t.Weight:F1})"));
                    SendReply(player, $"<color=#aaffaa>Templates: {list}</color>");
                    return;
                }
                if (sub == "help")
                {
                    SendReply(player,
                        "<color=#aaffaa>/fakebase</color> — base au point visé\n" +
                        "<color=#aaffaa>/fakebase random [N]</color> — N bases aléatoires\n" +
                        "<color=#aaffaa>/fakebase cleanup</color> — supprime toutes les bases\n" +
                        "<color=#aaffaa>/fakebase list</color> — liste les templates");
                    return;
                }
            }

            if (!CheckCopyPaste(player)) return;

            Vector3? target = GetLookPoint(player, config.MaxLookDistance);
            if (target == null)
            {
                SendReply(player, $"<color=#ff5555>Vise un sol à moins de {config.MaxLookDistance} m.</color>");
                return;
            }

            Quaternion baseRot = ComputeBaseRotation(player.eyes.HeadForward());
            if (!IsValidSpawnPoint(target.Value, baseRot, out string reason))
            {
                SendReply(player, $"<color=#ff5555>Spawn refusé : {reason}.</color>");
                return;
            }

            string template = PickTemplate();
            if (template == null)
            {
                SendReply(player, "<color=#ff5555>Aucun template configuré.</color>");
                return;
            }

            float yaw = baseRot.eulerAngles.y;
            bool ok = PasteAndTrack(template, target.Value, yaw);
            if (ok)
                SendReply(player, $"<color=#55ff55>Base '{template}' en cours de paste...</color>");
            else
                SendReply(player, $"<color=#ff5555>Échec — vérifie que oxide/data/copypaste/{template}.json existe.</color>");
        }

        [ConsoleCommand("fakebase.cleanup")]
        private void CmdCleanupConsole(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            if (caller != null && !HasAdminPerm(caller)) return;
            int removed = CleanupAllBases();
            arg.ReplyWith($"Removed {removed} fake base entities.");
        }

        [ConsoleCommand("fakebase.random")]
        private void CmdRandomConsole(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            if (caller != null && !HasAdminPerm(caller)) return;
            if (CopyPaste == null) { arg.ReplyWith("CopyPaste plugin not loaded."); return; }
            int count = arg.HasArgs() ? Math.Min(arg.GetInt(0, config.DefaultRandomCount), config.MaxRandomCount) : config.DefaultRandomCount;
            SpawnRandomBases(count, caller);
            arg.ReplyWith($"Spawning {count} fake bases asynchronously.");
        }

        #endregion

        #region Helpers

        private bool HasAdminPerm(BasePlayer player)
        {
            return player != null &&
                   (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin));
        }

        private bool CheckCopyPaste(BasePlayer player)
        {
            if (CopyPaste != null) return true;
            SendReply(player, "<color=#ff5555>Plugin CopyPaste non chargé. Installe-le depuis uMod.</color>");
            return false;
        }

        private Vector3? GetLookPoint(BasePlayer player, float maxDistance)
        {
            if (Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, maxDistance, RaycastMask))
                return hit.point;
            return null;
        }

        private float TerrainY(Vector3 pos)
        {
            return TerrainMeta.HeightMap?.GetHeight(pos) ?? pos.y;
        }

        private Quaternion ComputeBaseRotation(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            float yaw = Mathf.Round(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg / 90f) * 90f;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        private bool IsValidSpawnPoint(Vector3 center, Quaternion baseRot, out string reason)
        {
            reason = null;

            const int blockedTopo =
                (int)TerrainTopology.Enum.Road     | (int)TerrainTopology.Enum.Rail   |
                (int)TerrainTopology.Enum.Building | (int)TerrainTopology.Enum.Cliff  |
                (int)TerrainTopology.Enum.River    | (int)TerrainTopology.Enum.Lake   |
                (int)TerrainTopology.Enum.Ocean    | (int)TerrainTopology.Enum.Monument;

            // Sample center + 4 nearby points to cover the footprint
            Vector3[] samples =
            {
                center,
                center + baseRot * new Vector3( 3f, 0f,  0f),
                center + baseRot * new Vector3(-3f, 0f,  0f),
                center + baseRot * new Vector3( 0f, 0f,  3f),
                center + baseRot * new Vector3( 0f, 0f, -3f),
            };

            foreach (var pt in samples)
            {
                if (config.MaxWaterDepth > 0f && TerrainMeta.WaterMap != null)
                {
                    float waterY = TerrainMeta.WaterMap.GetHeight(pt);
                    float terrY  = TerrainY(pt);
                    if (waterY - terrY > config.MaxWaterDepth)
                    {
                        reason = "point dans l'eau";
                        return false;
                    }
                }

                if (config.BlockBadTopology && TerrainMeta.TopologyMap != null)
                {
                    int topo = TerrainMeta.TopologyMap.GetTopology(pt);
                    if ((topo & blockedTopo) != 0)
                    {
                        reason = "topologie incompatible (route, falaise, monument...)";
                        return false;
                    }
                }
            }

            if (config.MonumentBufferMeters > 0f && TerrainMeta.Path?.Monuments != null)
            {
                foreach (var monument in TerrainMeta.Path.Monuments)
                {
                    if (monument == null) continue;
                    if (monument.Distance(center) < config.MonumentBufferMeters)
                    {
                        reason = $"trop proche du monument '{monument.name}'";
                        return false;
                    }
                }
            }

            return true;
        }

        private string PickTemplate()
        {
            if (config.Templates == null || config.Templates.Count == 0) return null;

            float total = 0f;
            foreach (var t in config.Templates) total += Math.Max(0.01f, t.Weight);

            float roll = UnityEngine.Random.Range(0f, total);
            foreach (var t in config.Templates)
            {
                roll -= Math.Max(0.01f, t.Weight);
                if (roll <= 0f) return t.Name;
            }
            return config.Templates[0].Name;
        }

        #endregion

        #region Paste

        // Counter incremented each time we trigger a paste; OnPasteFinished decrements
        // and processes the spawned entities. Confirmed against CopyPaste v4.2.7 source:
        // TryPasteFromVector3 returns `true` on success — the entity list arrives via
        // the OnPasteFinished hook, NOT in the return value.
        private int _pendingPastes;
        private HashSet<string> _myTemplateNames;

        private bool PasteAndTrack(string templateName, Vector3 position, float yawDegrees)
        {
            if (CopyPaste == null) return false;

            var args = new string[]
            {
                "autoheight",     "true",
                "blockcollision", "0",
                "deployables",    "true",
                "inventories",    "true",
                "stability",      "false",
            };

            _pendingPastes++;

            object result;
            try
            {
                result = CopyPaste.Call("TryPasteFromVector3", position, yawDegrees, templateName, args);
            }
            catch (Exception e)
            {
                _pendingPastes--;
                PrintError($"CopyPaste.TryPasteFromVector3 threw: {e.Message}");
                return false;
            }

            if (result == null)
            {
                _pendingPastes--;
                PrintWarning($"CopyPaste returned null for '{templateName}'.");
                return false;
            }

            if (result is string err)
            {
                _pendingPastes--;
                PrintWarning($"CopyPaste error for '{templateName}': {err}");
                return false;
            }

            // Success: result is `true`. Real entity list will arrive in OnPasteFinished.
            return true;
        }

        // Fires after CopyPaste finishes spawning all entities for a paste
        private void OnPasteFinished(List<BaseEntity> pastedEntities, string filename, IPlayer player, Vector3 startPos)
        {
            if (_pendingPastes <= 0) return;
            if (pastedEntities == null || pastedEntities.Count == 0) return;
            if (_myTemplateNames == null || !_myTemplateNames.Contains(filename)) return;

            _pendingPastes--;
            ProcessPastedEntities(pastedEntities);
        }

        private void RebuildTemplateIndex()
        {
            _myTemplateNames = new HashSet<string>(
                config.Templates?.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        private void ProcessPastedEntities(List<BaseEntity> entities)
        {
            // Pick one fake identity per pasted base so all "owner-visible" entities agree
            ulong fakeOwner = config.FakeOwnership ? GenerateFakeSteamId() : 0UL;
            string fakeName = config.FakeOwnership ? PickFakeName() : null;

            foreach (var ent in entities)
            {
                if (ent == null || ent.IsDestroyed) continue;

                if (ent.net != null)
                    storedData.EntityIds.Add(ent.net.ID.Value);

                // Sleeping bag → fake deployer name (the "noob who lives here" signal)
                if (ent is SleepingBag bag)
                {
                    if (config.FakeOwnership)
                    {
                        bag.OwnerID = fakeOwner;
                        bag.deployerUserID = fakeOwner;
                        bag.niceName = fakeName;
                        bag.SendNetworkUpdate();
                    }
                    else
                    {
                        bag.OwnerID = 0UL;
                    }
                    continue;
                }

                // Tool cupboard → fake owner + auto-stock loot
                if (ent is BuildingPrivlidge tc)
                {
                    tc.OwnerID = config.FakeOwnership ? fakeOwner : 0UL;
                    if (config.StockCupboard && tc.inventory != null)
                        StockContainer(tc.inventory, config.CupboardLoot);
                    continue;
                }

                // Building blocks / decorations: clear ownership for normal decay
                ent.OwnerID = 0UL;

                if (config.StockBoxes && ent.ShortPrefabName == "woodbox_deployed" && ent is StorageContainer box && box.inventory != null)
                {
                    StockContainer(box.inventory, config.BoxLoot);
                }
                else if (ent.ShortPrefabName == "lantern.deployed" && ent is BaseOven lantern)
                {
                    AddFuelToOven(lantern, 50);
                }
                else if (ent.ShortPrefabName == "campfire" && ent is BaseOven fire)
                {
                    fire.SetFlag(BaseEntity.Flags.On, false);
                }
                else if (ent is CodeLock codeLock && !codeLock.hasCode)
                {
                    codeLock.code = UnityEngine.Random.Range(1000, 9999).ToString();
                    codeLock.hasCode = true;
                    codeLock.SetFlag(BaseEntity.Flags.Locked, true);
                    codeLock.SendNetworkUpdate();
                }
            }
            SaveData();
        }

        private ulong GenerateFakeSteamId()
        {
            // Looks like a real Steam community ID (76561197XXXXXXXXX range)
            return 76561198000000000UL + (ulong)UnityEngine.Random.Range(0, 200_000_000);
        }

        private string PickFakeName()
        {
            if (config.FakeUsernames == null || config.FakeUsernames.Count == 0)
                return "Survivor";
            return config.FakeUsernames[UnityEngine.Random.Range(0, config.FakeUsernames.Count)];
        }

        private void ScheduleStaggeredWipeSpawn()
        {
            int count = config.AutoSpawnOnWipeCount;
            if (count <= 0) return;

            float initialDelay = Math.Max(5f, config.AutoSpawnOnWipeDelay);
            float windowSeconds = Math.Max(0f, config.AutoSpawnOnWipeStaggerMinutes * 60f);

            if (windowSeconds <= 0f)
            {
                // Stagger disabled — burst all at once
                Puts($"Wipe spawn: {count} bases in {initialDelay:F0}s (stagger disabled).");
                timer.Once(initialDelay, () =>
                {
                    if (CopyPaste == null) { PrintWarning("CopyPaste not loaded, skipping."); return; }
                    SpawnRandomBases(count, null);
                });
                return;
            }

            Puts($"Wipe spawn: {count} bases staggered over {config.AutoSpawnOnWipeStaggerMinutes:F0} min " +
                 $"(starts in {initialDelay:F0}s).");

            for (int i = 0; i < count; i++)
            {
                // Even distribution + ±40% jitter so it doesn't look mechanical
                float t = (i + UnityEngine.Random.Range(-0.4f, 0.4f)) / Math.Max(1, count - 1);
                t = Mathf.Clamp01(t);
                float delay = initialDelay + t * windowSeconds;

                timer.Once(delay, () =>
                {
                    if (CopyPaste == null) return;
                    SpawnRandomBases(1, null);
                });
            }
        }

        private void SpawnRandomBases(int count, BasePlayer notify)
        {
            if (TerrainMeta.Size.x <= 0f) return;

            int spawned = 0;
            int attempts = 0;
            int maxAttempts = count * 25;
            float halfMap = (TerrainMeta.Size.x / 2f) - config.MapEdgeBuffer;

            void TryNext()
            {
                if (spawned >= count || attempts >= maxAttempts)
                {
                    if (notify != null && notify.IsConnected)
                        SendReply(notify, $"<color=#55ff55>{spawned}/{count} bases générées sur la map.</color>");
                    Puts($"Random spawn done: {spawned}/{count} (attempts: {attempts}).");
                    return;
                }

                attempts++;

                Vector3 pos = new Vector3(
                    UnityEngine.Random.Range(-halfMap, halfMap),
                    0f,
                    UnityEngine.Random.Range(-halfMap, halfMap)
                );
                pos.y = TerrainY(pos);

                float yaw = UnityEngine.Random.Range(0, 4) * 90f;
                Quaternion baseRot = Quaternion.Euler(0f, yaw, 0f);

                if (!IsValidSpawnPoint(pos, baseRot, out _))
                {
                    timer.Once(0.05f, TryNext);
                    return;
                }

                string template = PickTemplate();
                if (template == null)
                {
                    return;
                }

                if (PasteAndTrack(template, pos, yaw)) spawned++;

                timer.Once(config.RandomPasteDelay, TryNext);
            }

            TryNext();
        }

        private void StockContainer(ItemContainer container, List<LootEntry> pool)
        {
            if (container == null || pool == null) return;
            foreach (var entry in pool)
            {
                if (string.IsNullOrEmpty(entry.Shortname)) continue;
                if (UnityEngine.Random.value > entry.Chance) continue;
                int qty = UnityEngine.Random.Range(entry.Min, entry.Max + 1);
                if (qty <= 0) continue;
                var def = ItemManager.FindItemDefinition(entry.Shortname);
                if (def == null) continue;
                var item = ItemManager.Create(def, qty);
                if (item == null) continue;
                if (!item.MoveToContainer(container))
                    item.Remove();
            }
        }

        private void AddFuelToOven(BaseEntity ent, int amount)
        {
            if (ent is not BaseOven oven || oven.inventory == null) return;
            var def = ItemManager.FindItemDefinition("lowgradefuel");
            if (def == null) return;
            var item = ItemManager.Create(def, amount);
            if (item == null) return;
            if (!item.MoveToContainer(oven.inventory))
                item.Remove();
        }

        #endregion

        #region Cleanup

        private int CleanupAllBases(bool silent = false)
        {
            int removed = 0;
            foreach (var id in storedData.EntityIds.ToArray())
            {
                var ent = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
                if (ent != null && !ent.IsDestroyed)
                {
                    ent.Kill();
                    removed++;
                }
            }
            storedData.EntityIds.Clear();
            SaveData();
            if (!silent) Puts($"{removed} fake base entities removed.");
            return removed;
        }

        #endregion

        #region Night Lighting

        private void UpdateNightLighting()
        {
            if (TOD_Sky.Instance == null) return;
            bool isNight = TOD_Sky.Instance.IsNight;

            foreach (var id in storedData.EntityIds.ToArray())
            {
                var ent = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
                if (ent == null || ent.IsDestroyed) continue;
                if (ent.ShortPrefabName != "lantern.deployed") continue;
                if (ent is not BaseOven oven) continue;

                if (isNight)
                {
                    if (!oven.IsOn())
                    {
                        if (oven.inventory == null || oven.inventory.itemList.Count == 0)
                            AddFuelToOven(oven, 50);
                        oven.SetFlag(BaseEntity.Flags.On, true);
                    }
                }
                else if (oven.IsOn())
                {
                    oven.SetFlag(BaseEntity.Flags.On, false);
                }
            }
        }

        #endregion
    }
}
