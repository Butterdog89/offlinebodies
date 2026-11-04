using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace OfflineBodies
{
    /// <summary>
    /// Server side bookkeeping for sleeping bodies.
    ///
    /// Rules that keep items safe (no loss, no duplication):
    /// 1. Every body has a record stored in the world save. The record holds the same inventory
    ///    object as the body and is saved in the same world save as the chunks and player data.
    /// 2. A body entity is only valid while its BodyId matches the record. Any other body that
    ///    loads (for example an old copy after a crash) is removed without dropping its items.
    /// 3. When the owner logs back in, items come from the live body if it can be found,
    ///    otherwise from the record. The record is deleted afterwards.
    /// </summary>
    public class OfflineBodiesSystem : ModSystem
    {
        public const string ModId = "offlinebodies";
        public const string EntityCode = "offlinebodies:sleepingbody";
        public const string ChannelName = "offlinebodies";

        private ICoreServerAPI? _sapi;
        private readonly Dictionary<string, BodyRecord> _records = new();
        
        public static OfflineBodiesSystem? Instance { get; private set; }

        public override void Start(ICoreAPI api)
        {
            Instance = this;
            api.RegisterEntity("EntitySleepingBody", typeof(EntitySleepingBody));
            api.Network.RegisterChannel(ChannelName).RegisterMessageType<ReleaseDragPacket>();
        }

        // ---------- hands full while dragging ----------

        public override void StartClientSide(ICoreClientAPI capi)
        {
            var channel = capi.Network.GetChannel(ChannelName);
            capi.Input.InWorldAction += (EnumEntityAction action, bool on, ref EnumHandling handled) =>
            {
                var player = capi.World.Player;
                var self = player?.Entity;
                if (self == null || !on) return;
                if (action != EnumEntityAction.InWorldLeftMouseDown && action != EnumEntityAction.InWorldRightMouseDown) return;
                if (!EntitySleepingBody.IsDragging(capi.World, self, player!.PlayerUID)) return;

                bool sneaking = self.Controls.ShiftKey || self.Controls.Sneak;
                if (action == EnumEntityAction.InWorldRightMouseDown)
                {
                    if (sneaking)
                    {
                        // Let go, wherever you are looking. No need to aim at the body.
                        handled = EnumHandling.PreventDefault;
                        channel.SendPacket(new ReleaseDragPacket());
                        return;
                    }
                    // Doors are the one thing you can still use while your hands are full.
                    var sel = player.CurrentBlockSelection;
                    if (sel != null && IsDoor(capi.World.BlockAccessor.GetBlock(sel.Position))) return;
                }

                handled = EnumHandling.PreventDefault;
                capi.TriggerIngameError(this, "handsfull", Lang.Get("offlinebodies:hands-full"));
            };
        }

        private void OnPlayerJoinHandsFull(IServerPlayer player)
        {
            player.InWorldAction += (EnumEntityAction action, bool on, ref EnumHandling handled) =>
            {
                if (player.Entity == null) return;
                if (BlocksAction(player.Entity.World, player.PlayerUID, player.Entity, player.CurrentBlockSelection, action, on))
                {
                    handled = EnumHandling.PreventDefault;
                }
            };
        }

        /// <summary>
        /// While dragging, left and right clicks in the world do nothing (no mining, attacking, placing or using items).
        /// Exceptions: sneak + right-click lets go of the body, and right-clicking a door still opens it.
        /// </summary>
        private static bool BlocksAction(IWorldAccessor world, string uid, EntityAgent self, BlockSelection? blockSel, EnumEntityAction action, bool on)
        {
            if (!on) return false;
            if (action != EnumEntityAction.InWorldLeftMouseDown && action != EnumEntityAction.InWorldRightMouseDown) return false;
            if (!EntitySleepingBody.IsDragging(world, self, uid)) return false;
            bool sneaking = self.Controls.ShiftKey || self.Controls.Sneak;
            if (action == EnumEntityAction.InWorldRightMouseDown)
            {
                // Sneak + right-click is how you let go; the client turns it into a release packet.
                if (sneaking) return false;
                // Doors stay usable while carrying a body.
                if (blockSel != null && IsDoor(world.BlockAccessor.GetBlock(blockSel.Position))) return false;
            }
            return true;
        }

        /// <summary>
        /// Authoritative block gate: while a player is carrying a body, the server refuses to let them
        /// use (right-click) or break/build any block, so beds, chests and the like do nothing.
        /// Doors are the single exception. This runs even when the client-side block is bypassed.
        /// </summary>
        private EnumWorldAccessResponse OnTestBlockAccess(IPlayer player, BlockSelection blockSel, EnumBlockAccessFlags accessType, ref string claimant, EnumWorldAccessResponse response)
        {
            if (response == EnumWorldAccessResponse.Denied) return response;
            var self = player?.Entity;
            if (self == null || blockSel == null) return response;
            if (!EntitySleepingBody.IsDragging(self.World, self, player!.PlayerUID)) return response;

            // Opening/using a door is allowed; everything else is refused while the hands are full.
            if (accessType.HasFlag(EnumBlockAccessFlags.Use) && IsDoor(self.World.BlockAccessor.GetBlock(blockSel.Position)))
            {
                return response;
            }
            return EnumWorldAccessResponse.Denied;
        }

        /// <summary>True for door and trapdoor blocks (matched by block code), which stay usable while carrying.</summary>
        private static bool IsDoor(Block? block)
        {
            var path = block?.Code?.Path;
            return path != null && path.Contains("door");
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            api.Network.GetChannel(ChannelName).SetMessageHandler<ReleaseDragPacket>((player, _) =>
            {
                foreach (var body in api.World.LoadedEntities.Values.OfType<EntitySleepingBody>())
                {
                    if (body.DraggedBy == player.PlayerUID) body.ReleaseDrag(player);
                }
            });
            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.PlayerDisconnect += OnPlayerDisconnect;
            // Wake up while the player is still on the loading screen, so nobody sees (or drags) the
            // body and the standing player at the same time, and the client never has to jump.
            api.Event.PlayerJoin += OnPlayerJoinWakeUp;
            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            api.Event.PlayerJoin += OnPlayerJoinHandsFull;
            api.Event.OnTestBlockAccess += OnTestBlockAccess;
            api.Event.OnEntityLoaded += OnEntityAppeared;
            api.Event.OnEntitySpawn += OnEntityAppeared;

            api.ChatCommands.Create("offlinebodies")
                .WithDescription("List sleeping bodies")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(_ =>
                {
                    if (_records.Count == 0) return TextCommandResult.Success("No sleeping bodies.");
                    var lines = _records.Values.Select(r =>
                        $"{r.OwnerName}: {r.Inventory.Count(s => !s.Empty)} stacks at {(int)r.X} {(int)r.Y} {(int)r.Z}");
                    return TextCommandResult.Success(string.Join("\n", lines));
                });
        }

        public override void Dispose()
        {
            if (Instance == this) Instance = null;
        }

        // ---------- persistence ----------
        // Records live in the world save itself, so they are always saved together with the
        // player data and the chunks. After a crash all three roll back to the same autosave.

        private const string SaveKey = "offlinebodies-records";

        private void OnSaveGameLoaded()
        {
            var sapi = _sapi!;
            byte[]? data = sapi.WorldManager.SaveGame.GetData(SaveKey);
            if (data != null)
            {
                try
                {
                    var root = new TreeAttribute();
                    root.FromBytes(data);
                    foreach (var entry in root)
                    {
                        if (entry.Value is not TreeAttribute t) continue;
                        var rec = BodyRecord.FromTree(t, sapi);
                        _records[rec.OwnerUID] = rec;
                    }
                }
                catch (Exception e)
                {
                    Mod.Logger.Error($"Could not read sleeping body records: {e}");
                }
            }
            sapi.Event.GameWorldSave += OnGameWorldSave;
            Mod.Logger.Notification($"Loaded {_records.Count} sleeping body record(s)");
        }

        private void OnGameWorldSave()
        {
            var root = new TreeAttribute();
            int i = 0;
            foreach (var rec in _records.Values)
            {
                try
                {
                    root[(i++).ToString()] = rec.ToTree();
                }
                catch (Exception e)
                {
                    Mod.Logger.Error($"Failed to save the body record of {rec.OwnerName}: {e}");
                }
            }
            _sapi!.WorldManager.SaveGame.StoreData(SaveKey, root.ToBytes());
        }

        private void SaveRecord(BodyRecord rec)
        {
            // Nothing to do right away: records are written with the next world save (see OnGameWorldSave).
        }

        private void DeleteRecord(string uid)
        {
            _records.Remove(uid);
        }

        // ---------- logout: create the body ----------

        private void OnPlayerDisconnect(IServerPlayer player)
        {
            var sapi = _sapi!;
            var pe = player.Entity;
            if (pe == null || !pe.Alive) return;
            if (player.WorldData.CurrentGameMode != EnumGameMode.Survival) return;

            if (_records.ContainsKey(player.PlayerUID))
            {
                Mod.Logger.Warning($"{player.PlayerName} logged out but already has a body record; not creating a second body.");
                return;
            }

            var type = sapi.World.GetEntityType(new AssetLocation(EntityCode));
            if (type == null)
            {
                Mod.Logger.Error($"Entity type {EntityCode} is missing, cannot create a body for {player.PlayerName}");
                return;
            }

            try
            {
                var body = (EntitySleepingBody)sapi.World.ClassRegistry.CreateEntity(type);
                body.BodyId = Guid.NewGuid().ToString();
                body.OwnerUID = player.PlayerUID;
                body.OwnerName = player.PlayerName;
                body.LoggedOutAtReal = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                body.CopyAppearanceFrom(pe);

                var (inv, origins) = TakeItemsFromPlayer(player, body.BodyId);
                body.SetContents(inv, origins);

                body.Pos.SetFrom(pe.Pos);
                body.Pos.SetFrom(pe.Pos);
                body.Pos.Pitch = 0;
                body.Pos.Roll = 0;

                var rec = new BodyRecord
                {
                    OwnerUID = player.PlayerUID,
                    OwnerName = player.PlayerName,
                    BodyId = body.BodyId,
                    X = pe.Pos.X, Y = pe.Pos.Y, Z = pe.Pos.Z,
                    Inventory = inv,
                    Origins = origins
                };
                // Record first, so the spawn check below accepts the body.
                _records[player.PlayerUID] = rec;
                SaveRecord(rec);

                sapi.World.SpawnEntity(body);
                body.ApplyDisplayGear(player);
                Mod.Logger.Notification($"{player.PlayerName} fell asleep at {(int)rec.X} {(int)rec.Y} {(int)rec.Z} with {inv.Count(s => !s.Empty)} stacks");
            }
            catch (Exception e)
            {
                Mod.Logger.Error($"Failed to create a body for {player.PlayerName}: {e}");
            }
        }

        /// <summary>
        /// Moves the hotbar and the contents of the backpacks into a new inventory.
        /// The bags themselves and the clothes/armor stay on the player.
        /// </summary>
        private (InventoryGeneric inv, string[] origins) TakeItemsFromPlayer(IServerPlayer player, string bodyId)
        {
            var sources = new List<ItemSlot>();
            var origins = new List<string>();

            void Collect(string invClass, System.Func<ItemSlot, bool> include)
            {
                var src = player.InventoryManager.GetOwnInventory(invClass);
                if (src == null) return;
                for (int i = 0; i < src.Count; i++)
                {
                    var slot = src[i];
                    if (slot == null || !include(slot)) continue;
                    sources.Add(slot);
                    origins.Add($"{invClass}:{i}");
                }
            }

            Collect(GlobalConstants.hotBarInvClassName, _ => true);
            // Bag slots hold the bags; moving a bag would move its contents with it, so skip them
            // and take the contents slot by slot instead.
            Collect(GlobalConstants.backpackInvClassName, slot => slot is not ItemSlotBackpack);

            var inv = new InventoryGeneric(Math.Max(1, sources.Count), $"{ModId}-{bodyId}", _sapi);
            for (int i = 0; i < sources.Count; i++)
            {
                var slot = sources[i];
                if (slot.Empty) continue;
                inv[i].Itemstack = slot.TakeOutWhole();
                slot.MarkDirty();
            }
            return (inv, origins.ToArray());
        }

        // ---------- login: wake up ----------

        private readonly Dictionary<string, string> _pendingWakeMessages = new();

        private void OnPlayerJoinWakeUp(IServerPlayer player)
        {
            TryWakeUp(player, clientReady: false);
        }

        private void OnPlayerNowPlaying(IServerPlayer player)
        {
            // Chat only shows once the player is in the world, so the wake-up message waits until now.
            if (_pendingWakeMessages.Remove(player.PlayerUID, out var langKey))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get(langKey), EnumChatType.Notification);
            }
            // Fallback in case the body could not be handled at join (its chunk was still loading,
            // or it was dragged away and the owner has to be moved, which must wait until now).
            TryWakeUp(player, clientReady: true);
        }

        /// <param name="clientReady">True once the player's game has finished loading (PlayerNowPlaying).</param>
        private void TryWakeUp(IServerPlayer player, bool clientReady)
        {
            if (!_records.TryGetValue(player.PlayerUID, out var rec)) return;

            var body = FindBody(rec);
            if (body != null)
            {
                // If the body was dragged away, waking up means moving the owner. A teleport sent while
                // the player is still on the loading screen crashes their game (NullReferenceException in
                // Entity.OnReceivedServerPacket), so in that case wait for PlayerNowPlaying.
                if (!clientReady && NeedsMove(player, body.Pos.X, body.Pos.Y, body.Pos.Z)) return;
                WakeUp(player, rec, body);
                return;
            }

            // The body may sit in a chunk that is not loaded (it was dragged away). Load it and look again.
            var sapi = _sapi!;
            int cs = GlobalConstants.ChunkSize;
            sapi.WorldManager.LoadChunkColumnPriority((int)rec.X / cs, (int)rec.Z / cs, new ChunkLoadOptions
            {
                OnLoaded = () => sapi.Event.RegisterCallback(_ =>
                {
                    if (player.ConnectionState == EnumClientState.Offline) return;
                    if (!_records.TryGetValue(player.PlayerUID, out var rec2) || rec2.BodyId != rec.BodyId) return;
                    var body2 = FindBody(rec2);
                    // Same rule as above: never move a player who is still loading. PlayerNowPlaying tries again.
                    bool ready = clientReady || player.ConnectionState == EnumClientState.Playing;
                    double tx = body2?.Pos.X ?? rec2.X, ty = body2?.Pos.Y ?? rec2.Y, tz = body2?.Pos.Z ?? rec2.Z;
                    if (!ready && NeedsMove(player, tx, ty, tz)) return;
                    WakeUp(player, rec2, body2);
                }, 500)
            });
        }

        private EntitySleepingBody? FindBody(BodyRecord rec)
        {
            return _sapi!.World.LoadedEntities.Values
                .OfType<EntitySleepingBody>()
                .FirstOrDefault(b => b.BodyId == rec.BodyId && b.Alive);
        }

        private void WakeUp(IServerPlayer player, BodyRecord rec, EntitySleepingBody? body)
        {
            InventoryGeneric source;
            string[] origins;
            bool fromBackup = body == null;

            if (body != null)
            {
                body.PrepareForWakeUp();
                source = body.Inventory!;
                origins = body.Origins;
                MoveOwnerTo(player, body.Pos.X, body.Pos.Y, body.Pos.Z);
            }
            else
            {
                Mod.Logger.Warning($"Body of {player.PlayerName} not found, restoring from the saved record");
                source = rec.Inventory;
                origins = rec.Origins;
                MoveOwnerTo(player, rec.X, rec.Y, rec.Z);
            }

            int restored = RestoreItems(player, source, origins);

            // Remove the record before the body so a crash in between can never give items twice:
            // without a record, a leftover body is deleted when it loads.
            DeleteRecord(player.PlayerUID);
            body?.RemoveQuietly();

            Mod.Logger.Notification($"{player.PlayerName} woke up, {restored} stacks returned (from {(fromBackup ? "backup" : "body")})");
            string msgKey = fromBackup ? "offlinebodies:woke-up-restored" : "offlinebodies:woke-up";
            if (player.ConnectionState == EnumClientState.Playing)
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get(msgKey), EnumChatType.Notification);
            }
            else
            {
                _pendingWakeMessages[player.PlayerUID] = msgKey;
            }
        }

        /// <summary>
        /// Puts the owner where the body lies. Skips the teleport when they are already there
        /// (the body was not dragged), and keeps the direction they were facing.
        /// </summary>
        private static void MoveOwnerTo(IServerPlayer player, double x, double y, double z)
        {
            var pe = player.Entity;
            if (pe == null) return;
            if (!NeedsMove(player, x, y, z)) return;
            pe.TeleportToDouble(x, y, z);
        }

        private static bool NeedsMove(IServerPlayer player, double x, double y, double z)
        {
            var pe = player.Entity;
            return pe != null && pe.Pos.SquareDistanceTo(x, y, z) >= 0.25;
        }

        private int RestoreItems(IServerPlayer player, InventoryGeneric source, string[] origins)
        {
            int count = 0;
            for (int i = 0; i < source.Count; i++)
            {
                var stack = source[i].Itemstack;
                if (stack == null) continue;
                source[i].Itemstack = null;
                count++;

                ItemSlot? target = null;
                if (i < origins.Length)
                {
                    var parts = origins[i].Split(':');
                    var inv = player.InventoryManager.GetOwnInventory(parts[0]);
                    if (inv != null && int.TryParse(parts[1], out int idx) && idx < inv.Count) target = inv[idx];
                }

                if (target != null && target.Empty && target.CanHold(new DummySlot(stack)))
                {
                    target.Itemstack = stack;
                    target.MarkDirty();
                    continue;
                }

                if (!player.InventoryManager.TryGiveItemstack(stack, true) || stack.StackSize > 0)
                {
                    if (stack.StackSize > 0)
                    {
                        _sapi!.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.Add(0, 0.5, 0));
                    }
                }
            }
            return count;
        }

        // ---------- keeping the record in sync ----------

        public void OnBodyChanged(EntitySleepingBody body)
        {
            if (_sapi == null) return;
            if (!_records.TryGetValue(body.OwnerUID, out var rec) || rec.BodyId != body.BodyId) return;
            rec.Inventory = body.Inventory!;
            rec.Origins = body.Origins;
            rec.X = body.Pos.X; rec.Y = body.Pos.Y; rec.Z = body.Pos.Z;
            SaveRecord(rec);
        }

        private void OnEntityAppeared(Entity entity)
        {
            if (entity is not EntitySleepingBody body || _sapi == null) return;
            if (_records.TryGetValue(body.OwnerUID ?? "", out var rec) && rec.BodyId == body.BodyId) return;

            // A stale copy (the owner already woke up, or a newer body exists). Remove it without dropping items.
            Mod.Logger.Notification($"Removing stale sleeping body of {body.OwnerName} ({body.BodyId})");
            _sapi.Event.EnqueueMainThreadTask(() => body.RemoveQuietly(), "offlinebodies-removestale");
        }

        public bool IsValidBody(EntitySleepingBody body)
        {
            return _records.TryGetValue(body.OwnerUID ?? "", out var rec) && rec.BodyId == body.BodyId;
        }
    }

    public class BodyRecord
    {
        public string OwnerUID = "";
        public string OwnerName = "";
        public string BodyId = "";
        public double X, Y, Z;
        public InventoryGeneric Inventory = null!;
        public string[] Origins = Array.Empty<string>();

        public TreeAttribute ToTree()
        {
            var tree = new TreeAttribute();
            tree.SetInt("version", 1);
            tree.SetString("ownerUID", OwnerUID);
            tree.SetString("ownerName", OwnerName);
            tree.SetString("bodyId", BodyId);
            tree.SetDouble("x", X); tree.SetDouble("y", Y); tree.SetDouble("z", Z);
            tree.SetString("origins", string.Join("|", Origins));
            var invTree = new TreeAttribute();
            Inventory.ToTreeAttributes(invTree);
            tree["inventory"] = invTree;
            return tree;
        }

        public static BodyRecord FromTree(TreeAttribute tree, ICoreAPI api)
        {
            var invTree = (TreeAttribute)tree["inventory"];
            var rec = new BodyRecord
            {
                OwnerUID = tree.GetString("ownerUID"),
                OwnerName = tree.GetString("ownerName"),
                BodyId = tree.GetString("bodyId"),
                X = tree.GetDouble("x"), Y = tree.GetDouble("y"), Z = tree.GetDouble("z"),
                Origins = (tree.GetString("origins") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries)
            };
            rec.Inventory = new InventoryGeneric(Math.Max(1, invTree.GetInt("qslots", 1)), $"{OfflineBodiesSystem.ModId}-{rec.BodyId}", api);
            rec.Inventory.FromTreeAttributes(invTree);
            rec.Inventory.ResolveBlocksOrItems();
            return rec;
        }
    }

    [ProtoBuf.ProtoContract]
    public class ReleaseDragPacket
    {
        [ProtoBuf.ProtoMember(1)]
        public bool Release = true;
    }
}
