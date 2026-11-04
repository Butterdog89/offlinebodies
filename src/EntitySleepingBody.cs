using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Newtonsoft.Json.Linq;

namespace OfflineBodies
{
    /// <summary>
    /// The body a player leaves behind when they log out. It can't be hurt, anyone can open it
    /// like a chest, and sneak + right-click picks it up to drag it.
    /// Structure based on EntityPlayerCorpse from Death Corpses (MIT, DArkHekRoMaNT and contributors).
    /// </summary>
    public class EntitySleepingBody : EntityAgent
    {
        private const int PacketClose = 1001;
        private const float DragReleaseDistance = 6f;
        private const double DragFollowDistance = 2.0;
        private const string DragStatCode = "offlinebodies-drag";
        /// <summary>Added to the dragger's walk speed stat; -0.35 means 35% slower.</summary>
        private const float DragWalkSpeedPenalty = -0.35f;

        /// <summary>True if this player is dragging any loaded sleeping body.</summary>
        public static bool IsDragging(IWorldAccessor world, EntityAgent player, string playerUid)
        {
            // A dragged body is never more than DragReleaseDistance away from its dragger.
            float r = DragReleaseDistance + 2;
            return world.GetEntitiesAround(player.Pos.XYZ, r, r, e => e is EntitySleepingBody b && b.DraggedBy == playerUid).Length > 0;
        }

        private TreeAttribute? _invTree;
        private GuiDialogBodyContents? _dialog;
        private readonly HashSet<string> _viewers = new();
        private float _dragSaveTimer;

        public InventoryGeneric? Inventory { get; private set; }

        public string BodyId
        {
            get => WatchedAttributes.GetString("bodyId", "");
            set => WatchedAttributes.SetString("bodyId", value);
        }

        public string OwnerUID
        {
            get => WatchedAttributes.GetString("ownerUID", "");
            set => WatchedAttributes.SetString("ownerUID", value);
        }

        public string OwnerName
        {
            get => WatchedAttributes.GetString("ownerName", "?");
            set => WatchedAttributes.SetString("ownerName", value);
        }

        public string LoggedOutAtReal
        {
            get => WatchedAttributes.GetString("loggedOutAt", "");
            set => WatchedAttributes.SetString("loggedOutAt", value);
        }

        /// <summary>UID of the player dragging this body, or empty.</summary>
        public string DraggedBy
        {
            get => WatchedAttributes.GetString("draggedBy", "");
            set => WatchedAttributes.SetString("draggedBy", value);
        }

        public string[] Origins
        {
            get => (WatchedAttributes.GetString("origins") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
            set => WatchedAttributes.SetString("origins", string.Join("|", value));
        }

        // Bodies are never hurt, never pushed by other entities and never despawn on their own.
        public override bool ShouldReceiveDamage(DamageSource damageSource, float damage) => false;
        public override bool ReceiveDamage(DamageSource damageSource, float damage) => false;
        public override bool IsInteractable => true;
        public override bool StoreWithChunk => true;
        public override bool AllowOutsideLoadedRange => false;

        public void SetContents(InventoryGeneric inv, string[] origins)
        {
            Origins = origins;
            _invTree = new TreeAttribute();
            inv.ToTreeAttributes(_invTree);
            WatchedAttributes["bodyInv"] = _invTree;
            Inventory = inv;
        }

        public void CopyAppearanceFrom(EntityPlayer player)
        {
            // Skin, hair, eyes etc. are kept in skinConfig; copy it so the body looks like its owner.
            if (player.WatchedAttributes["skinConfig"] is ITreeAttribute skin)
            {
                WatchedAttributes["skinConfig"] = skin.Clone();
            }
        }

        /// <summary>
        /// Shows the owner's clothes and armor on the body. These are display copies only:
        /// the real clothes stay on the player and this inventory can't be opened or dropped.
        /// Call after the body has spawned.
        /// </summary>
        public void ApplyDisplayGear(IPlayer owner)
        {
            var gear = GetBehavior<EntityBehaviorSeraphInventory>();
            var src = owner.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (gear?.Inventory == null || src == null) return;
            int n = Math.Min(gear.Inventory.Count, src.Count);
            for (int i = 0; i < n; i++)
            {
                gear.Inventory[i].Itemstack = src[i].Itemstack?.Clone();
            }
            gear.storeInv();
        }

        private static readonly AssetLocation PlayerCode = new("game", "player");

        /// <summary>The body uses the player's skin options; copy them from the player entity type once.</summary>
        private static void EnsureSkinnableParts(EntityProperties properties, ICoreAPI api)
        {
            if (properties.Attributes?["skinnableParts"].Exists == true) return;
            var playerType = api.World.GetEntityType(PlayerCode);
            var parts = playerType?.Attributes?["skinnableParts"];
            if (parts == null || !parts.Exists) return;
            var jo = properties.Attributes?.Token as JObject ?? new JObject();
            jo["skinnableParts"] = parts.Token.DeepClone();
            properties.Attributes = new JsonObject(jo);
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            EnsureSkinnableParts(properties, api);
            base.Initialize(properties, api, InChunkIndex3d);
            LoadInventory();

            if (api.Side == EnumAppSide.Client)
            {
                WatchedAttributes.RegisterModifiedListener("bodyInv", () =>
                {
                    if (Inventory != null && WatchedAttributes["bodyInv"] is TreeAttribute t)
                    {
                        Inventory.FromTreeAttributes(t);
                        Inventory.ResolveBlocksOrItems();
                    }
                });
            }
        }

        private void LoadInventory()
        {
            if (WatchedAttributes["bodyInv"] is not TreeAttribute t)
            {
                t = new TreeAttribute();
                WatchedAttributes["bodyInv"] = t;
            }
            _invTree = t;

            if (Inventory == null)
            {
                Inventory = new InventoryGeneric(Math.Max(1, t.GetInt("qslots", 1)), $"{OfflineBodiesSystem.ModId}-{BodyId}", Api);
                Inventory.FromTreeAttributes(t);
            }
            Inventory.Api = Api;
            Inventory.ResolveBlocksOrItems();

            if (Api.Side == EnumAppSide.Server)
            {
                Inventory.SlotModified += OnSlotModified;
            }
        }

        private void OnSlotModified(int slotId)
        {
            if (Inventory == null || _invTree == null) return;
            Inventory.ToTreeAttributes(_invTree);
            WatchedAttributes.MarkPathDirty("bodyInv");
            OfflineBodiesSystem.Instance?.OnBodyChanged(this);
        }

        // ---------- interaction ----------

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
        {
            if (mode != EnumInteractMode.Interact || byEntity is not EntityPlayer ep || !ep.Alive)
            {
                // Left clicks do nothing at all.
                return;
            }

            bool sneaking = ep.Controls.ShiftKey || ep.Controls.Sneak;

            if (Api.Side == EnumAppSide.Server)
            {
                var player = Api.World.PlayerByUid(ep.PlayerUID) as IServerPlayer;
                if (player == null) return;
                if (sneaking) ToggleDrag(player);
                else OpenFor(player);
            }
            else if (!sneaking)
            {
                OpenDialogClient();
            }
        }

        private void OpenFor(IServerPlayer player)
        {
            if (Inventory == null) return;
            if (OfflineBodiesSystem.Instance?.IsValidBody(this) != true) return;
            player.InventoryManager.OpenInventory(Inventory);
            _viewers.Add(player.PlayerUID);
        }

        private void OpenDialogClient()
        {
            if (Api is not ICoreClientAPI capi || Inventory == null) return;
            if (_dialog?.IsOpened() == true) return;
            _dialog = new GuiDialogBodyContents(capi, this);
            _dialog.TryOpen();
        }

        public void SendClosePacket()
        {
            if (Api is ICoreClientAPI capi)
            {
                capi.Network.SendEntityPacket(EntityId, PacketClose, null);
            }
        }

        public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data)
        {
            if (packetid == PacketClose)
            {
                if (Inventory != null) player.InventoryManager.CloseInventory(Inventory);
                _viewers.Remove(player.PlayerUID);
                return;
            }

            // Item moves from the slot grid.
            if (packetid < 1000 && Inventory != null)
            {
                if (!_viewers.Contains(player.PlayerUID)) return;
                if (player.Entity.Pos.DistanceTo(Pos) > 6) return;
                Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                return;
            }

            base.OnReceivedClientPacket(player, packetid, data);
        }

        /// <summary>Closes the loot window for everyone looking (used when the owner wakes up).</summary>
        public void CloseForEveryone()
        {
            if (Inventory == null || Api is not ICoreServerAPI sapi) return;
            foreach (var uid in _viewers)
            {
                if (sapi.World.PlayerByUid(uid) is IServerPlayer p)
                {
                    p.InventoryManager.CloseInventory(Inventory);
                }
            }
            _viewers.Clear();
        }

        // ---------- dragging ----------

        private void ToggleDrag(IServerPlayer player)
        {
            if (DraggedBy == player.PlayerUID)
            {
                StopDrag(player);
                return;
            }
            if (!string.IsNullOrEmpty(DraggedBy) && Api.World.PlayerByUid(DraggedBy)?.Entity?.Alive == true)
            {
                player.SendIngameError("busy", Lang.Get("offlinebodies:drag-busy"));
                return;
            }
            DraggedBy = player.PlayerUID;
            // Dragging a body is heavy work: slow the dragger down (removed again in StopDrag).
            player.Entity?.Stats.Set("walkspeed", DragStatCode, DragWalkSpeedPenalty, false);
            player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("offlinebodies:drag-start", OwnerName), EnumChatType.Notification);
        }

        private void StopDrag(IServerPlayer? player)
        {
            // Also look the dragger up by UID, so the slowdown is removed even when no message is sent.
            var dragger = player ?? Api.World.PlayerByUid(DraggedBy) as IServerPlayer;
            dragger?.Entity?.Stats.Remove("walkspeed", DragStatCode);
            DraggedBy = "";
            player?.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("offlinebodies:drag-stop", OwnerName), EnumChatType.Notification);
            OfflineBodiesSystem.Instance?.OnBodyChanged(this);
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (Api.Side != EnumAppSide.Server) return;

            string dragger = DraggedBy;
            if (string.IsNullOrEmpty(dragger)) return;

            var player = Api.World.PlayerByUid(dragger) as IServerPlayer;
            var pe = player?.Entity;
            if (player == null || pe == null || !pe.Alive || player.ConnectionState != EnumClientState.Playing
                || pe.Pos.Dimension != Pos.Dimension
                || pe.Pos.DistanceTo(Pos) > DragReleaseDistance)
            {
                StopDrag(player?.ConnectionState == EnumClientState.Playing ? player : null);
                return;
            }

            // Follow two blocks behind the dragger, at their height. If that spot is blocked,
            // try one block behind, and if that is blocked too, pull the body onto their feet.
            double yaw = pe.Pos.Yaw;
            double ty = pe.Pos.Y;
            double tx = pe.Pos.X, tz = pe.Pos.Z;
            foreach (double dist in new[] { DragFollowDistance, 1.0 })
            {
                double cx = pe.Pos.X - Math.Sin(yaw) * dist;
                double cz = pe.Pos.Z - Math.Cos(yaw) * dist;
                var block = Api.World.BlockAccessor.GetBlock(new BlockPos((int)Math.Floor(cx), (int)Math.Floor(ty + 0.1), (int)Math.Floor(cz), pe.Pos.Dimension));
                if (block.CollisionBoxes == null || block.CollisionBoxes.Length == 0)
                {
                    tx = cx; tz = cz;
                    break;
                }
            }

            if (Pos.SquareDistanceTo(tx, ty, tz) > 0.01)
            {
                Pos.SetPos(tx, ty, tz);
                Pos.Yaw = pe.Pos.Yaw;
            }

            // Save the new spot every few seconds while dragging.
            _dragSaveTimer += dt;
            if (_dragSaveTimer > 3)
            {
                _dragSaveTimer = 0;
                OfflineBodiesSystem.Instance?.OnBodyChanged(this);
            }
        }

        // ---------- removal ----------

        /// <summary>The owner is logging in: stop any drag and close every loot window first.</summary>
        public void PrepareForWakeUp()
        {
            if (!string.IsNullOrEmpty(DraggedBy)) StopDrag(null);
            CloseForEveryone();
        }

        public void ReleaseDrag(IServerPlayer player)
        {
            if (DraggedBy == player.PlayerUID) StopDrag(player);
        }

        /// <summary>Removes the body without dropping anything (its items went back to the owner, or it is a stale copy).</summary>
        public void RemoveQuietly()
        {
            CloseForEveryone();
            if (Api.Side == EnumAppSide.Server && !string.IsNullOrEmpty(DraggedBy))
            {
                StopDrag(null);
            }
            if (Inventory != null)
            {
                foreach (var slot in Inventory) slot.Itemstack = null;
            }
            var gear = GetBehavior<EntityBehaviorSeraphInventory>()?.Inventory;
            if (gear != null)
            {
                foreach (var slot in gear) slot.Itemstack = null;
            }
            Die(EnumDespawnReason.Removed);
        }

        public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource? damageSourceForDeath = null)
        {
            // A body can only be removed on purpose; anything else (for example a /kill command) is ignored
            // so the items are never dropped or lost.
            if (reason != EnumDespawnReason.Removed) return;
            base.Die(reason, damageSourceForDeath);
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            if (Api.Side == EnumAppSide.Client)
            {
                _dialog?.TryClose();
            }
        }

        // ---------- display ----------

        public override string GetName() => Lang.Get("offlinebodies:body-name", OwnerName);

        public override string GetInfoText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Lang.Get("offlinebodies:body-info", LoggedOutAtReal));
            if (!string.IsNullOrEmpty(DraggedBy))
            {
                sb.AppendLine(Lang.Get("offlinebodies:body-dragged-by", Api.World.PlayerByUid(DraggedBy)?.PlayerName ?? "?"));
            }
            return sb.ToString();
        }

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
        {
            return
            [
                new WorldInteraction { ActionLangCode = "offlinebodies:help-loot", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "offlinebodies:help-drag", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" }
            ];
        }
    }
}
